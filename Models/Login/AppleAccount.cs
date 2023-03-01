using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using PlayerService.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class AppleAccount : PlatformDataModel
{
    private const string DB_KEY_ISS              = "iss";
    private const string DB_KEY_AUD              = "aud";
    private const string DB_KEY_EXPIRATION       = "exp";
    private const string DB_KEY_ISSUED_AT        = "iat";
    private const string DB_KEY_SUB              = "sub";
    private const string DB_KEY_NONCE            = "nonce";
    private const string DB_KEY_C_HASH           = "cHash";
    private const string DB_KEY_EMAIL            = "email";
    private const string DB_KEY_EMAIL_VERIFIED   = "verified";
    private const string DB_KEY_IS_PRIVATE_EMAIL = "isPvt";
    private const string DB_KEY_AUTH_TIME        = "authTime";
    private const string DB_KEY_NONCE_SUPPORTED  = "nonceSup";
    
    public const string FRIENDLY_KEY_ISS              = "iss";
    public const string FRIENDLY_KEY_AUD              = "aud";
    public const string FRIENDLY_KEY_EXPIRATION       = "expiration";
    public const string FRIENDLY_KEY_ISSUED_AT        = "issuedAt";
    public const string FRIENDLY_KEY_SUB              = "sub";
    public const string FRIENDLY_KEY_NONCE            = "nonce";
    public const string FRIENDLY_KEY_C_HASH           = "cHash";
    public const string FRIENDLY_KEY_EMAIL            = "email";
    public const string FRIENDLY_KEY_EMAIL_VERIFIED   = "verified";
    public const string FRIENDLY_KEY_IS_PRIVATE_EMAIL = "isPrivateEmail";
    public const string FRIENDLY_KEY_AUTH_TIME        = "authTime";
    public const string FRIENDLY_KEY_NONCE_SUPPORTED  = "nonceSupported";
    
    [BsonElement(DB_KEY_ISS)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISS)]
    public string Iss { get; set; }
    
    [BsonElement(DB_KEY_AUD)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUD)]
    public string Aud { get; set; }
    
    [BsonElement(DB_KEY_EXPIRATION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_EXPIRATION)]
    public long Expiration { get; set; }
    
    [BsonElement(DB_KEY_ISSUED_AT)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_ISSUED_AT)]
    public long IssuedAt { get; set; }
    
    [BsonElement(DB_KEY_SUB)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SUB)]
    public string Id { get; set; }
    
    [BsonElement(DB_KEY_NONCE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_NONCE)]
    public string Nonce { get; set; }
    
    [BsonElement(DB_KEY_C_HASH)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_C_HASH)]
    public string CHash { get; set; }
    
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
    
    [BsonElement(DB_KEY_NONCE_SUPPORTED)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_NONCE_SUPPORTED)]
    public bool NonceSupported { get; set; }

    public AppleAccount(string iss,      string aud,   long   exp,           long   iat, string id, string nonce,
                        string cHash,    string email, string emailVerified, string isPrivateEmail,
                        long   authTime, bool   nonceSupported)
    {
        Iss = iss;
        Aud = aud;
        Expiration = exp;
        IssuedAt = iat;
        Id = id;
        Nonce = nonce;
        CHash = cHash;
        Email = email;
        EmailVerified = emailVerified;
        IsPrivateEmail = isPrivateEmail;
        AuthTime = authTime;
        NonceSupported = nonceSupported;
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