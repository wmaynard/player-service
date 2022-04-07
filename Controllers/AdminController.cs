using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayerService.Models;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Services;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2/admin"), RequireAuth(TokenType.ADMIN)]
public class AdminController : PlatformController
{
#pragma warning disable CS0649
	private readonly PlayerAccountService _playerService;
	private readonly DiscriminatorService _discriminatorService;
	private readonly DynamicConfigService _dynamicConfigService;
	private readonly ProfileService _profileService;
	private readonly NameGeneratorService _nameGeneratorService;
	private readonly ItemService _itemService;

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
#pragma warning restore CS0649
	private Dictionary<string, ComponentService> ComponentServices { get; init; }
	
	public AdminController(IConfiguration config) : base(config) =>
		ComponentServices = new Dictionary<string, ComponentService>()
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

	[HttpGet, Route("details")]
	public ActionResult Details()
	{
		string accountId = Require<string>("accountId");

		GenericData output = new GenericData();

		GenericData components = new GenericData();
		foreach (KeyValuePair<string, ComponentService> pair in ComponentServices)
			components[pair.Key] = pair.Value.Lookup(accountId);

		Player player = _playerService.Find(accountId);
		player.Discriminator = _discriminatorService.Lookup(player);

		output["player"] = player;
		output["profiles"] = _profileService.Find(profile => profile.AccountId == accountId);
		output["components"] = components;
		output["items"] = _itemService.GetItemsFor(accountId);
		
		return Ok(value: output);
	}

	[HttpGet, Route("search")]
	public ActionResult Search()
	{
		string term = Require<string>("term").ToLower();
		const string hex = @"\A\b[0-9a-fA-F]+\b\Z";

		// Because _id is a special field in Mongo (an ObjectId), we can't use normal string evaluations in our filter.
		// Anything that's not a hex value will cause the driver to throw a FormatException when translating to a query.
		// Consequently, we need to run a separate search for exact Id matches.
		Player[] PlayerIdMatches = term.Length == 24 && Regex.IsMatch(term, pattern: hex)
			? _playerService.Find(player =>
				player.Id.Equals(term)
				|| player.AccountIdOverride.Equals(term)
			).ToArray()
			: Array.Empty<Player>();
		
		Profile[] ProfileIdMatches = term.Length == 24 && Regex.IsMatch(term, pattern: hex)
			? _profileService.Find(profile => profile.AccountId.Equals(term)).ToArray()
			: Array.Empty<Profile>();

		Profile[] ProfileEmailMatches = _profileService.FindByEmail(term);

		// Now we can search for partial matches.  Frustratingly, on ObjectId fields only, Contains() returns false on
		// exact matches, hence the above query.
		List<Player> players = _playerService.Find(player =>
			player.Id.ToLower().Contains(term)
			|| player.Screenname.ToLower().Contains(term)
			|| player.InstallId.ToLower().Contains(term)
			|| player.AccountIdOverride.ToLower().Contains(term)
		).ToList();
		players.AddRange(PlayerIdMatches);

		GenericData discs = _discriminatorService.Search(players.Select(player => player.AccountId).ToArray());
		foreach (Player player in players)
			player.Discriminator = discs.Optional<int?>(player.AccountId);
		
		List<Profile> profiles = _profileService.Find(profile =>
			profile.AccountId.Contains(term)
		).ToList();
		profiles.AddRange(ProfileIdMatches);
		profiles.AddRange(ProfileEmailMatches);
		
		// Grab any Players from found profiles that we don't already have.  Finding anything here should be
		// extremely rare.
		foreach (string accountId in profiles.Select(profile => profile.AccountId))
			if (!players.Select(player => player.Id).Contains(accountId))
				players.Add(_playerService.FindOne(player => player.Id == accountId));

		Player[] parents = players
			.Where(player => !player.IsLinkedAccount)
			.ToArray();
		foreach (Player parent in parents)
			parent.LinkedAccounts = players
				.Where(player => player.AccountIdOverride == parent.AccountId)
				.OrderByDescending(player => player.CreatedTimestamp)
				.ToArray();
		Player[] orphans = players
			.Where(player => player.IsLinkedAccount && !parents.Any(parent => parent.AccountId == player.AccountIdOverride))
			.ToArray();
		
		if (orphans.Any())
			Log.Warn(Owner.Default, "Linked accounts were found in the player search, but the parent account was not.", data: new
			{
				Orphans = orphans.Select(orphan => orphan.AccountIdOverride)
			});
		
		float? sum = null; // Assigning to a field in the middle of a LINQ query is a little janky, but this prevents sum re-evaluation / requiring another loop.
		GenericData[] results = parents.OrderByDescending(player => player.WeighSearchTerm(term)).Select(player => new GenericData()
		{
			{ "player", player },
			{ "score", player.SearchWeight },
			{ "confidence", 100 * player.SearchWeight / (sum ??= players.Sum(p => p.SearchWeight)) } // Percentage of the score this record has.
		}).ToArray();
		
		return Ok(new { Results = results });
	}
	public override ActionResult HealthCheck()
	{
		return Ok(new
		{
			DynamicConfigUrl = _dynamicConfigService.Url,
			PlatformUrl = _dynamicConfigService.PlatformUrl
		});
	}
}