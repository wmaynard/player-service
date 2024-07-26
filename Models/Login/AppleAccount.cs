using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace PlayerService.Models.Login;

public class AppleAccount : PlatformDataModel, ISsoAccount
{
    private const string DB_KEY_ISSUER           = "iss";
    private const string DB_KEY_AUD              = "aud";
    private const string DB_KEY_SUB              = "sub";
    private const string DB_KEY_EMAIL            = "email";
    private const string DB_KEY_EMAIL_VERIFIED   = "verified";
    private const string DB_KEY_IS_PRIVATE_EMAIL = "isPvt";
    private const string DB_KEY_AUTH_TIME        = "authTime";
    
    public const string FRIENDLY_KEY_ISSUER           = "iss";
    public const string FRIENDLY_KEY_AUD              = "aud";
    public const string FRIENDLY_KEY_SUB              = "sub";
    public const string FRIENDLY_KEY_EMAIL            = "email";
    public const string FRIENDLY_KEY_EMAIL_VERIFIED   = "verified";
    public const string FRIENDLY_KEY_IS_PRIVATE_EMAIL = "isPrivateEmail";
    public const string FRIENDLY_KEY_AUTH_TIME        = "authTime";
    
    [BsonElement(DB_KEY_ISSUER)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISSUER)]
    public string Issuer { get; set; }
    
    [BsonElement(DB_KEY_AUD)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUD)]
    public string Audience { get; set; }
    
    [BsonElement(DB_KEY_SUB)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SUB)]
    public string Id { get; set; }

    [BsonElement(DB_KEY_EMAIL)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_EMAIL)]
    public string Email { get; set; }
    
    [BsonElement(DB_KEY_EMAIL_VERIFIED)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_EMAIL_VERIFIED)]
    public string EmailVerified { get; set; }
    
    [BsonElement(DB_KEY_IS_PRIVATE_EMAIL)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_IS_PRIVATE_EMAIL)]
    public string IsPrivateEmail { get; set; }
    
    [BsonElement(DB_KEY_AUTH_TIME)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUTH_TIME)]
    public long AuthTime { get; set; }
    
    [BsonElement(PlatformCollectionDocument.DB_KEY_CREATED_ON)]
    [JsonIgnore]
    public long AddedOn { get; set; }

    [BsonElement("period")]
    [JsonIgnore]
    public long RollingLoginTimestamp { get; set; }

    [BsonElement("webLogins")]
    [JsonIgnore]
    public long WebValidationCount { get; set; }
	
    [BsonElement("clientLogins")]
    [JsonIgnore]
    public long ClientValidationCount { get; set; }
	
    [BsonElement("logins")]
    [JsonIgnore]
    public long LifetimeValidationCount { get; set; }
	
    [BsonElement(TokenInfo.DB_KEY_IP_ADDRESS)]
    [JsonIgnore]
    public string IpAddress { get; set; }

    public AppleAccount(JwtSecurityToken token)
    {
        // TODO: Use private consts for these apple token payload values
        Issuer = token?.Claims.First(claim => claim.Type == "iss").Value;
        Audience = token?.Claims.First(claim => claim.Type == "aud").Value;
        Id = token?.Claims.First(claim => claim.Type == "sub").Value;
        Email = token?.Claims.FirstOrDefault(claim => claim.Type == "email")?.Value;
        EmailVerified = token?.Claims.FirstOrDefault(claim => claim.Type == "email_verified")?.Value;
        IsPrivateEmail = token?.Claims.FirstOrDefault(claim => claim.Type == "is_private_email")?.Value;

        if (long.TryParse(token?.Claims.First(claim => claim.Type == "auth_time").Value, out long authTime))
            AuthTime = authTime;
    }
    
    public static AppleAccount ValidateToken(string token, string nonce) => string.IsNullOrWhiteSpace(token)
        ? null
        : AppleSignatureVerifyService.Instance.Verify(token, nonce);
}