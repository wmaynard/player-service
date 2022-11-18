using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using PlayerService.Models;
using PlayerService.Models.Login;
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

[ApiController, Route("player/v2/account")]
public class AccountController : PlatformController
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
    private readonly NameGeneratorService _nameGeneratorService;
    private readonly AuditService _auditService;
    private readonly SaltService _saltService;
	
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
    public AccountController() => ComponentServices = new Dictionary<string, ComponentService>
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

    /// <summary>
    /// Adds a Google account to the player's record.
    /// </summary>
    [HttpPatch, Route("google")]
    public ActionResult LinkGoogle()
    {
        DeviceInfo device = Require<DeviceInfo>(Player.FRIENDLY_KEY_DEVICE);
        GoogleAccount google = GoogleAccount.ValidateToken(Require<string>(SsoData.FRIENDLY_KEY_GOOGLE_TOKEN));
        
        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player fromGoogle = _playerService.FromGoogle(google);

        if (fromGoogle != null && fromDevice.Id != fromGoogle.Id)
            throw new PlatformException("Account conflict.");
        
        if (fromGoogle != null)
            throw new PlatformException("Account already linked.");

        return Ok(_playerService.AttachGoogle(fromDevice, google));
    }
    
    /// <summary>
    /// Adds a Rumble account to the player's record.  Requires external email confirmation to actually be used.
    /// </summary>
    [HttpPatch, Route("rumble")]
    public ActionResult LinkRumble()
    {
        DeviceInfo device = Require<DeviceInfo>(Player.FRIENDLY_KEY_DEVICE);
        RumbleAccount rumble = Require<RumbleAccount>(SsoData.FRIENDLY_KEY_RUMBLE_ACCOUNT);

        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player fromRumble = _playerService.FromRumble(rumble, mustExist: false, mustNotExist: true);
        
        if (fromRumble != null && fromDevice.Id != fromRumble.Id)
            throw new PlatformException("Account conflict.  The account exists on a different account and can't be added to this one.");

        if (fromRumble != null)
            throw new PlatformException("Account already linked.");

        _playerService.AttachRumble(fromDevice, rumble);
        return Ok(fromDevice);
    }

    /// <summary>
    /// Confirms an email address for a Rumble account.  Enables the Rumble account to be used as a login.
    /// </summary>
    [HttpGet, Route("confirm")]
    public ActionResult ConfirmAccount()
    {
        string id = Require<string>("id");
        string code = Require<string>(RumbleAccount.FRIENDLY_KEY_CODE);

        Player player = _playerService.UseConfirmationCode(id, code)
            ?? throw new PlatformException("Incorrect or expired code.");

        long affected = _playerService.ClearUnconfirmedAccounts(player.RumbleAccount);
        if (affected > 0)
            Log.Warn(Owner.Will, "Account confirmation cleared other unconfirmed accounts.", data: new
            {
                Affected = affected,
                Detail = "The player likely had other attempts on other devices when trying to link their account.",
                Player = player.RumbleAccount.Email
            });

        return Ok(player);
    }


    [HttpPatch, Route("twoFactor")]
    public ActionResult VerifyTwoFactor()
    {
        string code = Require<string>("code");

        Player output = _playerService.UseTwoFactorCode(Token.AccountId, code)
            ?? throw new PlatformException("Invalid or expired code.");

        return Ok(output);
    }
    

    /// <summary>
    /// Starts the password reset process.  Doing this sends an email to the player with a 2FA recovery code.
    /// </summary>
    [HttpPatch, Route("recover")]
    public ActionResult RecoverAccount() => Ok(_playerService.BeginReset(Require<string>(RumbleAccount.FRIENDLY_KEY_EMAIL)));

    /// <summary>
    /// Primes a Rumble account to accept a new password hash without knowledge of the old one.  Comes in after 2FA codes.
    /// </summary>
    [HttpPatch, Route("reset")]
    public ActionResult UsePasswordRecoveryCode()
    {
        string username = Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME);
        string code = Require<string>(RumbleAccount.FRIENDLY_KEY_CODE);

        return Ok(_playerService.CompleteReset(username, code));
    }

    /// <summary>
    /// Changes a password hash.  The oldHash is optional iff /reset has been hit successfully.
    /// </summary>
    [HttpPatch, Route("password")]
    public ActionResult ChangePassword()
    {
        string username = Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME);
        string oldHash = Optional<string>("oldHash");
        string newHash = Require<string>("newHash");

        if (oldHash == newHash)
            throw new PlatformException("Invalid hash.  Passwords cannot be the same.");
        if (string.IsNullOrWhiteSpace(newHash))
            throw new PlatformException("Invalid hash.  Cannot be empty or null.");

        Player output = _playerService.UpdateHash(username, oldHash, newHash);
        output.Token = GenerateToken(output);

        return Ok(output);
    }

    /// <summary>
    /// Take over all related accounts as children.  The provided token represents the parent-to-be account.
    /// </summary>
    [HttpPatch, Route("adopt"), RequireAuth]
    public ActionResult Link() => Ok(_playerService.LinkAccounts(Token.AccountId));

    [HttpGet, Route("salt"), RequireAuth]
    public ActionResult GetSalt() => Ok(new RumbleJson
    {
        { Salt.FRIENDLY_KEY_SALT, _saltService.Fetch(username: Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME))?.Value }
    });

    [HttpGet, Route("refresh"), RequireAuth]
    public ActionResult RefreshToken()
    {
        Player player = _playerService.Find(Token.AccountId);
        GenerateToken(player);

        return Ok(player);
    }

    /// <summary>
    /// Uses device information and optional SSO information to find the appropriate player accounts.  If more than one
    /// account is found, a 400-level response is returned with necessary data for the client / server to work with.
    /// </summary>
    /// <returns>Relevant player accounts with generated tokens.</returns>
    [HttpPost, Route("login"), NoAuth, HealthMonitor(weight: 1)]
    public ActionResult Login()
    {
        DeviceInfo device = Require<DeviceInfo>(Player.FRIENDLY_KEY_DEVICE);
        SsoData sso = Optional<SsoData>("sso")?.ValidateTokens();

        Player fromDevice = _playerService.FromDevice(device, isUpsert: true);
        Player player = fromDevice.Parent ?? fromDevice;
        Player[] others = _playerService.FromSso(sso);
        
        player.Discriminator = _discriminatorService.Lookup(player);
        player.LastLogin = Timestamp.UnixTime;
        
        ValidatePlayerScreenname(ref player);
        sso?.ValidatePlayers(others.Union(new []{ player }).ToArray());

        GenerateToken(player);

        if (AccountConflictExists(player, others, out ActionResult conflictResult))
            return conflictResult;

        player.GoogleAccount ??= sso?.GoogleAccount;
        player.AppleAccount ??= sso?.AppleAccount;

        if (player.LinkExpiration > 0 && player.LinkExpiration <= Timestamp.UnixTime)
            _playerService.RemoveExpiredLinkCodes();

        _playerService.Update(player);

        return Ok(new RumbleJson
        {
            { "geoData", GeoIPData },
            { "requestId", HttpContext.Request.Headers["X-Request-ID"].ToString() ?? Guid.NewGuid().ToString() },
            { "player", player }
        });
    }

    [HttpGet, Route("status")]
    public ActionResult GetAccountStatus() => Ok(_playerService.FromToken(Token));

#region Utilities
    private string GenerateToken(Player player)
    {
        int discriminator = _discriminatorService.Lookup(player);
        player.Token = _apiService
            .GenerateToken(
                player.AccountId,
                player.Screenname,
                email: null, 
                discriminator, 
                audiences: TOKEN_AUDIENCE
            );
        return player.Token;
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

    private string[] GetEmailAddresses(IEnumerable<Player> players)
    {
        return players
            .Where(player => player != null)
            .SelectMany(player => new[]
            {
                player.GoogleAccount?.Email,
                player.RumbleAccount?.Email
            })
            .Where(email => email != null)
            .ToArray();
    }

    private bool TwoFactorRequired(Player player, Player[] others, out ActionResult unverifiedResult)
    {
        unverifiedResult = null;
        RumbleAccount[] rumbles = others
            .Union(new[] { player })
            .Select(account => account.RumbleAccount)
            .Where(rumble => rumble != null && rumble.Status.HasFlag(RumbleAccount.AccountStatus.Confirmed))
            .ToArray();

        if (!rumbles.Any())
            return false;
        if (rumbles.Length > 1)
            throw new PlatformException("More than one rumble account found.");

        RumbleAccount rumble = rumbles.First();

        if (rumble.ConfirmedIds.Contains(player.Id))
            return false;

        _playerService.SetLinkCode(ids: others
            .Union(new []{ player })
            .Select(account => account.Id)
            .ToArray());
        _playerService.SendTwoFactorNotification(rumble.Email);

        unverifiedResult = Problem(new RumbleJson
        {
            { "geoData", GeoIPData },
            { "errorCode", "verificationRequired" },
            { "player", player },
            { "rumble", rumble }
        });
        return true;
    }

    private bool AccountConflictExists(Player player, Player[] others, out ActionResult conflictResult)
    {
        conflictResult = null;
        Player[] conflicts = others
            .Where(other => other.Id != player.Id)
            .ToArray();

        if (!conflicts.Any())
            return false;

        if (TwoFactorRequired(player, others, out conflictResult))
            return true;

        _playerService.Update(player);
        foreach (Player conflict in conflicts)
        {
            conflict.Discriminator = _discriminatorService.Lookup(conflict);
            GenerateToken(conflict);
        }

        string[] emails = GetEmailAddresses(conflicts.Union(new[] { player }));
        string[] ids = others
            .Select(other => other.Id)
            .Union(new[] { player.Id })
            .ToArray();

        _playerService.SetLinkCode(ids);
        foreach (string email in emails)
            _playerService.SendLoginNotification(player, email);

        conflictResult = Problem(new RumbleJson
        {
            { "geoData", GeoIPData },
            { "errorCode", "accountConflict" },
            { "player", player },
            { "conflicts", others.Where(other => other.Id != player.Id) }
        });
        return true;
    }

    #endregion Utilities
}