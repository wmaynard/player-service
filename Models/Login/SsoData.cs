using System;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Services;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

/// <summary>
/// This class is just used as a transport layer from request -> databases.  It is not intended for storage.
/// </summary>
public class SsoData : PlatformDataModel
{
    public const string FRIENDLY_KEY_APPLE_TOKEN    = "appleToken";
    public const string FRIENDLY_KEY_APPLE_NONCE    = "appleNonce";
    public const string FRIENDLY_KEY_GOOGLE_TOKEN   = "googleToken";
    public const string FRIENDLY_KEY_PLARIUM_CODE   = "plariumCode";
    public const string FRIENDLY_KEY_PLARIUM_TOKEN  = "plariumToken";
    public const string FRIENDLY_KEY_RUMBLE_ACCOUNT = "rumble";
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_APPLE_TOKEN)]
    public string AppleToken { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_APPLE_NONCE)]
    public string AppleNonce { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_GOOGLE_TOKEN)]
    public string GoogleToken { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_PLARIUM_CODE)]
    public string PlariumCode { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_PLARIUM_TOKEN)]
    public string PlariumToken { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_RUMBLE_ACCOUNT)]
    public RumbleAccount RumbleAccount { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public GoogleAccount GoogleAccount { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public AppleAccount AppleAccount { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public PlariumAccount PlariumAccount { get; set; }
    
    /// <summary>
    /// This needs to be updated whenever we add new SSO providers.  When we see a non-Rumble SSO account, we don't
    /// need to or want to send out 2FA emails.  TODO: add an SsoAccount interface, grab these two bools with reflection
    /// </summary>
    [BsonIgnore]
    [JsonIgnore]
    public bool SkipTwoFactor => GoogleAccount != null || AppleAccount != null || PlariumAccount != null;
    
    [BsonIgnore]
    [JsonIgnore]
    public bool AccountsProvided => RumbleAccount != null || GoogleAccount != null || AppleAccount != null || PlariumAccount != null;

    public SsoData ValidateTokens()
    {
        try
        {
            GoogleAccount = GoogleAccount.ValidateToken(GoogleToken);
            if (!string.IsNullOrWhiteSpace(GoogleToken) && GoogleAccount == null)
                throw new GoogleValidationException(GoogleToken);
        }
        catch (Exception e)
        {
            throw new GoogleValidationException(GoogleToken, e);
        }
    
        try
        {
            AppleAccount = AppleAccount.ValidateToken(AppleToken, AppleNonce);
            if (!string.IsNullOrWhiteSpace(AppleToken) && AppleAccount == null)
                throw new AppleValidationException(AppleToken);
        }
        catch (Exception e)
        {
            throw new AppleValidationException(AppleToken, e);
        }

        try
        {
            PlariumAccount = PlariumService.Instance.Verify(PlariumCode, PlariumToken);

            if (PlariumAccount == null)
                throw new PlariumValidationException($"{PlariumCode}{PlariumToken}");
        }
        catch (Exception e)
        {
            throw new PlariumValidationException(PlariumCode, e);
        }

        if (string.IsNullOrWhiteSpace(RumbleAccount?.Hash))
            RumbleAccount = null;

        if (RumbleAccount != null)
        {
            RumbleAccount.Email = RumbleAccount.Email.ToLower();
            RumbleAccount.Username = RumbleAccount.Username.ToLower();
        }
    
        return this;
    }

    public void ValidatePlayers(Player[] players)
    {
        RumbleAccount?.Validate();
        
        if (!AccountsProvided)
            return;
        if (players == null || players.Length == 0)
            throw new RecordNotFoundException("players", "No players found for SSO.");
        if (GoogleAccount != null && players.All(player => player.GoogleAccount == null))
            throw new GoogleUnlinkedException(GoogleAccount);
        if (AppleAccount != null && players.All(player => player.AppleAccount == null))
            throw new AppleUnlinkedException(AppleAccount);
        if (PlariumAccount != null && players.All(player => player.PlariumAccount == null))
            throw new PlariumUnlinkedException(PlariumAccount);
        if (RumbleAccount != null && players.All(player => player.RumbleAccount == null))
            throw new RumbleUnlinkedException(RumbleAccount);
    }

    public bool HasAtLeastOneAccount() => GoogleAccount != null
        || AppleAccount != null
        || PlariumAccount != null
        || RumbleAccount != null;
}
