using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;
#pragma warning disable CS0618

namespace PlayerService.Controllers;

[ApiController, Route("player/v2"), RequireAuth, UseMongoTransaction]
[SuppressMessage("ReSharper", "CoVariantArrayConversion")]
public class TopController : PlatformController
{

#pragma warning disable
	private readonly PlayerAccountService _playerService;
	private readonly DC2Service _dc2Service;
	private readonly DiscriminatorService _discriminatorService;
	private readonly ItemService _itemService;
	private readonly NameGeneratorService _nameGeneratorService;
	private readonly AuditService _auditService;
	
	// Component Services
	private readonly AbTestService _abTestService;
	private readonly AccountService _accountService;
	private readonly EquipmentService _equipmentService;
	private readonly HeroService _heroService;
	private readonly MultiplayerService _multiplayerService;
	private readonly QuestService _questService;
	private readonly StoreService _storeService;
	private readonly SummaryService _summaryService;
	private readonly TutorialService _tutorialService;
	private readonly WalletService _walletService;
	private readonly WorldService _worldService;
#pragma warning restore
	private Dictionary<string, ComponentService> ComponentServices { get; init; }
	
	/// <summary>
	/// Will on 2021.12.16
	/// Normally, so many newlines for a constructor is discouraged.  However, for something that requires so many
	/// different services, I'm willing to make an exception for readability.
	///
	/// I don't think breaking all of a player's data into so many components was good design philosophy; while it
	/// was clearly done to prevent Mongo documents from hitting huge sizes, this does mean that player-service
	/// needs to hit the database very frequently, and updating components requires a separate db hit per component.
	/// 
	/// Most of the components have a very small amount of information, so I'm not sure why they were separated.
	/// If I had to wager a guess, the architect was used to RDBMS-style design where each of these things would be
	/// its own table, but the real strength of Mongo is being able to store full objects in one spot, avoiding the
	/// need to write joins / multiple queries to pull information.
	///
	/// The maximum document size in Mongo is 16 MB, and we're nowhere close to that limit.
	///
	/// This also means that we have many more possible points of failure; whenever we go to update the player record,
	/// we have so many more write operations that can fail, which should trigger a transaction rollback.
	///
	/// Maintaining a transaction with async writes has caused headaches and introduced kluges to get it working, too.
	/// This would be less of a problem with fewer components, or just one gigantic player record.
	///
	/// To retrieve components from a monolithic record, mongo can Project specific fields, and just used the one query.
	/// 
	/// TODO: Compare performance with loadtests: all these collections vs. a monolithic player record
	/// When there's some downtime, it's worth exploring and quantifying just what kind of impact this design has.
	/// Maybe it's more performant than I think.  It's also entirely possible that it requires too much time to change
	/// for TH, but we should re-evaluate it for the next game if that's the case.
	/// </summary>
	[SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")]
	[SuppressMessage("ReSharper", "ExpressionIsAlwaysNull")]
	public TopController(IConfiguration config)  : base(config) =>
		ComponentServices = new Dictionary<string, ComponentService>
		{
			{ Component.AB_TEST, _abTestService },
			{ Component.ACCOUNT, _accountService },
			{ Component.EQUIPMENT, _equipmentService },
			{ Component.HERO, _heroService },
			{ Component.MULTIPLAYER, _multiplayerService },
			{ Component.QUEST, _questService },
			{ Component.STORE, _storeService },
			{ Component.SUMMARY, _summaryService },
			{ Component.TUTORIAL, _tutorialService },
			{ Component.WALLET, _walletService },
			{ Component.WORLD, _worldService }
		};

	[HttpPatch, Route("update"), RequireAccountId, HealthMonitor(weight: 10)]
	public ActionResult Update()
	{
		IClientSessionHandle session = _itemService.StartTransaction();
		Component[] components = Require<Component[]>("components");
		Item[] items = Optional<Item[]>("items") ?? Array.Empty<Item>();
		Item[] itemUpdates = Optional<Item[]>(key: "updatedItems") ?? Array.Empty<Item>();
		Item[] itemCreations = Optional<Item[]>(key: "newItems") ?? Array.Empty<Item>();
		Item[] itemDeletions = Optional<Item[]>(key: "deletedItems") ?? Array.Empty<Item>();
		
		// TODO: Remove this when "items" is removed.
		if ((itemCreations.Any() || itemUpdates.Any() || itemDeletions.Any()) && items.Any())
			throw new PlatformException("If using the new item update capabilities, passing 'items' is not supported.  Remove the key from your request.");

		foreach (Item item in items)
			item.AccountId = Token.AccountId;
		foreach (Item item in itemUpdates)
			item.AccountId = Token.AccountId;
		foreach (Item item in itemCreations)
			item.AccountId = Token.AccountId;

		long totalMS = Timestamp.UnixTimeMS;
		long componentMS = Timestamp.UnixTimeMS;

		string aid = Token.AccountId;
		foreach (Component component in components)
			Task.Run(() => _auditService.Record(aid, component.Name, updateVersion: component.Version));

		List<Task<bool>> tasks = components.Select(data => ComponentServices[data.Name]
			.UpdateAsync(
				accountId: Token.AccountId,
				data: data.Data,
				version: data.Version,
				session: session
			)
		).ToList();

		componentMS = Timestamp.UnixTimeMS - componentMS;

		long itemMS = Timestamp.UnixTimeMS;

#region Deprecated Item Code
		Item[] toSave = items.Where(item => !item.MarkedForDeletion).ToArray();
		Item[] toDelete = items.Where(item => item.MarkedForDeletion).ToArray();

		if (toSave.Any())
			tasks.Add(_itemService.BulkUpdateAsync(toSave, session));
		if (toDelete.Any())
			tasks.Add(_itemService.BulkDeleteAsync(toDelete, session));
#endregion

		if (itemCreations.Any())
			tasks.Add(_itemService.InsertAsync(itemCreations, session));
		if (itemUpdates.Any())
			tasks.Add(_itemService.BulkUpdateAsync2(itemUpdates, session));
		if (itemDeletions.Any())
			tasks.Add(_itemService.BulkDeleteAsync(itemDeletions, session));

		itemMS = Timestamp.UnixTimeMS - itemMS;

		try
		{
			Task.WaitAll(tasks.ToArray());
		}
		catch (AggregateException e)
		{
			throw e.InnerException ?? e;
		}

		if (tasks.Select(task => task.Result).Any(success => !success))
		{
			session.AbortTransaction();
			Log.Warn(Owner.Default, "The update was aborted.  One or more updates was unsuccessful.");
			return Problem(detail: "Transaction aborted.");
		}
		
		session.CommitTransaction();

		totalMS = Timestamp.UnixTimeMS - totalMS;

		return Ok(new
		{
			Token = Token, 
			componentTaskCreationMS = componentMS, 
			itemTaskCreationMS = itemMS, 
			totalMS = totalMS,
			itemMap = itemCreations.Select(item => item.Map)
		});
	}

	[HttpGet, Route("read"), RequireAccountId]
	public ActionResult Read()
	{
		// ~900 ms sequential reads
		// ~250-350ms concurrent reads
		string[] names = Optional<string>("names")?.Split(',');

		List<Task<Component>> tasks = new List<Task<Component>>();

		string aid = Token.AccountId;
		foreach (string name in names)
		{
			if (!ComponentServices.ContainsKey(name))
				continue;
			tasks.Add(ComponentServices[name].LookupAsync(aid));
		}

		Task.WaitAll(tasks.ToArray());

		return Ok(value: new RumbleJson
		{
			{ "accountId", Token.AccountId },
			{ "components", tasks.Select(task => task.Result) }
		});
	}

	/// <summary>
	/// Used for Titans Website logins.  Since the website does not have access to installId, this performs the same operations on SSO data.
	/// If there's an account conflict, this will fail 100% of the time.  Account conflicts *should* be impossible without installId information,
	/// however.
	/// </summary>
	/// <returns></returns>
	/// <exception cref="PlatformException"></exception>
	[HttpPost, Route("login"), NoAuth]
	public ActionResult Login()
	{
		throw new NotImplementedException();
		// RumbleJson sso = Optional<RumbleJson>("sso");
		//
		// List<Profile> profiles = _profileService.Find(sso, out List<SsoData> ssoData);
		// string[] accountIds = profiles.Select(profile => profile.AccountId).Distinct().ToArray();
		//
		// if (!accountIds.Any())
		// 	throw new PlatformException("Account does not yet exist, or SSO is invalid.", code: ErrorCode.MongoRecordNotFound);
		// if (accountIds.Length > 1)
		// 	throw new PlatformException("Profile was found on multiple accounts!");
		//
		// Player player = _playerService.Find(accountIds.First());
		//
		// int discriminator = _discriminatorService.Lookup(player);
		// string email = profiles.FirstOrDefault(profile => profile.Email != null)?.Email;
		//
		// string token = _apiService.GenerateToken(player.AccountId, player.Screenname, email, discriminator, AccountController.TOKEN_AUDIENCE);
		//
		// return Ok(new RumbleJson
		// {
		// 	{ "player", player },
		// 	{ "discriminator", discriminator },
		// 	{ "accessToken", token }
		// });
	}


	[HttpGet, Route("config"), NoAuth, HealthMonitor(weight: 5)]
	public ActionResult GetConfig()
	{
		string clientVersion = Optional<string>("clientVersion") ?? "default";

		RumbleJson clientVar = _dc2Service.GetValuesFor(Audience.GameClient);

		RumbleJson output = new RumbleJson();
		foreach (KeyValuePair<string, object> pair in clientVar.Where(pair => pair.Key.StartsWith("default") || pair.Key.StartsWith(clientVersion)))
			output[pair.Key.Replace("default:", "").Replace($"{clientVersion}:", "")] = pair.Value;

		return Ok(new
		{
			ClientVersion = clientVersion,
			ClientVars = output
		});
	}

	[HttpGet, Route("items"), RequireAccountId]
	public ActionResult GetItems()
	{
		string[] ids = Optional<string>("ids")?.Split(',');
		string[] types = Optional<string>("types")?.Split(',');

		long itemMS = Timestamp.UnixTimeMS;
		List<Item> output = _itemService.GetItemsFor(Token.AccountId, ids, types);
		itemMS = Timestamp.UnixTimeMS - itemMS;
		
		return Ok(new { Items = output, itemMS = itemMS });
	}

	[HttpPatch, Route("screenname"), RequireAccountId]
	public ActionResult ChangeName()
	{
		string sn = Require<string>("screenname");
		
		_playerService.SyncScreenname(sn, Token.AccountId);
		Player player = _playerService.Find(Token.AccountId);

		int discriminator = _discriminatorService.Update(player);
		
		string token = _apiService.GenerateToken(player.AccountId, player.Screenname, Token.Email, discriminator, AccountController.TOKEN_AUDIENCE);

		return Ok(new
		{
			Player = player,
			AccessToken = token,
			Discriminator = discriminator
		});
	}

	[HttpGet, Route("lookup")]
	public ActionResult PlayerLookup()
	{
		// TODO: This is a little janky, and once the SummaryComponent is implemented, this should just return those entries.
		string[] accountIds = Require<string>("accountIds")?.Split(",");
		
		List<DiscriminatorGroup> discriminators = _discriminatorService.Find(accountIds);

		Dictionary<string, string> avatars = new Dictionary<string, string>();
		Dictionary<string, int> accountLevels = new Dictionary<string, int>();
		foreach (Component component in ComponentServices[Component.ACCOUNT].Find(accountIds))
		{
			if (!avatars.ContainsKey(component.AccountId) || avatars[component.AccountId] == null)
				avatars[component.AccountId] = component.Data.Optional<string>("accountAvatar");
			accountLevels[component.AccountId] = component.Data.Optional<int?>("accountLevel") ?? -1;
		}

		List<RumbleJson> output = new List<RumbleJson>();
		
		// TODO: Add borders to lookup data.

		foreach (DiscriminatorGroup group in discriminators)
			foreach (DiscriminatorMember member in group.Members.Where(member => accountIds.Contains(member.AccountId)))
				output.Add(new RumbleJson
				{
					{ Player.FRIENDLY_KEY_ACCOUNT_ID, member.AccountId },
					// { Player.FRIENDLY_KEY_SCREENNAME, member.ScreenName },
					{ Player.FRIENDLY_KEY_DISCRIMINATOR, group.Number.ToString().PadLeft(4, '0') },
					{ "accountAvatar", avatars.ContainsKey(member.AccountId) ? avatars[member.AccountId] : null },
					{ "accountLevel", accountLevels.ContainsKey(member.AccountId) ? accountLevels[member.AccountId] : null }
				});

		return Ok(new
		{
			Results = output
		});
	}

	// TODO: Remove /launch permanently once we've switched over to it.
	[HttpPost, Route("launch"), NoAuth]
	public ActionResult Launch()
	{
		_apiService
			.Request("/player/v2/account/login")
			.SetPayload(new RumbleJson
			{
				{
					Player.FRIENDLY_KEY_DEVICE, new RumbleJson
					{
						{ DeviceInfo.FRIENDLY_KEY_INSTALL_ID, Require<string>("installId") },
						{ DeviceInfo.FRIENDLY_KEY_CLIENT_VERSION, Optional<string>("clientVersion") },
						{ DeviceInfo.FRIENDLY_KEY_DATA_VERSION, Optional<string>("dataVersion") },
						{ DeviceInfo.FRIENDLY_KEY_LANGUAGE, Optional<string>("systemLanguage") },
						{ DeviceInfo.FRIENDLY_KEY_OS_VERSION, Optional<string>("osVersion") },
						{ DeviceInfo.FRIENDLY_KEY_TYPE, Optional<string>("deviceType") }
					}
				},
				{
					"sso", new RumbleJson
					{
						{ SsoData.FRIENDLY_KEY_GOOGLE_TOKEN, Optional<RumbleJson>("sso")?.Optional<string>("googleToken") }
					}
				}
			})
			.Post(out RumbleJson json, out int code);

		RumbleJson output = new RumbleJson
		{
			{ "success", true },
			{ "remoteAddr", GeoIPData?.IPAddress },
			{ "geoipAddr", GeoIPData?.IPAddress },
			{ "country", GeoIPData?.CountryCode },
			{ "serverTime", Timestamp.UnixTime },
			{ "requestId", json?.Optional<string>("requestId") },
			{ "accessToken", json?.Optional<Player>("player")?.Token },
			{ "player", new RumbleJson
			{
				{ "clientVersion", json?.Optional<Player>("player")?.Device?.ClientVersion },
				{ "dateCreated", 0 },
				{ "lastSavedInstallId", json?.Optional<Player>("player")?.Device?.InstallId },
				{ "screenname", json?.Optional<Player>("player")?.Screenname },
				{ "username", json?.Optional<Player>("player")?.Screenname },
				{ "id", json?.Optional<Player>("player")?.Id }
			}},
			{ "discriminator", json?.Optional<Player>("player")?.Discriminator },
			{ "ssoData", Array.Empty<string>() },
			{ "warning", "This endpoint is deprecated!  It will be leaving player-service once the new login flows have been adopted." }
		};
		return Ok(output);
	}
	
	// {
	// 	"success": true,
	// 	"remoteAddr": "73.162.30.116",
	// 	"geoipAddr": "73.162.30.116",
	// 	"country": "US",
	// 	"serverTime": 1667435651,
	// 	"requestId": "c005ee2cb35f165b134b09f128977037",
	// 	"accessToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJhaWQiOiI2MmU4NDE5MjUwMzAzNDNjNjA3OWU3OGQiLCJleHAiOjE2Njc4Njc2NTEsImlzcyI6IlJ1bWJsZSBUb2tlbiBTZXJ2aWNlIiwiaWF0IjoxNjY3NDM1NjUxLCJhdWQiOlsiY2hhdC1zZXJ2aWNlIiwiZG16LXNlcnZpY2UiLCJsZWFkZXJib2FyZC1zZXJ2aWNlIiwibWFpbC1zZXJ2aWNlIiwibWF0Y2htYWtpbmctc2VydmljZSIsIm11bHRpcGxheWVyLXNlcnZpY2UiLCJuZnQtc2VydmljZSIsInBsYXllci1zZXJ2aWNlIiwicHZwLXNlcnZpY2UiLCJyZWNlaXB0LXNlcnZpY2UiXSwic24iOiJQbGF5ZXI4YmMwYjYyIiwiZCI6NzMwNywiaXAiOiI3My4xNjIuMzAuMTE2IiwiY2MiOiJVUyIsInJlcSI6InBsYXllci1zZXJ2aWNlIiwiZ2tleSI6IjU3OTAxYzZkZjgyYTQ1NzA4MDE4YmE3M2I4ZDE2MDA0In0.vRElIAYUJpCuy4peekYo4kohV0r2-LvGz0mMROP4mkdaKMwX_egPesvZnoam64NGgOLnjCbSWaOeaqGGdXFo6g",
	// 	"player": {
	// 		"clientVersion": "0.1.432",
	// 		"dateCreated": 1659388306,
	// 		"lastSavedInstallId": "locust-postman",
	// 		"screenname": "Player8bc0b62",
	// 		"username": "Player8bc0b62",
	// 		"id": "62e841925030343c6079e78d"
	// 	},
	// 	"discriminator": 7307,
	// 	"ssoData": []
	// }

	[HttpDelete, Route("pesticide"), NoAuth]
	public ActionResult KillAllLocusts()
	{
		// TODO: This should require admin, and optimize queries

		Player[] locusts = _playerService.Find(filter: player => player.Device.InstallId.StartsWith("locust-"));

		foreach (Player locust in locusts)
		{
			foreach (ComponentService componentService in ComponentServices.Values)
				componentService.Delete(locust);
			_itemService.Delete(locust);
			_playerService.Delete(locust.Id);
		}

		return Ok(new
		{
			LocustsKilled = locusts.Length
		});
	}
}