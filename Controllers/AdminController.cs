using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Interop;
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
			throw new RecordNotFoundException(update.Name, "Component not found.", data: new RumbleJson
			{
				{ "accountId", update.AccountId }
			});
		if (string.IsNullOrEmpty(update.AccountId))
			throw new ComponentInvalidException(update.AccountId, reason: "Missing accountId; can not be used for an update.");
		
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
			throw new RecordNotFoundException(_playerService.CollectionName, "Player not found.", data: new RumbleJson
			{
				{ "accountId", accountId }
			});
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
			throw new InvalidFieldException("screenname", "Field is null or empty.");
		
		int affected = _playerService.SyncScreenname(name, accountId, true);
		
		// TODO: Invalidate tokens

		return Ok(new { AffectedAccounts = affected });
	}

	[HttpGet, Route("search")]
	public ActionResult Search()
	{
		string[] terms = Require<string>("terms").ToLower().Split(',');

		if (terms.Any(term => term.Length < 3))
			throw new InvalidFieldException("terms", "Search terms must contain at least 3 characters each.");

		Player[] results = _playerService.Search(terms);
		RumbleJson discs = _discriminatorService.Search(results.Select(player => player.AccountId).ToArray());
		foreach (Player player in results)
			player.Discriminator = discs.Optional<int?>(player.AccountId);

		return Ok(new RumbleJson
		{
			{ "count", results.Length },
			{ "players", results }
		});
	}

	[HttpPost, Route("clone")]
	public ActionResult Clone()
	{
		PlatformEnvironment.EnforceNonprod();

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
		PlatformEnvironment.EnforceNonprod();

		string email = Optional<string>("email");

		if (email == null)
		{
			long affected = _playerService.DeleteAllRumbleAccounts();
			
			if (affected > 0)
				SlackDiagnostics
					.Log($"({PlatformEnvironment.Deployment}) All Rumble accounts have been deleted.", $"{Token.ScreenName} is to blame.  {affected} accounts were affected.")
					.AddChannel("C043FPR7U68")
					.Send()
					.Wait();

			return Ok(new RumbleJson
			{
				{ "affected", affected }
			});
		}

		// When using postman, '+' comes through as a space because it's not URL-encoded.
		// This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
		if (_playerService.DeleteRumbleAccount(email) == 0 && _playerService.DeleteRumbleAccount(email.Trim().Replace(" ", "+")) == 0)
			throw new RecordNotFoundException(_playerService.CollectionName, "Rumble account not found.");
		
		return Ok();
	}

	[HttpDelete, Route("appleAccount")]
	public ActionResult DeleteAppleAccount()
	{
		PlatformEnvironment.EnforceNonprod();

		string email = Optional<string>("email");
		
		if (email == null)
		{
			long affected = _playerService.DeleteAllAppleAccounts();
			
			if (affected > 0)
				SlackDiagnostics
					.Log($"({PlatformEnvironment.Deployment}) All Apple accounts have been deleted.", $"{Token.ScreenName} is to blame.  {affected} accounts were affected.")
					.AddChannel("C043FPR7U68")
					.Send()
					.Wait();

			return Ok(new RumbleJson
			          {
				          { "affected", affected }
			          });
		}

		// When using postman, '+' comes through as a space because it's not URL-encoded.
		// This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
		if (_playerService.DeleteAppleAccount(email) == 0 && _playerService.DeleteAppleAccount(email.Trim().Replace(" ", "+")) == 0)
			throw new RecordNotFoundException(_playerService.CollectionName, "Rumble account not found.", data: new RumbleJson
            {
                { "email", email }
            });
		
		return Ok();
	}

	[HttpDelete, Route("googleAccount")]
	public ActionResult DeleteGoogleAccount()
	{
		PlatformEnvironment.EnforceNonprod();

		string email = Optional<string>("email");
		
		if (email == null)
		{
			long affected = _playerService.DeleteAllGoogleAccounts();
			
			if (affected > 0)
				SlackDiagnostics
					.Log($"({PlatformEnvironment.Deployment}) All Google accounts have been deleted.", $"{Token.ScreenName} is to blame.  {affected} accounts were affected.")
					.AddChannel("C043FPR7U68")
					.Send()
					.Wait();

			return Ok(new RumbleJson
			{
				{ "affected", affected }
			});
		}

		// When using postman, '+' comes through as a space because it's not URL-encoded.
		// This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
		if (_playerService.DeleteGoogleAccount(email) == 0 && _playerService.DeleteGoogleAccount(email.Trim().Replace(" ", "+")) == 0)
			throw new RecordNotFoundException(_playerService.CollectionName, "Rumble account not found.", data: new RumbleJson
			{
				{ "email", email }
			});
		
		return Ok();
	}
	
	[HttpDelete, Route("plariumAccount")]
	public ActionResult DeletePlariumAccount()
	{
		PlatformEnvironment.EnforceNonprod();

		string email = Optional<string>("email");
		
		if (email == null)
		{
			long affected = _playerService.DeleteAllPlariumAccounts();
			
			if (affected > 0)
				SlackDiagnostics
					.Log($"({PlatformEnvironment.Deployment}) All Plarium accounts have been deleted.", $"{Token.ScreenName} is to blame.  {affected} accounts were affected.")
					.AddChannel("C043FPR7U68")
					.Send()
					.Wait();

			return Ok(new RumbleJson
			{
				{ "affected", affected }
			});
		}

		// When using postman, '+' comes through as a space because it's not URL-encoded.
		// This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
		if (_playerService.DeletePlariumAccount(email) == 0 && _playerService.DeletePlariumAccount(email.Trim().Replace(" ", "+")) == 0)
			throw new RecordNotFoundException(_playerService.CollectionName, "Rumble account not found.", data: new RumbleJson
            {
                { "email", email }
            });

		return Ok();
	}

	// TD-14514 | Account linking (previously known as "merge tool")
	[HttpPatch, Route("accountLink")]
	public ActionResult LinkAccounts()
	{
		string child = Require<string>("accountId");
		string parent = Require<string>("parentAccountId");
		bool force = Require<bool>("force");

		Player result = _playerService.LinkPlayerAccounts(child, parent, force, Token);

		return Ok(result);
	}
}