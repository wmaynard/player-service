using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PlayerService.Controllers;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Models.Login;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using StackExchange.Redis;

namespace PlayerService.Services;

public class PlayerAccountService : MinqTimerService<Player>
{
    public const Audience TOKEN_AUDIENCE = 
        Audience.ChatService
        | Audience.DmzService
        | Audience.GuildService
        | Audience.LeaderboardService
        | Audience.MailService
        | Audience.MatchmakingService
        | Audience.MultiplayerService
        | Audience.NftService
        | Audience.PlayerService
        | Audience.PvpService
        | Audience.ReceiptService
        | Audience.GameServer;
    
    private readonly DynamicConfig _config;
    private readonly ApiService _api;
    private readonly Random _rando;

    public string CollectionName => mongo.CollectionName;
    
    public PlayerAccountService(ApiService api, DynamicConfig config) : base("players", IntervalMs.FourHours)
    {
        _api = api;
        _config = config;
        _rando = new Random();
    }
    
    public Player Find(string accountId) => mongo.FirstOrDefault(query => query.EqualTo(player => player.Id, accountId));
    public Player FromToken(TokenInfo token) => Find(token.AccountId)
        ?? throw new RecordNotFoundException(mongo.CollectionName, "Account not found.");

    public bool InstallIdExists(DeviceInfo device) => !string.IsNullOrWhiteSpace(device?.InstallId)
        && mongo.Count(query => query.EqualTo(player => player.Device.InstallId, device.InstallId)) > 0;

    public int SyncScreenname(string screenname, string accountId, bool fromAdmin = false)
    {
        Require<AccountService>().SetScreenname(accountId, screenname, fromAdmin);
        return (int)mongo
            .Where(query => query.EqualTo(player => player.Id, accountId))
            .Or(query => query.EqualTo(player => player.ParentId, accountId))
            .Update(query => query.Set(player => player.Screenname, screenname));
    }

    public Player FromDevice(DeviceInfo device, GeoIPData geoIpData)
    {
        Player stored = mongo
            .FirstOrDefault(query => query.EqualTo(player => player.Device.InstallId, device.InstallId));
        
        // One of these conditions must be true; no record exists on the database, keysAuthorized is always true.
        device.Compare(stored?.Device, out bool devicesIdentical, out bool keysAuthorized);
        if (!(devicesIdentical || keysAuthorized))
            throw new DeviceMismatchException();

        // If the devices are identical, our DB record exists, but the keys are wrong.  This could be an attack.
        bool pkProvided = !string.IsNullOrWhiteSpace(device.PrivateKey);
        if (pkProvided && devicesIdentical && !keysAuthorized)
            throw new DeviceMismatchException();

        stored = mongo
            .Where(query => query.EqualTo(player => player.Device.InstallId, device.InstallId))
            .Upsert(query =>
            {
                query
                    .Set(player => player.Device.ClientVersion, device.ClientVersion)
                    .Set(player => player.Device.DataVersion, device.DataVersion)
                    .Set(player => player.Device.Language, device.Language)
                    .Set(player => player.Device.OperatingSystem, device.OperatingSystem)
                    .Set(player => player.Device.Type, device.Type)
                    .Set(player => player.LastLogin, Timestamp.Now)
                    .Increment(player => player.SessionCount, 1)
                    .Set(player => player.Device.ConfirmedPrivateKey, device.PrivateKey)
                    .SetOnInsert(player => player.Screenname, Require<NameGeneratorService>().Next);

                if (geoIpData != null)
                    query.Set(player => player.LocationData, geoIpData);
            });

        if (stored.Discriminator == null)
            AssignDiscriminator(stored);

        stored.Device?.CalculatePrivateKey();

        // Look for a parent account, if necessary.
        if (!string.IsNullOrWhiteSpace(stored.ParentId))
            stored.Parent = mongo.ExactId(stored.ParentId).FirstOrDefault();
        
        return stored.Parent ?? stored;
    }

    /// <summary>
    /// Attempts to assign a discriminator to a player account.  If this fails, the player's discriminator will be 0.
    /// Discriminators of 0 are not guaranteed to be unique.
    /// </summary>
    /// <param name="account">The account to assign a discriminator to.</param>
    /// <param name="desired">The desired discriminator, if any.  If null, a random number will be assigned.</param>
    /// <returns></returns>
    private int AssignDiscriminator(Player account, int? desired = null)
    {
        int attempts = 0;
        string sn = account.Screenname;
        while (attempts++ < 50)
        {
            desired ??= _rando.Next(1, 9_999);
            bool exists = mongo
                .Where(query => query
                    .EqualTo(player => player.Screenname, sn)
                    .EqualTo(player => player.Discriminator, desired)
                    .NotEqualTo(player => player.ParentId, account.Id)
                )
                .Count() > 0;

            if (exists)
            {
                desired = null;
                continue;
            }

            mongo
                .ExactId(account.Id)
                .Limit(1)
                .Update(query => query.Set(player => player.Discriminator, desired));
            
            account.Discriminator = desired;
            return (int)desired;
        }

        desired = 0;
        
        mongo
            .ExactId(account.Id)
            .Limit(1)
            .Update(query => query.Set(player => player.Discriminator, desired));
        
        Log.Error(Owner.Will, "Unable to generate a discriminator for an account.", data: new
        {
            Attempts = attempts,
            Help = "A discriminator was not available after multiple attempts.  It will be 0 for this account, and may not be unique.",
            AccountId = account.Id
        });
        account.Discriminator = desired;
        return (int)desired;
    }


    public Player[] FromSso(SsoData sso, string ipAddress, bool fromWeb)
    {
        if (sso == null || !sso.HasAtLeastOneAccount())
            return Array.Empty<Player>();

        Player[] output = mongo
            .Where(query =>
            {
                if (sso.GoogleAccount != null)
                    query.EqualTo(player => player.GoogleAccount.Id, sso.GoogleAccount.Id);
                if (sso.AppleAccount != null)
                    query.EqualTo(player => player.AppleAccount.Id, sso.AppleAccount.Id);
                if (sso.PlariumAccount != null)
                    query.EqualTo(player => player.PlariumAccount.Id, sso.PlariumAccount.Id);
                if (sso.RumbleAccount != null)
                    query
                        .EqualTo(player => player.RumbleAccount.Username, sso.RumbleAccount.Username)
                        .EqualTo(player => player.RumbleAccount.Hash, sso.RumbleAccount.Hash)
                        .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed);
            })
            .Limit(100)
            .UpdateAndReturn(update =>
            {
                if (fromWeb)
                {
                    if (sso.AppleAccount != null)
                        update
                            .Set(player => player.AppleAccount.IpAddress, ipAddress)
                            .Increment(player => player.AppleAccount.WebValidationCount)
                            .Increment(player => player.AppleAccount.LifetimeValidationCount);
                    if (sso.GoogleAccount != null)
                        update
                            .Set(player => player.GoogleAccount.IpAddress, ipAddress)
                            .Increment(player => player.GoogleAccount.WebValidationCount)
                            .Increment(player => player.GoogleAccount.LifetimeValidationCount);
                    if (sso.PlariumAccount != null)
                        update
                            .Set(player => player.PlariumAccount.IpAddress, ipAddress)
                            .Increment(player => player.PlariumAccount.WebValidationCount)
                            .Increment(player => player.PlariumAccount.LifetimeValidationCount);
                    if (sso.RumbleAccount != null)
                        update
                            .Set(player => player.RumbleAccount.IpAddress, ipAddress)
                            .Increment(player => player.RumbleAccount.WebValidationCount)
                            .Increment(player => player.RumbleAccount.LifetimeValidationCount);
                }
                else
                {
                    if (sso.AppleAccount != null)
                        update
                            .Set(player => player.AppleAccount.IpAddress, ipAddress)
                            .Increment(player => player.AppleAccount.ClientValidationCount)
                            .Increment(player => player.AppleAccount.LifetimeValidationCount);
                    if (sso.GoogleAccount != null)
                        update
                            .Set(player => player.GoogleAccount.IpAddress, ipAddress)
                            .Increment(player => player.GoogleAccount.ClientValidationCount)
                            .Increment(player => player.GoogleAccount.LifetimeValidationCount);
                    if (sso.PlariumAccount != null)
                        update
                            .Set(player => player.PlariumAccount.IpAddress, ipAddress)
                            .Increment(player => player.PlariumAccount.ClientValidationCount)
                            .Increment(player => player.PlariumAccount.LifetimeValidationCount);
                    if (sso.RumbleAccount != null)
                        update
                            .Set(player => player.RumbleAccount.IpAddress, ipAddress)
                            .Increment(player => player.RumbleAccount.ClientValidationCount)
                            .Increment(player => player.RumbleAccount.LifetimeValidationCount);
                }
            });
        
        if (sso.GoogleAccount != null && output.All(player => player.GoogleAccount == null))
            throw new GoogleUnlinkedException();
        if (sso.AppleAccount != null && output.All(player => player.AppleAccount == null))
            throw new AppleUnlinkedException();
        if (sso.PlariumAccount != null && output.All(player => player.PlariumAccount == null))
            throw new PlariumUnlinkedException();
        if (sso.RumbleAccount != null && output.All(player => player.RumbleAccount == null))
            throw DiagnoseEmailPasswordLogin(sso.RumbleAccount.Email, sso.RumbleAccount.Hash);

        return output;
    }

    private static Player EnsureZeroOrOneResult(Player[] results) => results?.Length <= 1
        ? results.FirstOrDefault()
        : throw new RecordsFoundException(1, results?.Length ?? 0);

    public void EnsureSsoAccountDoesNotExist(string accountId, ISsoAccount account)
    {
        string existing = mongo
            .Where(query =>
            {
                switch (account)
                {
                    case AppleAccount apple:
                        query.EqualTo(player => player.AppleAccount.Id, account.Id);
                        break;
                    case GoogleAccount google:
                        query.EqualTo(player => player.GoogleAccount.Id, account.Id);
                        break;
                    case PlariumAccount plarium:
                        query.EqualTo(player => player.PlariumAccount.Id, plarium.Id);
                        break;
                    case RumbleAccount rumble:
                        query
                            .EqualTo(player => player.RumbleAccount.Email, rumble.Email)
                            .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed);
                        break;
                }
            })
            .Limit(1)
            .Project(player => player.Id)
            .FirstOrDefault();

        if (existing == null)
            return;
        
        if (!PlatformEnvironment.IsProd)
            Log.Info(Owner.Will, $"SSO account conflict encountered", data: new
            {
                SsoData = account,
                ExistingAccountId = existing,
                RequestingAccountId = accountId
            });
        
        throw existing == accountId
            ? new AlreadyLinkedAccountException(account.GetType().Name)
            : new AccountOwnershipException(account.GetType().Name, accountId, existing);
    }

    public Player CompleteLink(RumbleAccount rumble)
    {
        long usernameCount = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Email, rumble.Email)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .Count();
        
        if (usernameCount > 0)
            throw new AccountOwnershipException("Rumble", "The username or email is already in use.");

        Player[] results = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Email, rumble.Email)
                .EqualTo(player => player.RumbleAccount.Hash, rumble.Hash)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .ToArray();

        return results.Length switch
        {
            > 1 => throw new RecordsFoundException(1, results.Length),
            _ => results.FirstOrDefault()
        };
    }
    
    public Player UpdateHash(string username, string oldHash, string newHash, string callingAccountId)
    {
        Player output = mongo
            .Where(query =>
            {
                FilterChain<Player> filter = query.EqualTo(player => player.RumbleAccount.Username, username);
                if (!string.IsNullOrWhiteSpace(oldHash))
                    filter.EqualTo(player => player.RumbleAccount.Hash, oldHash);
            })
            .Limit(1)
            .UpdateAndReturnOne(query =>
            {
                UpdateChain<Player> update = query.Set(player => player.RumbleAccount.Hash, newHash);
                if (!string.IsNullOrWhiteSpace(callingAccountId))
                    update.AddItems(player => player.RumbleAccount.ConfirmedIds, limitToKeep: 20, callingAccountId);
            }) ?? throw new RecordNotFoundException(mongo.CollectionName, "Account not found.");

        GenerateToken(output);
        return output;
    }

    public Player AttachSsoAccount(Player player, ISsoAccount account)
    {
        switch (account)
        {
            case RumbleAccount rumble:
                rumble.Status = RumbleAccount.AccountStatus.NeedsConfirmation;
                rumble.CodeExpiration = Timestamp.FifteenMinutesFromNow;
                rumble.ConfirmationCode = RumbleAccount.GenerateCode(segments: 10);
                player.RumbleAccount = rumble;
                _api
                    .Request("/dmz/player/account/confirmation")
                    .AddAuthorization(_config.AdminToken)
                    .SetPayload(new RumbleJson
                    {
                        { "email", rumble.Email },
                        { "accountId", player.Id },
                        { "code", rumble.ConfirmationCode },
                        { "expiration", rumble.CodeExpiration }
                    })
                    .OnFailure(response =>
                    {
                        player.RumbleAccount.EmailBanned = true;
                        Log.Error(Owner.Will, "Unable to send Rumble account confirmation email.", new
                        {
                            Response = response,
                            Address = rumble.Email
                        });
                    })
                    .Post();
                break;
            case GoogleAccount google:
                player.GoogleAccount = google;
                break;
            case AppleAccount apple:
                player.AppleAccount = apple;
                break;
            case PlariumAccount plarium:
                player.PlariumAccount = plarium;
                break; 
            default:
                throw new PlatformException("Unrecognized SSO account type");
        }

        account.AddedOn = Timestamp.Now;
        account.RollingLoginTimestamp = Timestamp.Now;
        account.WebValidationCount = 0;
        account.ClientValidationCount = 0;
        account.LifetimeValidationCount = 0;
        account.IpAddress = null;

        Update(player);

        try
        {
            GenerateToken(player);
        }
        catch (TokenBannedException e)
        {
            // PLATF-6466: Add info log for attempted account links that are banned
            Log.Warn(Owner.Will, "A token generation was attempted when signing into an SSO account but was banned", data: new
            {
                AccountId = player.AccountId,
                Screenname = player.Screenname,
                Help = "This may present itself as an account ID being banned when it isn't, but rather because the account it's trying to access with SSO is."
            }, exception: e);

            throw;
        }
        return player;
    }

    public Player UseConfirmationCode(string id, string code)
    {
        Player output = mongo
            .Where(query => query
                .EqualTo(player => player.Id, id)
                .EqualTo(player => player.RumbleAccount.ConfirmationCode, code)
                .LessThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
                .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.Now)
            )
            .UpdateAndReturnOne(query => query
                .Set(player => player.RumbleAccount.CodeExpiration, default)
                .Set(player => player.RumbleAccount.ConfirmationCode, null)
                .Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
                .AddItems(player => player.RumbleAccount.ConfirmedIds, limitToKeep: 20, id)
            );
        
        if (output != null)
            _api
                .Request("/dmz/player/account/welcome")
                .AddAuthorization(_config.AdminToken)
                .SetPayload(new RumbleJson
                {
                    { "email", output.RumbleAccount?.Email }
                })
                .OnFailure(response => Log.Error(Owner.Will, "Unable to send welcome email.", data: new
                {
                    Player = output,
                    Response = response
                }))
                .Post();

        return output;
    }

    public Player UseTwoFactorCode(string id, string code)
    {
        string linkCode = mongo
            .Where(query => query.EqualTo(player => player.Id, id))
            .Limit(1)
            .Project(player => player.LinkCode)
            .FirstOrDefault();

        return mongo
            .Where(query => query
                .EqualTo(player => player.LinkCode, linkCode)
                .NotEqualTo(player => player.RumbleAccount, null)
                .EqualTo(player => player.RumbleAccount.ConfirmationCode, code)
                .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.Now)
            )
            .UpdateAndReturnOne(query => query
                .AddItems(player => player.RumbleAccount.ConfirmedIds, limitToKeep: 20, id)
                .Set(player => player.RumbleAccount.CodeExpiration, default)
                .Set(player => player.RumbleAccount.ConfirmationCode, null)
                .Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            );
    }

    public Player SendTwoFactorNotification(string email)
    {
        Player output = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Email, email)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .UpdateAndReturnOne(query => query
                .Set(player => player.RumbleAccount.CodeExpiration, Timestamp.FifteenMinutesFromNow)
                .Set(player => player.RumbleAccount.ConfirmationCode, RumbleAccount.GenerateCode(segments: 2))
                .Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsTwoFactor)
            );
        
        _api
            .Request("/dmz/player/account/2fa")
            .AddAuthorization(_config.AdminToken)
            .SetPayload(new RumbleJson
            {
                { "email", email },
                { "code", output.RumbleAccount?.ConfirmationCode },
                { "expiration", output.RumbleAccount?.CodeExpiration }
            })
            .OnFailure(response => Log.Error(Owner.Will, "Unable to send 2FA code.", data: new
            {
                Player = output,
                Email = email
            }))
            .Post();

        return output;
    }

    public Player BeginReset(string email)
    {
        Player output = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Email, email)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .UpdateAndReturnOne(query => query
                .Set(player => player.RumbleAccount.CodeExpiration, Timestamp.FifteenMinutesFromNow)
                .Set(player => player.RumbleAccount.ConfirmationCode, RumbleAccount.GenerateCode(segments: 2))
                .Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetRequested)
            ) ?? throw new RumbleUnlinkedException(email);
        
        _api
            .Request("/dmz/player/account/reset")
            .AddAuthorization(_config.AdminToken)
            .SetPayload(new RumbleJson
            {
                { "email", email },
                { "accountId", output.Id },
                { "code", output.RumbleAccount?.ConfirmationCode },
                { "expiration", output.RumbleAccount?.CodeExpiration }
            })
            .OnFailure(response => Log.Error(Owner.Will, "Unable to send password reset email.", data: new
            {
                Player = output,
                Response = response
            }))
            .Post();

        return output;
    }

    public Player CompleteReset(string username, string code, string accountId = null) => mongo
        .Where(query => query
            .EqualTo(player => player.RumbleAccount.Username, username)
            .EqualTo(player => player.RumbleAccount.ConfirmationCode, code)
            .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.Now)
        )
        .UpdateAndReturnOne(query =>
        {
            UpdateChain<Player> update = query
                .Set(player => player.RumbleAccount.ConfirmationCode, null)
                .Set(player => player.RumbleAccount.CodeExpiration, default)
                .Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetPrimed);
            if (!string.IsNullOrWhiteSpace(accountId))
                update.AddItems(player => player.RumbleAccount.ConfirmedIds, limitToKeep: 20, accountId);
        }) ?? throw DiagnoseEmailPasswordLogin(username, null, code);

    public string SetLinkCode(string[] ids)
    {
        string[] overrides = mongo
            .Where(query => query.ContainedIn(player => player.ParentId, ids.Where(id => !string.IsNullOrWhiteSpace(id))))
            .Project(player => player.Id);

        string output = Guid.NewGuid().ToString();
        mongo
            .Where(query => query.ContainedIn(player => player.Id, ids.Union(overrides)))
            .Update(query => query
                .Set(player => player.LinkCode, output)
                .Set(player => player.LinkExpiration, Timestamp.FifteenMinutesFromNow)
            );
        return output;
    }

    public Player LinkAccounts(string accountId)
    {
        Player output = Find(accountId) ?? throw new RecordNotFoundException(mongo.CollectionName, "No player account found with specified ID.", data: new RumbleJson
        {
            { "accountId", accountId }
        });
        
        if (string.IsNullOrWhiteSpace(output.LinkCode))
            throw new RecordNotFoundException(mongo.CollectionName, "No matching link code found.", data: new RumbleJson
            {
                { "accountId", accountId }
            });
        
        if (output.LinkExpiration <= Timestamp.Now)
            throw new WindowExpiredException("Link code is expired.");

        Player[] others = mongo
            .Where(query => query
                .NotEqualTo(player => player.Id, output.Id)
                .Or(or => or
                    .EqualTo(player => player.LinkCode, output.LinkCode)
                    .EqualTo(player => player.ParentId, output.Id)
                )
            )
            .ToArray();
        
        if (!others.Any())
            throw new RecordNotFoundException(mongo.CollectionName, "No other accounts found to link.", data: new RumbleJson
            {
                { "accountId", accountId }
            });
        
        GoogleAccount[] googles = others
            .Select(other => other.GoogleAccount)
            .Union(new [] { output.GoogleAccount })
            .Where(account => account != null)
            .ToArray();
        AppleAccount[] apples = others
            .Select(other => other.AppleAccount)
            .Union(new [] { output.AppleAccount })
            .Where(account => account != null)
            .ToArray();
        PlariumAccount[] plariums = others
            .Select(other => other.PlariumAccount)
            .Union(new [] { output.PlariumAccount })
            .Where(account => account != null)
            .ToArray();
        RumbleAccount[] rumbles = others
            .Select(other => other.RumbleAccount)
            .Union(new [] { output.RumbleAccount })
            .Where(account => account != null && account.Status.HasFlag(RumbleAccount.AccountStatus.Confirmed)) // TODO: Getter property for this
            .ToArray();
        
        if (googles.Length > 1)
            throw new RecordsFoundException(1, googles.Length, "Multiple Google accounts found.");
        if (apples.Length > 1)
            throw new RecordsFoundException(1, apples.Length, "Multiple Apple accounts found.");
        if (plariums.Length > 1)
            throw new RecordsFoundException(1, plariums.Length, "Multiple Plarium accounts found.");
        if (rumbles.Length > 1)
            throw new RecordsFoundException(1, rumbles.Length, "Multiple Rumble accounts found.");
        
        output.GoogleAccount = googles.FirstOrDefault();
        output.AppleAccount = apples.FirstOrDefault();
        output.PlariumAccount = plariums.FirstOrDefault();
        output.RumbleAccount = rumbles.FirstOrDefault();
        
        Update(output);

        mongo
            .Where(query => query.ContainedIn(player => player.Id, others.Select(other => other.Id)))
            .Update(query => query
                .Set(player => player.ParentId, output.Id)
                .Set(player => player.GoogleAccount, null)
                .Set(player => player.AppleAccount, null)
                .Set(player => player.PlariumAccount, null)
                .Set(player => player.RumbleAccount, null)
                .Set(player => player.LinkCode, null)
            );

        return output;
    }

    public void SendLoginNotifications(string deviceType, params string[] emails)
    {
        foreach (string email in emails.Distinct().Where(address => !string.IsNullOrWhiteSpace(address)))
            _api
                .Request("/dmz/player/account/notification")
                .AddAuthorization(_config.AdminToken)
                .SetPayload(new RumbleJson
                {
                    { "email", email },
                    { "device", deviceType }
                })
                .OnFailure(response => Log.Error(Owner.Will, "Unable to send Rumble login account notification.", new
                {
                    Response = response
                }))
                .Post();
    }

    public Player[] Search(params string[] terms)
    {
        List<Player> output = new();

        string[] possibleIds = terms.Where(term => term.CanBeMongoId()).ToArray();

        output = mongo
            .Where(query => query.ContainedIn(player => player.Id, possibleIds))
            .ToList();
        if (output.Any())
            return output.ToArray();
        
        foreach (string term in terms)
            output.AddRange(mongo
                .Where(query => query.ContainsSubstring(player => player.Id, term))
                .Or(query => query.ContainsSubstring(player => player.Device.InstallId, term))
                .Or(query => query.ContainsSubstring(player => player.ParentId, term))
                .Or(query => query.ContainsSubstring(player => player.RumbleAccount.Email, term))
                .Or(query => query.ContainsSubstring(player => player.GoogleAccount.Id, term))
                .Or(query => query.ContainsSubstring(player => player.GoogleAccount.Email, term))
                .Or(query => query.ContainsSubstring(player => player.GoogleAccount.Name, term))
                .Or(query => query.ContainsSubstring(player => player.AppleAccount.Id, term))
                .Or(query => query.ContainsSubstring(player => player.AppleAccount.Email, term))
                .Or(query => query.ContainsSubstring(player => player.PlariumAccount.Id, term))
                .Or(query => query.ContainsSubstring(player => player.PlariumAccount.Email, term))
                .Or(query => query.ContainsSubstring(player => player.Screenname, term))
                .Limit(100)
                .ToArray()
            );
        
        foreach (Player parent in output.Where(player => string.IsNullOrWhiteSpace(player.ParentId)))
            parent.Children = output
                .Where(player => player.ParentId == parent.Id)
                .Select(player => player.Id)
                .ToList();
            
        output = output
            .Where(player => string.IsNullOrWhiteSpace(player.ParentId))
            .DistinctBy(player => player.Id)
            .ToList();

        Player.WeighSearchResults(terms, ref output);
        
        return output.ToArray();
    }

    // TODO: Remove all this code smell
    public long DeleteRumbleAccount(string email) => mongo
        .Where(query => query.EqualTo(player => player.RumbleAccount.Email, email))
        .Update(query => query.Set(player => player.RumbleAccount, null));
    public long DeleteRumbleAccountById(string accountId) => mongo
        .Where(query => query.EqualTo(player => player.Id, accountId))
        .Update(query => query.Set(player => player.RumbleAccount, null));
    public long DeleteAllRumbleAccounts() => mongo
        .All()
        .Update(query => query.Set(player => player.RumbleAccount, null));
    public long DeleteAppleAccount(string email) => mongo
        .Where(query => query.EqualTo(player => player.AppleAccount.Email, email))
        .Update(query => query.Set(player => player.AppleAccount, null));
    public long DeleteAppleAccountById(string accountId) => mongo
        .Where(query => query.EqualTo(player => player.Id, accountId))
        .Update(query => query.Set(player => player.AppleAccount, null));
    public long DeleteAllAppleAccounts() => mongo
        .All()
        .Update(query => query.Set(player => player.AppleAccount, null));
    public long DeleteGoogleAccount(string email) => mongo
        .Where(query => query.EqualTo(player => player.GoogleAccount.Email, email))
        .Update(query => query.Set(player => player.GoogleAccount, null));
    public long DeleteGoogleAccountById(string accountId) => mongo
        .Where(query => query.EqualTo(player => player.Id, accountId))
        .Update(query => query.Set(player => player.GoogleAccount, null));
    public long DeleteAllGoogleAccounts() => mongo
        .All()
        .Update(query => query.Set(player => player.GoogleAccount, null));
    public long DeletePlariumAccount(string email) => mongo
        .Where(query => query.EqualTo(player => player.PlariumAccount.Email, email))
        .Update(query => query.Set(player => player.PlariumAccount, null));
    public long DeletePlariumAccountById(string accountId) => mongo
        .Where(query => query.EqualTo(player => player.Id, accountId))
        .Update(query => query.Set(player => player.PlariumAccount, null));
    public long DeleteAllPlariumAccounts() => mongo
        .All()
        .Update(query => query.Set(player => player.PlariumAccount, null));

    protected override void OnElapsed()
    {
        long affected = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
                .LessThanOrEqualTo(player => player.RumbleAccount.CodeExpiration, Timestamp.Now)
            )
            .Update(query => query.Set(player => player.RumbleAccount, null));
        
        if (affected > 0)
            Log.Info(Owner.Will, "Deleted unconfirmed RumbleAccount data", data: new
            {
                Affected = affected
            });

        affected = mongo
            .Where(query => query.LessThanOrEqualTo(player => player.LinkExpiration, Timestamp.Now))
            .Update(query => query
                .Set(player => player.LinkCode, null)
                .Set(player => player.LinkExpiration, default)
            );
        if (affected > 0)
            Log.Info(Owner.Will, "Deleted expired account links", data: new
            {
                Affected = affected
            });

        Task.Run(() =>
        {
            long _affected = 0;
            _affected += mongo
                .Where(query => query.LessThanOrEqualTo(player => player.AppleAccount.RollingLoginTimestamp, Timestamp.OneWeekAgo))
                .Update(update => update
                    .Set(player => player.AppleAccount.WebValidationCount, 0)
                    .Set(player => player.AppleAccount.ClientValidationCount, 0)
                    .Set(player => player.AppleAccount.RollingLoginTimestamp, Timestamp.Now)
                );
            _affected += mongo
                .Where(query => query.LessThanOrEqualTo(player => player.GoogleAccount.RollingLoginTimestamp, Timestamp.OneWeekAgo))
                .Update(update => update
                    .Set(player => player.GoogleAccount.WebValidationCount, 0)
                    .Set(player => player.GoogleAccount.ClientValidationCount, 0)
                    .Set(player => player.GoogleAccount.RollingLoginTimestamp, Timestamp.Now)
                );
            _affected += mongo
                .Where(query => query.LessThanOrEqualTo(player => player.RumbleAccount.RollingLoginTimestamp, Timestamp.OneWeekAgo))
                .Update(update => update
                    .Set(player => player.RumbleAccount.WebValidationCount, 0)
                    .Set(player => player.RumbleAccount.ClientValidationCount, 0)
                    .Set(player => player.RumbleAccount.RollingLoginTimestamp, Timestamp.Now)
                );
            _affected += mongo
                .Where(query => query.LessThanOrEqualTo(player => player.PlariumAccount.RollingLoginTimestamp, Timestamp.OneWeekAgo))
                .Update(update => update
                    .Set(player => player.PlariumAccount.WebValidationCount, 0)
                    .Set(player => player.PlariumAccount.ClientValidationCount, 0)
                    .Set(player => player.PlariumAccount.RollingLoginTimestamp, Timestamp.Now)
                );
            
            Log.Info(Owner.Will, "Reset the SSO rolling login timestamps", data: new
            {
                Affected = _affected
            });
        });
    }
    
    private PlatformException DiagnoseEmailPasswordLogin(string email, string hash, string code = null)
    {
        RumbleAccount[] accounts = mongo
            .Where(query => query.EqualTo(player => player.RumbleAccount.Email, email))
            .Limit(1_000)
            .Sort(sort => sort
                .OrderByDescending(player => player.RumbleAccount.Status)
                .ThenByDescending(player => player.RumbleAccount.CodeExpiration)
            )
            .Project(player => player.RumbleAccount);

        int confirmed = accounts.Count(rumble => rumble.Status == RumbleAccount.AccountStatus.Confirmed);

        bool waitingOnConfirmation = confirmed == 0 && accounts.Any(rumble => rumble.Status == RumbleAccount.AccountStatus.NeedsConfirmation && rumble.CodeExpiration > Timestamp.Now);
        bool allExpired = confirmed == 0 && !accounts.Any(rumble => rumble.Status == RumbleAccount.AccountStatus.NeedsConfirmation && rumble.CodeExpiration > Timestamp.Now);
        bool codeInvalid = !allExpired && !string.IsNullOrWhiteSpace(code) && accounts.All(rumble => rumble.ConfirmationCode != code);

        PlatformException output = confirmed switch
        {
            0 when accounts.Length == 0 => new RumbleUnlinkedException(email),
            0 when waitingOnConfirmation => new RumbleNotConfirmedException(email),
            0 when allExpired => new ConfirmationCodeExpiredException(email),
            0 => new RumbleUnlinkedException(email),
            1 when codeInvalid => new CodeInvalidException(email),
            1 => new InvalidPasswordException(email),
            _ => new RecordsFoundException(0, 1, confirmed, "Found more than one confirmed Rumble account for an email address!")
        };

        return output;
    }

    public Player LinkPlayerAccounts(string childId, string parentId, bool force, TokenInfo token)
    {
        Player[] players = mongo
            .WithTransaction(out Transaction transaction)
            .Where(query => query.ContainedIn(player => player.Id, new[] { childId, parentId }))
            .Limit(2)
            .ToArray();
        try
        {
            if (players.Length != 2)
            {
                Abort(transaction);
                Log.Error(Owner.Will, "An administrator tried to link two accounts, but one or both accounts could not be found.", data: new
                {
                    ChildId = childId,
                    ParentId = parentId,
                    Token = token
                });
                throw new PlatformException("Could not find both accounts to link.");
            }
            
            Player child = players.First(player => player.Id == childId);
            Player parent = players.First(player => player.Id == parentId);
            
            if (!string.IsNullOrWhiteSpace(child.ParentId))
                if (!force)
                    throw new PlatformException("Account is already linked to another account.");
                else
                    Log.Warn(Owner.Will, "Account was previously linked to another account; the previous parent will be lost because the force flag is set.", data: new
                    {
                        Child = child,
                        Parent = parent,
                        Token = token
                    });
            
            if (parent.HasSso && !force)
                throw new PlatformException("Parent account has SSO; no link can be made without the force flag.");

            child = mongo
                .WithTransaction(transaction)
                .ExactId(child.Id)
                .UpdateAndReturnOne(update => update
                    .Set(player => player.AppleAccount, null)
                    .Set(player => player.GoogleAccount, null)
                    .Set(player => player.RumbleAccount, null)
                    .Set(player => player.PlariumAccount, null)
                    .Set(player => player.ParentId, parent.ParentId ?? parent.Id)
                );
            
            Commit(transaction);
            
            Log.Info(Owner.Will, $"An account was linked to a parent account with{(parent.HasSso ? "" : "out")} SSO attached", data: new
            {
                Child = child,
                Parent = parent,
                Token = token
            });
            
            return child;
        }
        catch
        {
            Abort(transaction);
            throw;
        }
    }

    public new void Update(Player model) => mongo
        .Where(query => query.EqualTo(player => player.Id, model.Id))
        .Update(query => query
            .Set(player => player.AppleAccount, model.AppleAccount)
            .Set(player => player.GoogleAccount, model.GoogleAccount)
            .Set(player => player.PlariumAccount, model.PlariumAccount)
            .Set(player => player.RumbleAccount, model.RumbleAccount)
            .Set(player => player.CreatedOn, model.CreatedOn)
            .Set(player => player.LinkCode, model.LinkCode)
            .Set(player => player.LocationData, model.LocationData)
            .Set(player => player.Device.ClientVersion, model.Device.ClientVersion)
            .Set(player => player.Device.DataVersion, model.Device.DataVersion)
            .Set(player => player.Device.Language, model.Device.Language)
            .Set(player => player.Device.OperatingSystem, model.Device.OperatingSystem)
            .Set(player => player.Device.Type, model.Device.Type) // Update everything except Device.InstallId / PK
            .Set(player => player.Screenname, model.Screenname)
            .SetToCurrentTimestamp(player => player.LastLogin)
        );

    public string GenerateToken(string accountId) => GenerateToken(Find(accountId));

    public string GenerateToken(Player player) => player.Token ??= _api
        .GenerateToken(
            accountId: player.AccountId,
            screenname: player.Screenname,
            email: player.Email, 
            discriminator: player.Discriminator ?? 0,
            audiences: TOKEN_AUDIENCE
        );

    public override long ProcessGdprRequest(TokenInfo token, string dummyText) => mongo
        .Where(query => query.EqualTo(player => player.Id, token.AccountId))
        .Or(query => query.EqualTo(player => player.ParentId, token.AccountId))
        .Update(query => query
            .Set(player => player.AppleAccount, null)
            .Set(player => player.GoogleAccount, null)
            .Set(player => player.RumbleAccount, null)
            .Set(player => player.PlariumAccount, null)
            .Set(player => player.LocationData, null)
            .Set(player => player.Screenname, dummyText)
            .Set(player => player.Device.Language, dummyText)
            .Set(player => player.Device.OperatingSystem, dummyText)
            .Set(player => player.Device.InstallId, dummyText)
            .Set(player => player.ParentId, null)
        );

    public Player ChangeScreenname(string accountId, string newName)
    {
        Player player = Find(accountId) ?? throw new PlatformException("Account not found");
        if (player.Screenname != newName)
        {
            int oldDiscriminator = player.Discriminator ?? 0;

            player = mongo
                .ExactId(player.Id)
                .UpdateAndReturnOne(query => query
                    .Set(db => db.Screenname, newName)
                    .Set(db => db.Discriminator, 0)
                );
            SyncScreenname(newName, player.Id);
            AssignDiscriminator(player, oldDiscriminator);
        }
        
        GenerateToken(player);

        return player;
    }

    public RumbleJson[] CreateLookupResults(string[] accountIds, Dictionary<string, string> avatars, Dictionary<string, int> levels)
    {
        // TODO: Fix this kluge; PLATF-6498
        return mongo
            .Where(query => query.ContainedIn(player => player.Id, accountIds))
            .ToArray()
            .Select(player => new RumbleJson
            {
                { TokenInfo.FRIENDLY_KEY_ACCOUNT_ID, player.Id },
                { Player.FRIENDLY_KEY_SCREENNAME, player.Screenname },
                { Player.FRIENDLY_KEY_DISCRIMINATOR, player.Discriminator.ToString().PadLeft(4, '0') },
                { "accountAvatar", avatars.ContainsKey(player.Id) ? avatars[player.Id] : null },
                { "accountLevel", levels.ContainsKey(player.Id) ? levels[player.Id] : null }
            })
            .ToArray();
    }
    public RumbleJson[] CreateLookupResults(string[] accountIds, Dictionary<string, LookupData> data)
    {
        // TODO: Fix this kluge; PLATF-6498
        return mongo
            .Where(query => query.ContainedIn(player => player.Id, accountIds))
            .ToArray()
            .Select(player =>
            {
                bool hasKey = data.TryGetValue(player.Id, out LookupData results);
                if (!hasKey)
                    results = new();
                return new RumbleJson
                {
                    { TokenInfo.FRIENDLY_KEY_ACCOUNT_ID, player.Id },
                    { Player.FRIENDLY_KEY_SCREENNAME, player.Screenname },
                    { Player.FRIENDLY_KEY_DISCRIMINATOR, player.Discriminator.ToString().PadLeft(4, '0') },
                    { "accountAvatar", results.Avatar },
                    { "accountLevel", results.AccountLevel },
                    { "chatTitle", results.ChatTitle }
                };
            })
            .ToArray();
    }
}