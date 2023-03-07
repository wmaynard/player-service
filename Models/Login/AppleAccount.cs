using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class AppleAccount : PlatformDataModel
{
    private const string DB_KEY_ISS              = "iss";
    private const string DB_KEY_AUD              = "aud";
    private const string DB_KEY_SUB              = "sub";
    private const string DB_KEY_EMAIL            = "email";
    private const string DB_KEY_EMAIL_VERIFIED   = "verified";
    private const string DB_KEY_IS_PRIVATE_EMAIL = "isPvt";
    private const string DB_KEY_AUTH_TIME        = "authTime";
    
    public const string FRIENDLY_KEY_ISS              = "iss";
    public const string FRIENDLY_KEY_AUD              = "aud";
    public const string FRIENDLY_KEY_SUB              = "sub";
    public const string FRIENDLY_KEY_EMAIL            = "email";
    public const string FRIENDLY_KEY_EMAIL_VERIFIED   = "verified";
    public const string FRIENDLY_KEY_IS_PRIVATE_EMAIL = "isPrivateEmail";
    public const string FRIENDLY_KEY_AUTH_TIME        = "authTime";
    
    [BsonElement(DB_KEY_ISS)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISS)]
    public string Iss { get; set; }
    
    [BsonElement(DB_KEY_AUD)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUD)]
    public string Aud { get; set; }
    
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

    public AppleAccount(string iss,           string aud,            string id, string email, 
                        string emailVerified, string isPrivateEmail, long authTime)
    {
        Iss = iss;
        Aud = aud;
        Id = id;
        Email = email;
        EmailVerified = emailVerified;
        IsPrivateEmail = isPrivateEmail;
        AuthTime = authTime;
    }
    
    public static AppleAccount ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return AppleSignatureVerifyService.Instance.Verify(token);
    }
}