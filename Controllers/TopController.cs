using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using PlayerService.Utilities;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities.JsonTools;
#pragma warning disable CS0618

namespace PlayerService.Controllers;

[ApiController, Route("player/v2"), RequireAuth, UseMongoTransaction]
[SuppressMessage("ReSharper", "CoVariantArrayConversion")]
public class TopController : PlatformController
{

#pragma warning disable
	private readonly PlayerAccountService _playerService;
	private readonly DynamicConfig _dynamicConfig;
	private readonly ItemService _itemService;
	private readonly NameGeneratorService _nameGeneratorService;
	
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
		string origin = Optional<string>("origin") ?? "Unknown origin";
		
		// TODO: Remove this when "items" is removed.
		if ((itemCreations.Any() || itemUpdates.Any() || itemDeletions.Any()) && items.Any())
			throw new ObsoleteOperationException("If using the new item update capabilities, passing 'items' is not supported.  Remove the key from your request.");

		foreach (Item item in items)
			item.AccountId = Token.AccountId;
		foreach (Item item in itemUpdates)
			item.AccountId = Token.AccountId;
		foreach (Item item in itemCreations)
			item.AccountId = Token.AccountId;

		long totalMS = TimestampMs.Now;
		long componentMS = TimestampMs.Now;

		List<Task<bool>> tasks = components.Select(data => ComponentServices[data.Name]
			.UpdateAsync(
				accountId: Token.AccountId,
				data: data.Data,
				version: data.Version,
				session: session,
				origin: origin
			)
		).ToList();

		componentMS = TimestampMs.Now - componentMS;

		long itemMS = TimestampMs.Now;

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

		itemMS = TimestampMs.Now - itemMS;

		try
		{
			Task.WaitAll(tasks.ToArray());
		}
		catch (AggregateException e)
		{
			throw e?.InnerException ?? e;
		}

		if (tasks.Where(task => task != null).Select(task => task.Result).Any(success => !success))
		{
			session.AbortTransaction();
			Log.Warn(Owner.Default, "The update was aborted.  One or more updates was unsuccessful.");
			return Problem(detail: "Transaction aborted.");
		}
		
		session.CommitTransaction();

		totalMS = TimestampMs.Now - totalMS;

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


		string aid = Token.AccountId;

		if (_playerService.Find(aid) == null)
		{
			Log.Warn(Owner.Will, "Unable to find player account.  This ID could be from another service, such as Portal", data: new
			{
				AccountId = Token.AccountId,
				Origin = Token.Requester
			});
			throw new PlatformException("Unable to find player account.", code: ErrorCode.AccountNotFound);
		}
		
		List<Task<Component>> tasks = new ();
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

	[HttpGet, Route("config"), NoAuth, HealthMonitor(weight: 5)]
	public ActionResult GetConfig()
	{
		const string OVERRIDE = ":";
		Version clientVersion = new (Optional<string>("clientVersion") ?? "0.0.0.0");
		RumbleJson config = _dynamicConfig.GetValuesFor(Audience.GameClient);
		RumbleJson variables = new();
		
		List<ConfigOverride> overrides = new();
		List<string> parsingErrors = new();
		foreach (KeyValuePair<string, object> pair in config)
		{
			int index = pair.Key.IndexOf(OVERRIDE, StringComparison.Ordinal);

			string key = index switch
			{
				-1 => pair.Key,
				>= 0 when pair.Key.StartsWith("default") => null,
				_ => pair.Key[..index]
			};
			if (key == null)
				continue;

			string version = index > -1
				? pair.Key[(index + 1)..]
				: null;

			if (string.IsNullOrWhiteSpace(version))
			{
				variables[key] = pair.Value;
				continue;
			}

			try
			{
				Version configVersion = new (version);

				if (clientVersion.CompareTo(configVersion) >= 0)
					overrides.Add(new ConfigOverride
					{
						Key = key,
						Version = new Version(version),
						Value = pair.Value
					});
			}
			catch (Exception e)
			{
				if (!PlatformEnvironment.IsProd)
					parsingErrors.Add(index > -1
						? $"'{pair.Key}' Couldn't parse version; expected format is 'key:version'"
						: $"'{pair.Key}' Couldn't parse variable; {e?.Message}"
					);
			}
		}
		
		// Previous response before refactor
		// {
		// 	"success": true,
		// 	"clientVersion": "1.3.1427",
		// 	"clientVars": {
		// 		"allow_pvp_preview": "true",
		// 		"baseCdnUrl": "https://towereng-a.akamaized.net/dev/",
		// 		"enable_screen_loading_telemetry": "true",
		// 		"gameServerUrl": "https://dev.nonprod.tower.cdrentertainment.com/game/",
		// 		"game_server_request_timeout_secs": "50",
		// 		"purchaseTimeoutMs": "30000",
		// 		"use_game_server_websockets": "false"
		// 	}
		// }

		IEnumerable<ConfigOverride> ordered = overrides
			.OrderByDescending(o => o.Version)
			.DistinctBy(o => o.Key);

		foreach (ConfigOverride o in ordered)
			variables[o.Key] = o.Value;

		RumbleJson output = new()
		{
			{ "success", true }, // TODO: Remove this after the client is no longer dependent on it; hardcoded on 2022.11.22
			{ "clientVersion", clientVersion.ToString() },
			{ "clientVars", variables.Sort() }
		};

		if (parsingErrors.Any())
			output["parsingErrors"] = parsingErrors;
		
		return Ok(output);
	}

	[HttpGet, Route("items"), RequireAccountId]
	public ActionResult GetItems()
	{
		string[] ids = Optional<string>("ids")?.Split(',');
		string[] types = Optional<string>("types")?.Split(',');

		long itemMS = TimestampMs.Now;
		List<Item> output = _itemService.GetItemsFor(Token.AccountId, ids, types);
		itemMS = TimestampMs.Now - itemMS;

		if (itemMS > 10_000)
			Log.Warn(Owner.Will, "Took a long time to retrieve items from MongoDB", data: new
			{
				itemCount = output.Count,
				TypeBreakdown = output
					.GroupBy(item => item.Type)
					.Select(group => new
					{
						Type = group.Key,
						Count = group.Count()
					}),
				DurationMs = itemMS
			});
		else if (output.Count > 100)
			Log.Warn(Owner.Will, "A lot of items were returned at once.  Consider refining the query to return fewer items.", data: new
			{
				itemCount = output.Count,
				TypeBreakdown = output
					.GroupBy(item => item.Type)
					.Select(group => new
					{
						Type = group.Key,
						Count = group.Count()
					}),
				DurationMs = itemMS
			});

		return Ok(new { Items = output, itemMS = itemMS });
	}

	[HttpPatch, Route("screenname"), RequireAccountId]
	public ActionResult ChangeName()
	{
		string sn = Require<string>("screenname");
		Player player = _playerService.ChangeScreenname(Token.AccountId, sn);

		return Ok(new
		{
			Player = player,
			AccessToken = player.Token,
			Discriminator = player.Discriminator
		});
	}

	[HttpGet, Route("lookup")]
	public ActionResult PlayerLookup()
	{
		// TODO: This is a little janky, and once the SummaryComponent is implemented, this should just return those entries.
		string[] accountIds = Require<string>("accountIds")
			?.Split(",")
			.Where(id => id.CanBeMongoId())
			.ToArray();

		Dictionary<string, LookupData> accountComponentData = new();

		// Dictionary<string, string> avatars = new();
		// Dictionary<string, int> accountLevels = new();
		foreach (Component component in ComponentServices[Component.ACCOUNT].Find(accountIds))
		{
			// if (!avatars.ContainsKey(component.AccountId) || avatars[component.AccountId] == null)
			// 	avatars[component.AccountId] = component.Data.Optional<string>("accountAvatar");
			// accountLevels[component.AccountId] = component.Data.Optional<int?>("accountLevel") ?? -1;
			accountComponentData[component.AccountId] = new LookupData
			{
				AccountLevel = component.Data.Optional<int?>("accountLevel") ?? -1,
				Avatar = component.Data.Optional<string>("accountAvatar"),
				ChatTitle = component.Data.Optional<string>("chatTitle"),
				TotalHeroScore = component.Data.Optional<long>("totalHeroScore")
			};
		}

		// RumbleJson[] output = _playerService.CreateLookupResults(accountIds, avatars, accountLevels);
		RumbleJson[] output = _playerService.CreateLookupResults(accountIds, accountComponentData);

		return Ok(new
		{
			Results = output
		});
	}
}

public struct LookupData
{
	public string Avatar;
	public int AccountLevel;
	public string ChatTitle;
	public long TotalHeroScore;
}