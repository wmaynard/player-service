using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace PlayerService.Models.Login.AppleAuth;

public class AppleAuthKey : PlatformDataModel
{
	private const string DB_KEY_KTY = "kty";
	private const string DB_KEY_KID = "kid";
	private const string DB_KEY_USE = "use";
	private const string DB_KEY_ALG = "alg";
	private const string DB_KEY_N   = "n";
	private const string DB_KEY_E   = "e";
	
	public const string FRIENDLY_KEY_KTY = "kty";
	public const string FRIENDLY_KEY_KID = "kid";
	public const string FRIENDLY_KEY_USE = "use";
	public const string FRIENDLY_KEY_ALG = "alg";
	public const string FRIENDLY_KEY_N   = "n";
	public const string FRIENDLY_KEY_E   = "e";
	
	[BsonElement(DB_KEY_KTY)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_KTY)]
	public string Kty { get; set; }
	
	[BsonElement(DB_KEY_KID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_KID)]
	public string Kid { get; set; }
	
	[BsonElement(DB_KEY_USE)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_USE)]
	public string Use { get; set; }
	
	[BsonElement(DB_KEY_ALG)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ALG)]
	public string Alg { get; set; }
	
	[BsonElement(DB_KEY_N)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_N)]
	public string N { get; set; }
	
	[BsonElement(DB_KEY_E)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_E)]
	public string E { get; set; }
}