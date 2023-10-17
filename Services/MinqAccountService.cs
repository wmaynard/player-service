using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Models.Login;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Services;

public class MinqAccountService : MinqTimerService<Player>
{
    private readonly DynamicConfig _config;
    private readonly ApiService _api;
    
    public MinqAccountService(ApiService api, DynamicConfig config) : base("players", interval: TimeSpan.FromDays(1).TotalMilliseconds)
    {
        _api = api;
        _config = config;
    }
    
    public Player Find(string accountId) => mongo.FirstOrDefault(query => query.EqualTo(player => player.Id, accountId));

    public bool InstallIdExists(DeviceInfo device) => !string.IsNullOrWhiteSpace(device?.InstallId)
        && mongo.Count(query => query.EqualTo(player => player.Device.InstallId, device.InstallId)) > 0;

    public int SyncScreenname(string screenname, string accountId, bool fromAdmin = false)
    {
        Require<DiscriminatorService>().Assign(accountId, screenname);
        Require<AccountService>().SetScreenname(accountId, screenname, fromAdmin);
        return (int)mongo
            .Where(query => query.EqualTo(player => player.Id, accountId))
            .Or(query => query.EqualTo(player => player.ParentId, accountId))
            .Update(query => query.Set(player => player.Screenname, screenname));
    }

    public Player FromDevice(DeviceInfo device, bool isUpsert = false)
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
            .Upsert(query => query
                .Set(player => player.Device.ClientVersion, device.ClientVersion)
                .Set(player => player.Device.DataVersion, device.DataVersion)
                .Set(player => player.Device.Language, device.Language)
                .Set(player => player.Device.OperatingSystem, device.OperatingSystem)
                .Set(player => player.Device.Type, device.Type)
                .Set(player => player.LastLogin, Timestamp.UnixTime)
                .Set(player => player.Device.ConfirmedPrivateKey, device.PrivateKey)
                .SetOnInsert(player => player.Screenname, Require<NameGeneratorService>().Next)
            );
        
        stored?.Device?.CalculatePrivateKey();

        // Look for a parent account, if necessary.
        if (!string.IsNullOrWhiteSpace(stored?.ParentId))
            stored.Parent = mongo.FirstOrDefault(query => query.EqualTo(player => player.Id, stored.ParentId));
        
        return stored?.Parent ?? stored;
    }

    public Player[] FromSso(SsoData sso, string ipAddress)
    {
        if (!sso.HasAtLeastOneAccount())
            return Array.Empty<Player>();
        
        RequestChain<Player> request = mongo.CreateRequestChain();

        if (sso.GoogleAccount != null)
            request.Or(query => query.EqualTo(player => player.GoogleAccount.Id, sso.GoogleAccount.Id));
        if (sso.AppleAccount != null)
            request.Or(query => query.EqualTo(player => player.AppleAccount.Id, sso.AppleAccount.Id));
        if (sso.PlariumAccount != null)
            request.Or(query => query.EqualTo(player => player.PlariumAccount.Id, sso.PlariumAccount.Id));
        if (sso.RumbleAccount != null && Require<LockoutService>().EnsureNotLockedOut(sso.RumbleAccount.Username, ipAddress))
            request.Or(query => query
                .EqualTo(player => player.RumbleAccount.Username, sso.RumbleAccount.Username)
                .EqualTo(player => player.RumbleAccount.Hash, sso.RumbleAccount.Hash)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            );

        Player[] output = request.ToArray();
        
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

    private Player EnsureOneResult(Player[] results) => results?.Length == 1
        ? results.First()
        : throw new RecordsFoundException(1, results?.Length ?? 0);

    public Player FromApple(AppleAccount apple) => EnsureOneResult(mongo
        .Where(query => query.EqualTo(player => player.AppleAccount.Id, apple.Id))
        .ToArray()
    );
    public Player FromGoogle(GoogleAccount google) => EnsureOneResult(mongo
        .Where(query => query.EqualTo(player => player.GoogleAccount.Id, google.Id))
        .ToArray()
    );
    public Player FromPlarium(PlariumAccount plarium) => EnsureOneResult(mongo
        .Where(query => query.EqualTo(player => player.PlariumAccount.Id, plarium.Id))
        .ToArray()
    );

    public Player CompleteLink(RumbleAccount rumble)
    {
        long usernameCount = mongo
            .Where(query => query
                .EqualTo(player => player.Email, rumble.Email)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .Count();
        
        if (usernameCount > 0)
            throw new AccountOwnershipException("Rumble", "The username or email is already in use.");

        Player[] results = mongo
            .Where(query => query
                .EqualTo(player => player.Email, rumble.Email)
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

    public void EnforceNoRumbleAccountExists(RumbleAccount rumble, string accountId)
    {
        Player[] results = mongo
            .Where(query => query
                .EqualTo(player => player.Email, rumble.Email)
                .GreaterThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
            )
            .ToArray();
        
        switch (results.Length)
        {
            case 0:
                return;
            case 1:
                throw results.Any(result => result.Id == accountId)
                    ? new AlreadyLinkedAccountException("Rumble")
                    : new AccountOwnershipException("Rumble", accountId, results.First().Id);
            case > 1:
                throw new RecordsFoundException(1, results.Length);
        }
    }

    public Player UpdateHash(string username, string oldHash, string newHash, string callingAccountId) => mongo
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
                            Response = response
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

        Update(player);
        GenerateToken(player);
        return player;
    }

    // TODO: Remove these in favor of just using AttachSsoAccount()
    public Player AttachRumble(Player player, RumbleAccount rumble) => AttachSsoAccount(player, rumble);
    public Player AttachGoogle(Player player, GoogleAccount google) => AttachSsoAccount(player, google);
    public Player AttachApple(Player player, AppleAccount apple) => AttachSsoAccount(player, apple);
    public Player AttachPlarium(Player player, PlariumAccount plarium) => AttachSsoAccount(player, plarium);

    public long DeleteUnconfirmedAccounts() => mongo
        .Where(query => query
            .EqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
            .LessThanOrEqualTo(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
        )
        .Update(query => query.Set(player => player.RumbleAccount, null));

    public Player UseConfirmationCode(string id, string code)
    {
        Player output = mongo
            .Where(query => query
                .EqualTo(player => player.Id, id)
                .EqualTo(player => player.RumbleAccount.ConfirmationCode, code)
                .LessThanOrEqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
                .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
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
                .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
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
            .GreaterThan(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
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
        
        if (output.LinkExpiration <= Timestamp.UnixTime)
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
    
    public void SendLoginNotification(Player player, string email) => _api
        .Request("/dmz/player/account/notification")
        .AddAuthorization(_config.AdminToken)
        .SetPayload(new RumbleJson
        {
            { "email", email },
            { "device", player.Device.Type }
        })
        .OnFailure(response => Log.Error(Owner.Will, "Unable to send Rumble login account notification.", new
        {
            Response = response
        }))
        .Post();

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
                .Or(query => query.ContainsSubstring(player => player.GoogleAccount.Email, term))
                .Or(query => query.ContainsSubstring(player => player.GoogleAccount.Name, term))
                .Or(query => query.ContainsSubstring(player => player.AppleAccount.Email, term))
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
            .DistinctBy(player => player.AccountId)
            .ToList();

        Player.WeighSearchResults(terms, ref output);
        
        return output.ToArray();
    }

    protected override void OnElapsed()
    {
        long affected = mongo
            .Where(query => query
                .EqualTo(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
                .LessThanOrEqualTo(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
            )
            .Update(query => query.Set(player => player.RumbleAccount, null));
        
        if (affected > 0)
            Log.Info(Owner.Will, "Deleted unconfirmed RumbleAccount data", data: new
            {
                Affected = affected
            });

        affected = mongo
            .Where(query => query.LessThanOrEqualTo(player => player.LinkExpiration, Timestamp.UnixTime))
            .Update(query => query
                .Set(player => player.LinkCode, null)
                .Set(player => player.LinkExpiration, default)
            );
        if (affected > 0)
            Log.Info(Owner.Will, "Deleted expired account links", data: new
            {
                Affected = affected
            });
    }
}

public interface ISsoAccount { }



































