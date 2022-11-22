using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PlayerService.Models;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2/admin"), RequireAuth(AuthType.ADMIN_TOKEN), IgnorePerformance]
public class AdminController : PlatformController
{
#pragma warning disable
	private readonly PlayerAccountService _playerService;
	private readonly DiscriminatorService _discriminatorService;
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
#pragma warning restore
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
	
	[HttpPatch, Route("component")]
	public ActionResult UpdateComponent()
	{
		// TODO: Mock player?
		Component update = Require<Component>("component");
		
		if (ComponentServices[update.Name]?.Lookup(update.AccountId) == null)
			throw new PlatformException("Component not found.", code: ErrorCode.InvalidRequestData);
		if (string.IsNullOrEmpty(update.AccountId))
			throw new PlatformException("Component does not contain an accountId and can not be used for an update.", code: ErrorCode.InvalidRequestData);
		
		IClientSessionHandle session = ComponentServices[update.Name].StartTransaction();
		try
		{
			ComponentServices[update.Name].UpdateAsync(update.AccountId, update.Data, session, update.Version).Wait();
			session.CommitTransaction();
		}
		catch (Exception e)
		{
			session.AbortTransaction();
			if (e.InnerException is PlatformException platEx)
				throw platEx;
			throw;
		}
		
		return Ok();
	}

	[HttpGet, Route("details")]
	public ActionResult Details()
	{
		string accountId = Require<string>("accountId");

		RumbleJson output = new RumbleJson();

		RumbleJson components = new RumbleJson();
		foreach (KeyValuePair<string, ComponentService> pair in ComponentServices)
			components[pair.Key] = pair.Value.Lookup(accountId);

		Player player = _playerService.Find(accountId);
		if (player == null)
			throw new PlatformException("Player not found.", code: ErrorCode.InvalidRequestData);
		player.Discriminator = _discriminatorService.Lookup(player);

		output["player"] = player;
		output["components"] = components;
		output["items"] = _itemService.GetItemsFor(accountId);
		
		return Ok(value: output);
	}

	[HttpPatch, Route("screenname")]
	public ActionResult ChangeScreenname()
	{
		string accountId = Require<string>("accountId");
		string name = Require<string>("screenname");

		if (string.IsNullOrWhiteSpace(name))
			throw new PlatformException("Invalid screenname.", code: ErrorCode.InvalidRequestData);
		
		int affected = _playerService.SyncScreenname(name, accountId);
		
		// TODO: Invalidate tokens

		return Ok(new { AffectedAccounts = affected });
	}

	[HttpGet, Route("search")]
	public ActionResult Search()
	{
		string[] terms = Require<string>("terms").ToLower().Split(',');

		if (terms.Any(term => term.Length < 3))
			throw new PlatformException("Search terms must contain at least 3 characters.");

		Player[] results = _playerService.Search(terms);
		RumbleJson discs = _discriminatorService.Search(results.Select(player => player.AccountId).ToArray());
		foreach (Player player in results)
			player.Discriminator = discs.Optional<int?>(player.AccountId);

		return Ok(new RumbleJson
		{
			{ "players", results }
		});
	}

	[HttpPost, Route("clone")]
	public ActionResult Clone()
	{
		if (!(PlatformEnvironment.IsLocal || PlatformEnvironment.IsDev))
			throw new PlatformException("Not allowed outside of local / dev.");

		string source = Require<string>("sourceAccountId");
		string target = Require<string>("targetAccountId");

		try
		{
			foreach (ComponentService service in ComponentServices.Select(pair => pair.Value))
			{
				Component sourceComponent = service.Lookup(source);
				Component targetComponent = service.Lookup(target);

				targetComponent.Data = sourceComponent.Data;
				service.Update(targetComponent);
			}
		}
		catch (Exception e)
		{
			throw new PlatformException("Unable to clone account", inner: e);
		}
		return Ok();
	}
	
	[HttpDelete, Route("profiles/unlink")]
	public ActionResult KillGPGProfile()
	{
		string email = Require<string>("email");
		
		return Ok();
	}

	[HttpPatch, Route("currency")]
	public ActionResult UpdateCurrency()
	{
		string accountId = Require<string>("accountId");
		string name = Require<string>("name");
		long amount = Require<long>("amount");
		int version = Require<int>("version");

		bool success = ((WalletService)ComponentServices[Component.WALLET]).SetCurrency(accountId, name, amount, version);

		return Ok(new RumbleJson
		{
			{ "success", success }
		});
	}

	[HttpDelete, Route("rumbleAccount")]
	public ActionResult DeleteRumbleAccount()
	{
		if (PlatformEnvironment.IsProd)
			throw new PlatformException("Not allowed on prod.");

		string email = Require<string>("email");

		// When using postman, '+' comes through as a space because it's not URL-encoded.
		// This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
		if (_playerService.DeleteRumbleAccount(email) == 0 && _playerService.DeleteRumbleAccount(email.Trim().Replace(" ", "+")) == 0)
			throw new PlatformException("Rumble account not found.");
		
		return Ok();
	}
}