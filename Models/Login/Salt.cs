using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class Salt : PlatformCollectionDocument
{
    private const string DB_KEY_USERNAME = "user";
    private const string DB_KEY_SALT = "salt";

    public const string FRIENDLY_KEY_SALT = "salt";
    
    [BsonElement(DB_KEY_USERNAME)]
    [JsonIgnore]
    [SimpleIndex(Unique = true)]
    public string Username { get; set; }
    
    [BsonElement(DB_KEY_SALT)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_SALT)]
    public string Value { get; set; }

    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Username))
            errors.Add("Username cannot be empty or null.");
        if (string.IsNullOrWhiteSpace(Value))
            errors.Add("Salt cannot be empty or null.");
    }
}