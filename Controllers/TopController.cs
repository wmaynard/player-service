using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Models;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using RCL.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2"), RequireAuth, UseMongoTransaction]
public class TopController : PlatformController
{
#pragma warning disable
	private readonly PlayerAccountService _playerService;
	private readonly DiscriminatorService _discriminatorService;
	private readonly DynamicConfigService _dynamicConfigService;
	private readonly ItemService _itemService;
	private readonly ProfileService _profileService;
	private readonly NameGeneratorService _nameGeneratorService;
	private readonly TokenGeneratorService _tokenGeneratorService;
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
		RumbleJson sso = Optional<RumbleJson>("sso");

		List<Profile> profiles = _profileService.Find(sso, out List<SsoData> ssoData);
		string[] accountIds = profiles.Select(profile => profile.AccountId).Distinct().ToArray();

		if (!accountIds.Any())
			throw new PlatformException("Account does not yet exist, or SSO is invalid.", code: ErrorCode.MongoRecordNotFound);
		if (accountIds.Length > 1)
			throw new PlatformException("Profile was found on multiple accounts!");

		Player player = _playerService.Find(accountIds.First());

		int discriminator = _discriminatorService.Lookup(player);
		string email = profiles.FirstOrDefault(profile => profile.Email != null)?.Email;

		string token = _tokenGeneratorService.Generate(player.AccountId, player.Screenname, discriminator, GeoIPData, email);

		return Ok(new RumbleJson
		{
			{ "player", player },
			{ "discriminator", discriminator },
			{ "accessToken", token }
		});
	}

	[HttpPost, Route("launch"), NoAuth, HealthMonitor(weight: 1)]
	public ActionResult Launch()
	{
		string installId = Require<string>("installId");
		string clientVersion = Optional<string>("clientVersion");
		string deviceType = Optional<string>("deviceType");

		RumbleJson sso = Optional<RumbleJson>("sso");

		// TODO: Remove by 7/28 if this is not consistently used for GPG diagnosis
		if (!PlatformEnvironment.IsProd && !string.IsNullOrWhiteSpace(sso?.Optional<RumbleJson>("googlePlay")?.Optional<string>("idToken")))
			Log.Info(Owner.Will, "SSO data found", data: new
			{
				ssoData = sso
			});

		Player player = _playerService.FindOne(player => player.InstallId == installId);

		player ??= CreateNewAccount(installId, deviceType, clientVersion); // TODO: are these vars used anywhere else?

		List<Profile> profiles = _profileService.Find(player.AccountId, sso, out List<SsoData> ssoData);
		Profile[] conflictProfiles = profiles
			.Where(profile => profile.AccountId != player.AccountId)
			.ToArray();
		
		// SSO data was provided, but there's no profile match.  We should create a profile for this SSO on this account.
		foreach (SsoData data in ssoData)
		{
			Profile[] sameTypes = profiles.Where(profile => profile.Type == data.Source).ToArray();
			if (sameTypes.Any())
			{
				// PLATF-6061: Update email addresses when elder accounts don't have one (or has changed).
				Profile profile = sameTypes.FirstOrDefault(p => p.ProfileId == data.AccountId);
				if (profile != null && profile.Email != data.Email)
				{
					profile.Email = data.Email;
					_profileService.Update(profile);
				}
				continue;
			}

			_profileService.Create(new Profile(player.Id, data));
		}

		int discriminator = _discriminatorService.Lookup(player);
		ValidatePlayerScreenname(ref player);	// TD-12118: Prevent InvalidUserException

		string token = _tokenGeneratorService.Generate(player.AccountId, player.Screenname, discriminator, geoData: GeoIPData, email: ssoData.FirstOrDefault(sso => sso.Email != null)?.Email);

		if (conflictProfiles.Any())
		{
			Player other = _playerService.Find(conflictProfiles.First().AccountId);

			// Will on 2022.07.06:
			// This conditional block is a fix for recent GPG issues.  Somehow, profiles were being assigned to
			// child accounts.  When a child account was assigned a profile, the accountConflict status permanently
			// blocks login; this is because the profile was never correctly re-assigned to the parent account.
			// My best guess for how this can occur is that the google token itself is inconsistently valid, and the profile
			// was assigned to the wrong account and then transferred later.
			if (!string.IsNullOrWhiteSpace(other.AccountIdOverride) && other.AccountIdOverride != other.Id)
			{
				Log.Warn(Owner.Will, "An invalid profile was found.  Attempting to resolve automatically.");

				try
				{
					other = _playerService.Find(other.AccountIdOverride);
					conflictProfiles.First().AccountId = other.Id;
					_profileService.Update(conflictProfiles.First());
				}
				catch (Exception e)
				{
					Log.Error(Owner.Will, "Unable to resolve invalid profile automatically.", 
						exception: e,
						data: new
						{
							otherAccount = other,
							conflicts = conflictProfiles
						}
					);
				}
			}
			
			other.GenerateRecoveryToken();
			_playerService.Update(other);

			object response = new
			{
				ErrorCode = "accountConflict",
				AccessToken = token,
				AccountId = player.AccountId,
				ConflictingAccountId = conflictProfiles.First().AccountId,
				ConflictingProfiles = conflictProfiles,
				TransferToken = other.TransferToken,
				SsoData = ssoData
			};
			
			Log.Info(Owner.Default, "Account Conflict", data: response);
			return Ok(response);
		}
		else if (player.InstallId != installId) // TODO: Not sure what this actually accomplishes.
			return Ok(new { ErrorCode = "installConflict", ConflictingAccountId = player.AccountId });
		else // No conflict
		{
			// TODO: #370 saveInstallIdProfile
			foreach (Profile p in profiles)
				_profileService.Update(p); // Are we even modifying anything?
			// TODO: #378 updateAccountData
		}

		player.PrepareIdForOutput();

		return Ok(new
		{
			RemoteAddr = GeoIPData.IPAddress ?? IpAddress, // fallbacks for local dev, since ::1 fails the lookups.
			GeoipAddr = GeoIPData.IPAddress ?? IpAddress,
			Country = GeoIPData.CountryCode,
			ServerTime = Timestamp.UnixTime,
			RequestId = HttpContext.Request.Headers["X-Request-ID"].ToString() ?? Guid.NewGuid().ToString(),
			AccessToken = token,
			Player = player,
			Discriminator = discriminator,
			SsoData = ssoData
		});
	}

	private Player CreateNewAccount(string installId, string deviceType, string clientVersion)
	{
		string dataVersion = Optional<string>("dataVersion");

		string username = null;
		try
		{
			username = _nameGeneratorService.Next;
		}
		catch (Exception e)
		{
			Log.Error(Owner.Will, "Could not generate a new player name.", exception: e);
		}
		Log.Info(Owner.Default, "Creating new account", data: new
		{
			username = username
		});
		Player player = new Player(username)
		{
			ClientVersion = clientVersion,
			DeviceType = deviceType,
			InstallId = installId,
			DataVersion = dataVersion,
			PreviousDataVersion = null,
			UpdatedTimestamp = 0
		};
		_playerService.Create(player);
		Profile profile = new Profile(player);
		_profileService.Create(profile);
		if (!PlatformEnvironment.SwarmMode)
			Log.Info(Owner.Default, "New account created.", data: new
			{
				InstallId = player.AccountId,
				ProfileId = profile.Id,
				AccountId = profile.AccountId
			});
		return player;
	}

	// TODO: Explore MongoTransaction attribute
	// TODO: "link" instead of "transfer"?
	[HttpPatch, Route("transfer"), RequireAccountId]
	public ActionResult Transfer()
	{
		string transferToken = Require<string>("transferToken");
		string[] profileIds = Optional<string[]>("profileIds") ?? new string[]{};
		// TODO: Optional<bool>("cancel")

		Player player = _playerService.Find(Token.AccountId);
		Player other = _playerService.FindOne(p => p.TransferToken == transferToken);

		if (other == null)
			throw new AccountLinkException(
				message: "No player found for transfer token.",
				requester: Token.AccountId,
				transferToken: transferToken
			);

		if (profileIds.Any()) // Move the specified profile IDs to the requesting player.  This reassigns SSO profiles to other accounts.
		{
			if (player.AccountIdOverride != null)
				Log.Warn(Owner.Default, "AccountIDOverride is not null for a transfer; this should be impossible.", data: new
				{
					Player = Token,
					OtherPlayer = other,
					TransferToken = transferToken
				});
			player.AccountIdOverride = null;
			
			Profile[] sso = _profileService.Find(p => profileIds.Contains(p.ProfileId) && p.Type != Profile.TYPE_INSTALL);
			foreach (Profile profile in sso)
			{
				profile.PreviousAccountIds.Add(other.AccountId);
				profile.AccountId = player.Id;
				_profileService.Update(profile);
			}
			other.AccountMergedTo = player.Id;
			
			_playerService.Update(player);
			_playerService.Update(other);
			Log.Info(Owner.Default, "Profiles transferred.", data: new
			{
				Player = Token,
				PreviousAccount = other,
				Profiles = sso
			});
			
			return Ok(new { TransferredProfiles = sso });
		}
		else
		{
			if (player.AccountId != other.AccountId)
				player.AccountIdOverride = other.AccountId;
			player.Screenname = other.Screenname;
			other.TransferToken = null;
			
			_playerService.Update(player);
			_playerService.Update(other);
			_playerService.SyncScreenname(other.Screenname, other.AccountId); // TODO: Can combine these updates into one query

			_profileService.Create(new Profile(player));

			Token.ScreenName = other.Screenname;
			Log.Info(Owner.Default, "AccountID linked via AccountIdOverride.", data: new
			{
				Player = Token,
				Account = player
			});
			return Ok(new { Account = player });
		}
	}

	[HttpGet, Route("config"), NoAuth, HealthMonitor(weight: 5)]
	public ActionResult GetConfig()
	{
		string clientVersion = Optional<string>("clientVersion");
		RumbleJson config = _dynamicConfigService.GameConfig;
		
		RumbleJson clientVars = ExtractClientVars(
			clientVersion, 
			prefixes: config.Require<string>("clientVarPrefixesCSharp").Split(','), 
			configs: config
		);

		return Ok(new
		{
			ClientVersion = clientVersion,
			ClientVars = clientVars
		});
	}
	private RumbleJson ExtractClientVars(string clientVersion, string[] prefixes, params RumbleJson[] configs)
	{
		List<string> clientVersions = new List<string>();
		if (clientVersion != null)
		{
			clientVersions.Add(clientVersion);
			while (clientVersion.IndexOf('.') > 0)
				clientVersions.Add(clientVersion = clientVersion[..clientVersion.LastIndexOf('.')]);
		}

		RumbleJson output = new RumbleJson();
		foreach (string prefix in prefixes)
		{
			string defaultVar = prefix + "default:";
			string[] versionVars = clientVersions
				.Select(it => prefix + it + ":")
				.ToArray();
			foreach (RumbleJson config in configs)
				foreach (string key in config.Keys)
				{
					string defaultKey = key.Replace(defaultVar, "");
					if (key.StartsWith(defaultVar) && !output.ContainsKey(defaultKey))
						output[defaultKey] = config.Require<string>(key);
					foreach(string it in versionVars)
						if (key.StartsWith(it))
							output[key.Replace(it, "")] = config.Require<string>(key);
				}
		}
		return output;
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

		string token = _tokenGeneratorService.Generate(
			accountId: player.AccountId, 
			screenname: player.Screenname, 
			discriminator: discriminator,
			geoData: GeoIPData, 
			email: Token.Email
		);

		return Ok(new
		{
			Player = player,
			AccessToken = token,
			Discriminator = discriminator
		});
	}

	[HttpPost, Route("iostest"), NoAuth]
	public void AppleTest()
	{
		// string token = Require<RumbleJson>("sso").Require<RumbleJson>("appleId").Require<string>("token");
		// AppleToken at = new AppleToken(token);
		// at.Decode();
		//
		//
		// RumbleJson payload = new RumbleJson();
		// RumbleJson response = PlatformRequest.Post("https://appleid.apple.com/auth/token", payload: payload).Send();
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
					{ Player.FRIENDLY_KEY_SCREENNAME, member.ScreenName },
					{ Profile.FRIENDLY_KEY_DISCRIMINATOR, group.Number.ToString().PadLeft(4, '0') },
					{ "accountAvatar", avatars.ContainsKey(member.AccountId) ? avatars[member.AccountId] : null },
					{ "accountLevel", accountLevels.ContainsKey(member.AccountId) ? accountLevels[member.AccountId] : null }
				});

		return Ok(new
		{
			Results = output
		});
	}

	[HttpDelete, Route("pesticide"), NoAuth]
	public ActionResult KillAllLocusts()
	{
		// TODO: This should require admin, and optimize queries

		Player[] locusts = _playerService.Find(filter: player => player.InstallId.StartsWith("locust-"));

		foreach (Player locust in locusts)
		{
			foreach (ComponentService componentService in ComponentServices.Values)
				componentService.Delete(locust);
			foreach (Profile profile in _profileService.Find(profile => profile.AccountId == locust.AccountId))
				_profileService.Delete(profile.Id);
			_itemService.Delete(locust);
			_playerService.Delete(locust.Id);
		}

		return Ok(new
		{
			LocustsKilled = locusts.Length
		});
	}

	[HttpDelete, Route("gpg"), NoAuth]
	public ActionResult KillGPGProfile()
	{
		if (PlatformEnvironment.IsProd)
		{
			Log.Error(Owner.Will, "The DELETE /gpg endpoint should not be called outside of Dev!");
			throw new PlatformException("Not allowed on prod!");
		}
		
		string email = Require<string>("email");
		Profile[] profiles = _profileService.Find(filter: profile => profile.Email == email);
		foreach (Profile p in profiles)
			_profileService.Delete(p);
		return Ok(new
		{
			DeletedProfiles = profiles.Length
		});
	}
	
	// Will on 2022.07.15 | In rare situations an account can come through that does not have a screenname.
	// The cause of these edge cases is currently unknown.  However, we can still add an insurance policy here.
	/// <summary>
	/// If a Player object does not have a screenname, this method looks up the screenname from their account component.
	/// If one is not found, a new screenname is generated.
	/// </summary>
	/// <param name="player">The player object to validate.</param>
	/// <returns>The found or generated screenname.</returns>
	private string ValidatePlayerScreenname(ref Player player)
	{
		if (!string.IsNullOrWhiteSpace(player.Screenname))
			return player.Screenname;
		
		Log.Warn(Owner.Default, "Player screenname is invalid.  Looking up account component's data to set it.");
		player.Screenname = _accountService.Lookup(player.AccountId)?.Data?.Optional<string>("accountName");
		
		if (string.IsNullOrWhiteSpace(player.Screenname))
		{
			player.Screenname = _nameGeneratorService.Next;
			Log.Warn(Owner.Default, "Player component screenname was also null; player has been assigned a new name.");
		}
		
		int count = _playerService.SyncScreenname(player.Screenname, player.AccountId);
		Log.Info(Owner.Default, "Screenname has been updated.", data: new
		{
			LinkedAccountsAffected = count
		});
		return player.Screenname;
	}
}