using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayerService.Models;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.CSharp.Common.Services;

namespace PlayerService.Controllers
{
	[ApiController, Route("player/v2/admin"), RequireAuth(TokenType.ADMIN)]
	public class AdminController : PlatformController
	{
		private readonly Services.PlayerAccountService _playerService;
		private readonly DiscriminatorService _discriminatorService;
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly ProfileService _profileService;
		private readonly NameGeneratorService _nameGeneratorService;
		private readonly ItemService _itemService;
		private Dictionary<string, ComponentService> ComponentServices { get; init; }
		
		public AdminController(IConfiguration config,
			DynamicConfigService configService,
			DiscriminatorService discriminatorService,
			ItemService itemService,
			NameGeneratorService nameGeneratorService,
			PlayerAccountService playerService,
			ProfileService profileService,
			AbTestService abTestService,				// Component Services
			AccountService accountService,
			EquipmentService equipmentService,
			HeroService heroService,
			MultiplayerService multiplayerService,
			QuestService questService,
			StoreService storeService,
			SummaryService summaryService,
			TutorialService tutorialService,
			WalletService walletService,
			WorldService worldService
		) : base(config)
		{
			_playerService = playerService;
			_discriminatorService = discriminatorService;
			_dynamicConfigService = configService;
			_itemService = itemService;
			_nameGeneratorService = nameGeneratorService;
			_profileService = profileService;

			ComponentServices = new Dictionary<string, ComponentService>();
			ComponentServices[Component.AB_TEST] = abTestService;
			ComponentServices[Component.ACCOUNT] = accountService;
			ComponentServices[Component.EQUIPMENT] = equipmentService;
			ComponentServices[Component.HERO] = heroService;
			ComponentServices[Component.MULTIPLAYER] = multiplayerService;
			ComponentServices[Component.QUEST] = questService;
			ComponentServices[Component.STORE] = storeService;
			ComponentServices[Component.SUMMARY] = summaryService;
			ComponentServices[Component.TUTORIAL] = tutorialService;
			ComponentServices[Component.WALLET] = walletService;
			ComponentServices[Component.WORLD] = worldService;
		}

		[HttpGet, Route("details")]
		public ActionResult Details()
		{
			string accountId = Require<string>("accountId");

			GenericData output = new GenericData();

			GenericData components = new GenericData();
			foreach (KeyValuePair<string, ComponentService> pair in ComponentServices)
				components[pair.Key] = pair.Value.Lookup(accountId);
			
			output["player"] = _playerService.Find(accountId);
			output["profiles"] = _profileService.Find(profile => profile.AccountId == accountId);
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

			// Now we can search for partial matches.  Frustratingly, on ObjectId fields only, Contains() returns false on
			// exact matches, hence the above query.
			List<Player> players = _playerService.Find(player =>
				player.Id.ToLower().Contains(term)
				|| player.Screenname.ToLower().Contains(term)
				|| player.InstallId.ToLower().Contains(term)
				|| player.AccountIdOverride.ToLower().Contains(term)
			).ToList();
			players.AddRange(PlayerIdMatches);
			
			List<Profile> profiles = _profileService.Find(profile =>
				profile.AccountId.Contains(term)
			).ToList();
			profiles.AddRange(ProfileIdMatches);
			
			// Grab any Players from found profiles that we don't already have.  Finding anything here should be
			// extremely rare.
			foreach (string accountId in profiles.Select(profile => profile.AccountId))
				if (!players.Select(player => player.Id).Contains(accountId))
					players.Add(_playerService.FindOne(player => player.AccountId == accountId));

			float? sum = null; // Assigning to a field in the middle of a LINQ query is a little janky, but this prevents sum re-evaluation / requiring another loop.
			GenericData[] results = players.OrderByDescending(player => player.WeighSearchTerm(term)).Select(player => new GenericData()
			{
				{ "player", player },
				{ "score", player.SearchWeight },
				{ "relevance", 100 * player.SearchWeight / (sum ??= players.Sum(p => p.SearchWeight)) } // Percentage of the score this record has.
			}).ToArray();
			
			return Ok(new { Results = results });
		}


		public override ActionResult HealthCheck()
		{
			throw new System.NotImplementedException();
		}
	}
}