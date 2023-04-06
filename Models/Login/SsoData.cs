using System;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
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
    public const string FRIENDLY_KEY_GOOGLE_TOKEN   = "googleToken";
    public const string FRIENDLY_KEY_PLARIUM_CODE  = "plariumCode";
    public const string FRIENDLY_KEY_RUMBLE_ACCOUNT = "rumble";
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_APPLE_TOKEN)]
    public string AppleToken { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_GOOGLE_TOKEN)]
    public string GoogleToken { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(FRIENDLY_KEY_PLARIUM_CODE)]
    public string PlariumCode { get; set; }
    
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
            AppleAccount = AppleAccount.ValidateToken(AppleToken);
            if (!string.IsNullOrWhiteSpace(AppleToken) && AppleAccount == null)
                throw new AppleValidationException(AppleToken);
        }
        catch (Exception e)
        {
            throw new AppleValidationException(AppleToken, e);
        }
        
        try
        {
            PlariumAccount = PlariumAccount.ValidateCode(PlariumCode);
            if (!string.IsNullOrWhiteSpace(PlariumCode) && PlariumAccount == null)
                throw new PlariumValidationException(PlariumCode);
        }
        catch (Exception e)
        {
            throw new PlariumValidationException(PlariumCode, e);
        }

        if (string.IsNullOrWhiteSpace(RumbleAccount?.Hash))
            RumbleAccount = null;
    
        return this;
    }

    public void ValidatePlayers(Player[] players)
    {
        RumbleAccount?.Validate();
        
        if (!AccountsProvided)
            return;
        if (players == null || players.Length == 0)
            throw new RecordNotFoundException("players", "No players found for SSO.");
        if (GoogleAccount != null && !players.Any(player => player.GoogleAccount != null))
            throw new GoogleUnlinkedException(GoogleAccount);
        if (AppleAccount != null && !players.Any(player => player.AppleAccount != null))
            throw new AppleUnlinkedException(AppleAccount);
        if (PlariumAccount != null && !players.Any(player => player.PlariumAccount != null))
            throw new PlariumUnlinkedException(PlariumAccount);
        if (RumbleAccount != null && !players.Any(player => player.RumbleAccount != null))
            throw new RumbleUnlinkedException(RumbleAccount);
    }
}
