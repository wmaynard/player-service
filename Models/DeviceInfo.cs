using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Models.Sso;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Data;

namespace PlayerService.Models;

public class DeviceInfo : PlatformDataModel
{
    [BsonElement("cv")]
    [JsonInclude, JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; }
    
    [BsonElement("dv")]
    [JsonInclude, JsonPropertyName("dataVersion")]
    public string DataVersion { get; set; }
    
    [BsonElement("lsi")]
    [JsonInclude, JsonPropertyName("installId")]
    public string InstallId { get; set; }
    
    [BsonElement("lang")]
    [JsonInclude, JsonPropertyName("language")]
    public string Language { get; set; }
    
    [BsonElement("os")]
    [JsonInclude, JsonPropertyName("osVersion")]
    public string OperatingSystem { get; set; }
    
    [BsonElement("t")]
    [JsonInclude, JsonPropertyName("type")]
    public string Type { get; set; }
}

public class SsoInput : PlatformDataModel
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

    public SsoInput ValidateTokens()
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

public class GoogleAccount : PlatformDataModel
{
    public string Id { get; set; }
    public string Email { get; set; }
    public bool EmailVerified { get; set; }
    public string HostedDomain { get; set; }
    public string Name { get; set; }
    public string Picture { get; set; }

    private GoogleAccount(GoogleJsonWebSignature.Payload payload)
    {
        Id = payload?.Subject;
        Email = payload?.Email;
        EmailVerified = payload?.EmailVerified ?? false;
        HostedDomain = payload?.HostedDomain;
        Name = payload?.Name;
        Picture = payload?.Picture;

        if (!string.IsNullOrWhiteSpace(Name))
            return;
        
        string name = $"{payload?.GivenName} {payload?.FamilyName}";
        if (!string.IsNullOrWhiteSpace(name))
            Name = name;
    }
    
    public static GoogleAccount ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        
        Task<GoogleJsonWebSignature.Payload> task = GoogleJsonWebSignature.ValidateAsync(token);
        task.Wait();

        return new GoogleAccount(task.Result);
    }
}

public class AppleAccount : PlatformDataModel
{
    public string Id { get; set; }
    public static AppleAccount ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return null;
    }
}