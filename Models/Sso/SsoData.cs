using System;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Data;

namespace PlayerService.Models.Sso;

public class SsoData : PlatformDataModel
{
    [BsonIgnore]
    [JsonInclude, JsonPropertyName("appleToken")]
    public string AppleToken { get; set; }
    
    [BsonIgnore]
    [JsonInclude, JsonPropertyName("googleToken")]
    public string GoogleToken { get; set; }

    [BsonIgnore]
    [JsonInclude, JsonPropertyName("rumble")]
    public RumbleAccount RumbleAccount { get; set; }
    
    public GoogleAccount GoogleAccount { get; set; }
    public AppleAccount AppleAccount { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public bool AccountsProvided => RumbleAccount != null || GoogleAccount != null || AppleAccount != null;

    public SsoData ValidateTokens()
    {
        try
        {
            GoogleAccount = GoogleAccount.ValidateToken(GoogleToken);
            if (GoogleToken != null && GoogleAccount == null)
                throw new PlatformException("Unable to validate Google token.");
        }
        catch (Exception e)
        {
            throw new SsoInvalidException(GoogleToken, "google", inner: e);
        }
    
        try
        {
            AppleAccount = AppleAccount.ValidateToken(AppleToken);
            if (AppleToken != null && AppleAccount == null)
                throw new PlatformException("Unable to validate Apple token.");
        }
        catch (Exception e)
        {
            throw new SsoInvalidException(AppleToken, "Apple", inner: e);
        }
    
        return this;
    }

    public void ValidatePlayers(Player[] players)
    {
        if (!AccountsProvided)
            return;
        if (players == null || players.Length == 0)
            throw new PlatformException("No players found for SSO.");
        if (GoogleAccount != null && !players.Any(player => player.GoogleAccount != null))
            throw new PlatformException("Missing Google account.");
        if (AppleAccount != null && !players.Any(player => player.AppleAccount != null))
            throw new PlatformException("Missing Apple account.");
        if (RumbleAccount != null && !players.Any(player => player.RumbleAccount != null))
            throw new PlatformException("Missing Rumble account.");
    }
    
}
