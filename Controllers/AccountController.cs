using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Models.Login;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2/account"), RequireAuth]
public class AccountController : PlatformController
{
#pragma warning disable
    private readonly PlayerAccountService _playerService;
    private readonly DynamicConfig _dynamicConfig;
    private readonly ItemService _itemService;
    private readonly NameGeneratorService _nameGeneratorService;
    private readonly SaltService _saltService;
    private readonly LockoutService _lockoutService;
	
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
    /// Adds an Apple account to the player's record.
    /// </summary>
    [HttpPatch, Route("apple")]
    public ActionResult LinkApple()
    {
        try
        {
            // TODO: Require<AppleAccount>() / Validate()?
            AppleAccount apple = AppleAccount.ValidateToken(
                token: Require<string>(SsoData.FRIENDLY_KEY_APPLE_TOKEN), 
                nonce: Require<string>(SsoData.FRIENDLY_KEY_APPLE_NONCE)
            );
            Player player = _playerService.Find(Token.AccountId);
            
            _playerService.EnsureSsoAccountDoesNotExist(Token.AccountId, apple);

            return Ok(_playerService.AttachSsoAccount(player, apple)?.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }
    
    /// <summary>
    /// Removes an Apple account from the player's record.
    /// </summary>
    [HttpDelete, Route("appleAccount")]
    public ActionResult DeleteAppleAccount()
    {
        // PlatformEnvironment.EnforceNonprod(); // Probably needed in prod eventually

        string playerId = Token.AccountId;
        try
        {
            // When using postman, '+' comes through as a space because it's not URL-encoded.
            // This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
            if (_playerService.DeleteAppleAccountById(playerId) == 0 && _playerService.DeleteAppleAccountById(playerId.Trim().Replace(" ", "+")) == 0)
                throw new RecordNotFoundException(_playerService.CollectionName, "Apple account not found.", data: new RumbleJson
                {
                    { "accountId", playerId }
                });
        }
        catch (Exception e)
        {
            throw new PlatformException(message: "Error occurred while trying to delete Apple account from player.", inner: e);
        }
		
        return Ok();
    }

    /// <summary>
    /// Adds a Google account to the player's record.
    /// </summary>
    [HttpPatch, Route("google")]
    public ActionResult LinkGoogle()
    {
        try
        {
            GoogleAccount google = GoogleAccount.ValidateToken(Require<string>(SsoData.FRIENDLY_KEY_GOOGLE_TOKEN));
            Player player = _playerService.Find(Token.AccountId);
            
            _playerService.EnsureSsoAccountDoesNotExist(Token.AccountId, google);
        
            return Ok(_playerService.AttachSsoAccount(player, google)?.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }
    
    /// <summary>
    /// Removes a Google account from the player's record.
    /// </summary>
    [HttpDelete, Route("googleAccount")]
    public ActionResult DeleteGoogleAccount()
    {
        // PlatformEnvironment.EnforceNonprod(); // Probably needed in prod eventually

        string playerId = Token.AccountId;
        try
        {
            // When using postman, '+' comes through as a space because it's not URL-encoded.
            // This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
            if (_playerService.DeleteGoogleAccountById(playerId) == 0 && _playerService.DeleteGoogleAccountById(playerId.Trim().Replace(" ", "+")) == 0)
                throw new RecordNotFoundException(_playerService.CollectionName, "Google account not found.", data: new RumbleJson
                {
                    { "accountId", playerId }
                });
        }
        catch (Exception e)
        {
            throw new PlatformException(message: "Error occurred while trying to delete Plarium account from player.", inner: e);
        }
		
        return Ok();
    }
    
    /// <summary>
    /// Adds a Plarium account to the player's record.
    /// </summary>
    [HttpPatch, Route("plarium")]
    public ActionResult LinkPlarium()
    {
        try
        {
            string code = Optional<string>(SsoData.FRIENDLY_KEY_PLARIUM_CODE);
            string token = Optional<string>(SsoData.FRIENDLY_KEY_PLARIUM_TOKEN);
		
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(token))
                throw new PlatformException($"Request did not contain one of two required fields: {SsoData.FRIENDLY_KEY_PLARIUM_CODE} or {SsoData.FRIENDLY_KEY_PLARIUM_TOKEN}.", code: ErrorCode.RequiredFieldMissing);
            
            PlariumAccount plarium = PlariumService.Instance.Verify(code, token);   // TODO: Require<PlariumAccount>() / Validate()?
            Player player = _playerService.Find(Token.AccountId);
            
            _playerService.EnsureSsoAccountDoesNotExist(Token.AccountId, plarium);
            
            return Ok(_playerService.AttachSsoAccount(player, plarium)?.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }
    
    /// <summary>
    /// Removes a Plarium account from the player's record.
    /// </summary>
    [HttpDelete, Route("plariumAccount")]
    public ActionResult DeletePlariumAccount()
    {
        // PlatformEnvironment.EnforceNonprod(); // Probably needed in prod eventually

        string playerId = Token.AccountId;
        try
        {
            // When using postman, '+' comes through as a space because it's not URL-encoded.
            // This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
            if (_playerService.DeletePlariumAccountById(playerId) == 0 && _playerService.DeletePlariumAccountById(playerId.Trim().Replace(" ", "+")) == 0)
                throw new RecordNotFoundException(_playerService.CollectionName, "Plarium account not found.", data: new RumbleJson
                {
                    { "accountId", playerId }
                });
        }
        catch (Exception e)
        {
            throw new PlatformException(message: "Error occurred while trying to delete Plarium account from player.", inner: e);
        }
		
        return Ok();
    }
    
    /// <summary>
    /// Adds a Rumble account to the player's record.  Requires external email confirmation to actually be used.
    /// </summary>
    [HttpPatch, Route("rumble")]
    public ActionResult LinkRumble()
    {
        try
        {
            RumbleAccount rumble = Require<RumbleAccount>(SsoData.FRIENDLY_KEY_RUMBLE_ACCOUNT);
            Player player = _playerService.Find(Token.AccountId);
            
            _playerService.EnsureSsoAccountDoesNotExist(Token.AccountId, rumble);
            _playerService.AttachSsoAccount(player, rumble);
            
            if (player.RumbleAccount.EmailBanned)
                throw new PlatformException($"That address has been rejected.", code: ErrorCode.EmailInvalidOrBanned);
            
            return Ok(player.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }
    
    /// <summary>
    /// Removes a Rumble account from the player's record.
    /// </summary>
    [HttpDelete, Route("rumbleAccount")]
    public ActionResult DeleteRumbleAccount()
    {
        // PlatformEnvironment.EnforceNonprod(); // Probably needed in prod eventually

        string playerId = Token.AccountId;
        try
        {
            // When using postman, '+' comes through as a space because it's not URL-encoded.
            // This is a quick kluge to enable debugging purposes without having to worry about URL-encoded params in Postman.
            if (_playerService.DeleteRumbleAccountById(playerId) == 0 && _playerService.DeleteRumbleAccountById(playerId.Trim().Replace(" ", "+")) == 0)
                throw new RecordNotFoundException(_playerService.CollectionName, "Rumble account not found.", data: new RumbleJson
                {
                    { "accountId", playerId }
                });
        }
        catch (Exception e)
        {
            throw new PlatformException(message: "Error occurred while trying to delete Rumble account from player.", inner: e);
        }
		
        return Ok();
    }

    /// <summary>
    /// Confirms an email address for a Rumble account.  Enables the Rumble account to be used as a login.
    /// We need to return Ok() even when the confirmation fails.  DMZ is expecting a 200-level code; anything other than
    /// 200 causes DMZ to throw the exception as per standardized behavior of other Platform services.
    /// </summary>
    [HttpGet, Route("confirm"), NoAuth]
    public ActionResult ConfirmAccount()
    {
        string id = Require<string>("id");
        string code = Require<string>(RumbleAccount.FRIENDLY_KEY_CODE);
        
        string failure = PlatformEnvironment.Require<string>("confirmationFailurePage");
        string success = PlatformEnvironment.Require<string>("confirmationSuccessPage")
            .Replace("{env}", PlatformEnvironment.Url("")
                .Replace("https://", "")
                .TrimEnd('/')
            );

        bool alreadyConfirmed = _playerService.Find(id)?.RumbleAccount?.Status.HasFlag(RumbleAccount.AccountStatus.Confirmed) ?? false;
        if (alreadyConfirmed)
            return Ok(new LoginRedirect(failure.Replace("{reason}", "confirmed")));
        
        Player player = _playerService.UseConfirmationCode(id, code);

        string redirectUrl = null;

        // e.g. https://eng.towersandtitans.com/email/failure/invalidCode
        if (player == null)
            return Ok(new LoginRedirect(failure.Replace("{reason}", "invalidCode")));

        _apiService
            .Request("/dmz/otp/token")
            .AddAuthorization(_playerService.GenerateToken(player))
            .OnSuccess(response => redirectUrl = success.Replace("{otp}", response.Require<string>("otp")))
            .OnFailure(response =>
            {
                Log.Error(Owner.Will, "Unable to generate OTP.", data: new
                {
                    Player = player,
                    Response = response
                });
                _apiService.Alert(
                    title: "Account Confirmation OTP Generation Failure",
                    message: "One-time password generation is failing in player-service ",
                    countRequired: 15,
                    timeframe: 600,
                    owner: Owner.Will,
                    impact: ImpactType.ServicePartiallyUsable,
                    data: response.AsRumbleJson,
                    confluenceLink: "https://rumblegames.atlassian.net/wiki/spaces/TH/pages/3317497857/Account+Confirmation+OTP+Generation+Failure"
                );
                redirectUrl = failure.Replace("{reason}", "otpFailure");
            })
            .Post();
        
        // e.g. https://eng.towersandtitans.com/email/dev.nonprod.cdrentertainment.com/success/deadbeefdeadbeefdeadbeef
        return Ok(new LoginRedirect(redirectUrl));
    }

    [HttpPatch, Route("twoFactor")]
    public ActionResult VerifyTwoFactor()
    {
        try
        {
            string code = Require<string>("code");

            Player output = _playerService.UseTwoFactorCode(Token.AccountId, code)
                ?? throw new RecordNotFoundException(_playerService.CollectionName, "Invalid or expired code.");

            return Ok(output.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }

    /// <summary>
    /// Starts the password reset process.  Doing this sends an email to the player with a 2FA recovery code.
    /// </summary>
    [HttpPatch, Route("recover"), NoAuth]
    public ActionResult RecoverAccount()
    {
        try
        {
            return Ok(_playerService
                .BeginReset(Require<string>(RumbleAccount.FRIENDLY_KEY_EMAIL))
                .Prune()
            );
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }

    /// <summary>
    /// Primes a Rumble account to accept a new password hash without knowledge of the old one.  Comes in after 2FA codes.
    /// </summary>
    [HttpPatch, Route("reset"), NoAuth]
    public ActionResult UsePasswordRecoveryCode()
    {
        try
        {
            string username = Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME);
            string code = Require<string>(RumbleAccount.FRIENDLY_KEY_CODE);
            string accountId = Token?.AccountId ?? Optional<string>(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID);

            if (string.IsNullOrWhiteSpace(accountId))
                accountId = null;

            accountId?.MustBeMongoId();

            return Ok(_playerService
                .CompleteReset(username, code)
                ?.Prune()
            );
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }

    /// <summary>
    /// Changes a password hash.  The oldHash is optional iff /reset has been hit successfully.
    /// </summary>
    [HttpPatch, Route("password"), NoAuth]
    public ActionResult ChangePassword()
    {
        try
        {
            string username = Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME);
            string oldHash = Optional<string>("oldHash");
            string newHash = Require<string>("newHash");

            if (string.IsNullOrWhiteSpace(newHash))
                throw new InvalidPasswordException(username, "Passwords cannot be empty or null.");
            if (oldHash == newHash)
                throw new InvalidPasswordException(username, "Passwords cannot be the same.");

            Player output = _playerService.UpdateHash(username, oldHash, newHash, Token?.AccountId);

            return Ok(output.Prune());
        }
        catch (PlatformException e)
        {
            return Problem(new LoginDiagnosis(e));
        }
    }

    /// <summary>
    /// Take over all related accounts as children.  The provided token represents the parent-to-be account.
    /// </summary>
    [HttpPatch, Route("adopt")]
    public ActionResult Link() => Ok(_playerService.LinkAccounts(Token.AccountId));

    [HttpGet, Route("salt")]
    public ActionResult GetSalt()
    {
        string username = Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME).ToLower().Trim();

        if (!EmailRegex.IsValid(username))
        {
            Log.Error(Owner.Will, "Incoming request is using an invalid email address to generate a salt", data: new
            {
                Email = username,
                Help = "This can frequently be an issue with the query string.  If not encoded, emails with a + in them don't come in to platform.  The + needs to be encoded for those situations."
            });
            return Problem(new LoginDiagnosis(new PlatformException($"Email address is invalid ({username}).", code: ErrorCode.EmailInvalidOrBanned)));
        }

        return Ok(new RumbleJson
        {
            {
                Salt.FRIENDLY_KEY_SALT, _saltService
                    .Fetch(username: Require<string>(RumbleAccount.FRIENDLY_KEY_USERNAME).ToLower())
                    ?.Value
            }
        });
    }

    [HttpGet, Route("refresh")]
    public ActionResult RefreshToken()
    {
        Player player = _playerService.Find(Token.AccountId);
        _playerService.GenerateToken(player);

        return Ok(player?.Prune());
    }

    /// <summary>
    /// Uses device information and optional SSO information to find the appropriate player accounts.  If more than one
    /// account is found, a 400-level response is returned with necessary data for the client / server to work with.
    /// </summary>
    /// <returns>Relevant player accounts with generated tokens.</returns>
    [HttpPost, Route("login"), NoAuth, HealthMonitor(weight: 1)]
    public ActionResult Login()
    {
        SsoData sso = null;
        try
        {
            // Device used to be a required field.  However, in order to support web logins for marketplace, it must now
            // be an optional field.  Websites can't possibly know the correct installId, and as such can only be used
            // to access existing linked accounts through SSO.
            DeviceInfo device = Optional<DeviceInfo>(Player.FRIENDLY_KEY_DEVICE);
            bool isWeb = device == null;
            sso = Optional<SsoData>("sso")?.ValidateTokens();
            Player player;
            Player[] others = _playerService.FromSso(sso, IpAddress, isWeb);
            Log.Local(Owner.Will, others.FirstOrDefault()?.PlariumAccount?.Id);

            if (isWeb)
            {
                if (others.All(other => other == null))
                    throw new RecordsFoundException(expected: 1, found: 0, reason: "No player exists with provided SSO.");
                player = others.First();
            }
            else
            {
                Player fromDevice = _playerService.FromDevice(device, GeoIPData);
                player = fromDevice.Parent ?? fromDevice;
            }

            sso?.ValidatePlayers(others.Union(new[] { player }).ToArray());

            _playerService.GenerateToken(player);

            bool twoFactorEnabled = !(sso?.SkipTwoFactor ?? false);
            if (AccountConflictExists(player, others, twoFactorEnabled, out ActionResult conflictResult))
                return conflictResult;

            // Limit the hits to our DB.  This is the only assignment happening
            bool updateRequired = 
                player.GoogleAccount == null && sso?.GoogleAccount != null
                || player.AppleAccount == null && sso?.AppleAccount != null
                || player.PlariumAccount == null && sso?.PlariumAccount != null;
            
            player.GoogleAccount ??= sso?.GoogleAccount;
            player.AppleAccount ??= sso?.AppleAccount;
            player.PlariumAccount ??= sso?.PlariumAccount;

            if (updateRequired)
                _playerService.Update(player);
            return Ok(new RumbleJson
            {
                { "geoData", GeoIPData },
                { "requestId", HttpContext.Request.Headers["X-Request-ID"].ToString() ?? Guid.NewGuid().ToString() },
                { "player", player.Prune() }
            });
        }
        catch (PlatformException e)
        {
            LoginDiagnosis diagnosis = new(e);

            if (diagnosis.PasswordInvalid && !string.IsNullOrWhiteSpace(sso?.RumbleAccount?.Email))
                _lockoutService.RegisterError(sso.RumbleAccount.Email, IpAddress);
            
            return Problem(new LoginDiagnosis(e));
        }
    }

    [HttpGet, Route("status")]
    public ActionResult GetAccountStatus()
    {
        Player output = _playerService.FromToken(Token);

        RumbleAccount rumble = output.RumbleAccount;

        if (rumble != null && rumble.Status == RumbleAccount.AccountStatus.NeedsConfirmation && !rumble.EmailBanned)
        {
            _apiService
                .Request("/dmz/bounces/valid")
                .AddParameter("email", rumble.Email)
                .OnFailure(response =>
                {
                    if (response.StatusCode != 400)
                        return;
                    rumble.EmailBanned = true;
                    rumble.Status = RumbleAccount.AccountStatus.EmailInvalid;
                })
                .Get();
        }
        
        return Ok(output.Prune());
    }

    #region Utilities

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
            throw new RecordsFoundException(1, rumbles.Length, "More than one Rumble account found.");

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
            { "player", player.Prune() },
            { "rumble", rumble.Prune() }
        });
        return true;
    }

    private bool AccountConflictExists(Player player, Player[] others, bool twoFactorEnabled, out ActionResult conflictResult)
    {
        conflictResult = null;
        Player[] conflicts = others
            .Where(other => other.Id != player.Id)
            .ToArray();

        if (!conflicts.Any())
            return false;

        if (twoFactorEnabled && TwoFactorRequired(player, others, out conflictResult))
            return true;

        _playerService.Update(player);
        foreach (Player conflict in conflicts)
            _playerService.GenerateToken(conflict);

        _playerService.SetLinkCode(others
            .Select(other => other.Id)
            .Union(new[] { player.Id })
            .ToArray()
        );
        _playerService.SendLoginNotifications(player.Device?.Type, conflicts
            .Union(new[] { player })
            .Select(p => p?.RumbleAccount?.Email)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .ToArray()
        );

        conflictResult = Problem(new RumbleJson
        {
            { "geoData", GeoIPData },
            { "errorCode", "accountConflict" },
            { "player", player.Prune() },
            { "conflicts", others.Where(other => other.Id != player.Id) }
        });
        return true;
    }
    #endregion Utilities
}