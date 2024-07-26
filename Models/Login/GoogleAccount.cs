using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace PlayerService.Models.Login;

public class GoogleAccount : PlatformDataModel, ISsoAccount
{
    private const string DB_KEY_EMAIL = "email";
    private const string DB_KEY_EMAIL_VERIFIED = "verified";
    private const string DB_KEY_HOSTED_DOMAIN = "host";
    private const string DB_KEY_ID = "id";
    private const string DB_KEY_NAME = "name";
    private const string DB_KEY_PICTURE = "pic";

    public const string FRIENDLY_KEY_EMAIL = "email";
    public const string FRIENDLY_KEY_EMAIL_VERIFIED = "verified";
    public const string FRIENDLY_KEY_HOSTED_DOMAIN = "hostedDomain";
    public const string FRIENDLY_KEY_ID = "id";
    public const string FRIENDLY_KEY_NAME = "name";
    public const string FRIENDLY_KEY_PICTURE = "picture";
    
    [BsonElement(DB_KEY_EMAIL)]
    [JsonPropertyName(FRIENDLY_KEY_EMAIL)]
    public string Email { get; set; }
    
    [BsonElement(DB_KEY_EMAIL_VERIFIED)]
    [JsonPropertyName(FRIENDLY_KEY_EMAIL_VERIFIED)]
    public bool EmailVerified { get; set; }
    
    [BsonElement(DB_KEY_HOSTED_DOMAIN)]
    [JsonPropertyName(FRIENDLY_KEY_HOSTED_DOMAIN)]
    public string HostedDomain { get; set; }
    
    [BsonElement(DB_KEY_ID)]
    [JsonPropertyName(FRIENDLY_KEY_ID)]
    public string Id { get; set; }
    
    [BsonElement(DB_KEY_NAME)]
    [JsonPropertyName(FRIENDLY_KEY_NAME)]
    public string Name { get; set; }
    
    [BsonElement(DB_KEY_PICTURE)]
    [JsonPropertyName(FRIENDLY_KEY_PICTURE)]
    public string Picture { get; set; }
    
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
