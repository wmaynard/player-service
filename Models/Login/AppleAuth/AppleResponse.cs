using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login.AppleAuth;

public class AppleResponse : PlatformDataModel
{
	public const string DB_KEY_KEYS = "keys";

	private const string FRIENDLY_KEY_KEYS = "keys";
	
	[BsonElement(DB_KEY_KEYS)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_KEYS)]
	public List<AppleAuthKey> Keys { get; set; }
}