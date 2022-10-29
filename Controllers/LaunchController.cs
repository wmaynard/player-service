using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using PlayerService.Models;
using PlayerService.Models.Sso;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2/temp"), UseMongoTransaction]
public class LaunchController : PlatformController
{
    public const Audience TOKEN_AUDIENCE = 
        Audience.ChatService
        | Audience.DmzService
        | Audience.LeaderboardService
        | Audience.MailService
        | Audience.MatchmakingService
        | Audience.MultiplayerService
        | Audience.NftService
        | Audience.PlayerService
        | Audience.PvpService
        | Audience.ReceiptService;
    
#pragma warning disable
    private readonly PlayerAccountService _playerService;
    private readonly DC2Service _dc2Service;
    private readonly DiscriminatorService _discriminatorService;
    private readonly ItemService _itemService;
    private readonly ProfileService _profileService;
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
    
    [SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")]
    [SuppressMessage("ReSharper", "ExpressionIsAlwaysNull")]
    public LaunchController() => ComponentServices = new Dictionary<string, ComponentService>
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

    [HttpPatch, Route("google")]
    public ActionResult LinkGoogle()
    {
        DeviceInfo device = Require<DeviceInfo>("device");
        GoogleAccount google = GoogleAccount.ValidateToken(Require<string>("googleToken"));
        
        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player fromGoogle = _playerService.FromGoogle(google);

        if (fromDevice.Id != fromGoogle?.Id)
            throw new PlatformException("Account conflict.");
        
        fromDevice.GoogleAccount = google;
        _playerService.Update(fromDevice);
        return Ok(fromDevice);
    }
    
    [HttpPatch, Route("rumble")]
    public ActionResult LinkRumble()
    {
        DeviceInfo device = Require<DeviceInfo>("device");
        RumbleAccount rumble = Require<RumbleAccount>("rumble");

        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player fromRumble = _playerService.FromRumble(rumble);
        
        if (fromRumble != null && fromDevice.Id != fromRumble.Id)
            throw new PlatformException("Account conflict.");

        if (fromRumble != null)
            throw new PlatformException("Account already linked.");
        
        _playerService.AttachRumble(fromDevice, rumble);
        return Ok(fromDevice);
    }

    [HttpGet, Route("confirm")]
    public ActionResult ConfirmAccount()
    {
        string id = Require<string>("id");
        string code = Require<string>("code");

        Player player = _playerService.UseConfirmationCode(id, code)
            ?? throw new PlatformException("Incorrect or expired code.");

        return Ok(player);
    }

    [HttpPatch, Route("recover")]
    public ActionResult RecoverAccount()
    {
        string email = Require<string>("email");
        string hash = Require<string>("hash");

        Player recovery = _playerService.BeginRecovery(email, hash)
            ?? throw new PlatformException("Account not found.");

        return Ok(recovery);
    }

    [HttpPatch, Route("link"), RequireAuth]
    public ActionResult Link()
    {
        return Ok(_playerService.LinkAccounts(Token.AccountId));
    }

    [HttpPost, Route("login"), NoAuth, HealthMonitor(weight: 1)]
    public ActionResult Login()
    {
        DeviceInfo device = Require<DeviceInfo>("device");
        SsoInput sso = Optional<SsoInput>("sso")?.ValidateTokens();

        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player player = fromDevice.Parent ?? fromDevice;
        Player[] others = _playerService.FromSso(sso);
        
        int discriminator = _discriminatorService.Lookup(player);        
        player.Screenname ??= _nameGeneratorService.Next;
        player.LastLogin = Timestamp.UnixTime;
        ValidatePlayerScreenname(ref player);
        
        player.Token = GenerateToken(player);

        Player[] conflicts = others
            .Where(other => other.Id != player.Id)
            .ToArray();
        if (conflicts.Any())  // We have an account conflict!
        {
            _playerService.Update(player);
            foreach (Player conflict in conflicts)
                conflict.Token = GenerateToken(conflict);
            string[] ids = others
                .Select(other => other.Id)
                .Union(new[] { player.Id })
                .ToArray();
            string code = _playerService.SetLinkCode(ids);

            return Ok(new RumbleJson
            {
                { "errorCode", "accountConflict" },
                { "accountId", player.AccountId },
                { "player", player },
                { "conflicts", others.Where(other => other.Id != player.Id) },
                { "transferToken", code },
                { "sso", sso }
            });
        }



        player.GoogleAccount ??= sso?.GoogleAccount;
        player.IosAccount ??= sso?.IosAccount;
        
        
        _playerService.Update(player);

        return Ok(new RumbleJson
        {
            { "remoteAddr", GeoIPData?.IPAddress ?? IpAddress },
            { "geoipAddr", GeoIPData?.IPAddress ?? IpAddress },
            { "country", GeoIPData?.CountryCode },
            { "serverTime", Timestamp.UnixTime },
            { "requestId", HttpContext.Request.Headers["X-Request-ID"].ToString() ?? Guid.NewGuid().ToString() },
            { "player", player },
            { "discriminator", discriminator },
            { "ssoData", sso }
        });
    }

    private string GenerateToken(Player player)
    {
        int discriminator = _discriminatorService.Lookup(player);
        return _apiService
            .GenerateToken(
                player.AccountId,
                player.Screenname,
                email: null, 
                discriminator, 
                audiences: TOKEN_AUDIENCE
            );
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
        player.Screenname = _accountService
            .Lookup(player.AccountId)
            ?.Data
            ?.Optional<string>("accountName");
		
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
