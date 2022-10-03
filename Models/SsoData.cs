using System.Text.Json.Serialization;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace PlayerService.Models;

public class SsoData : PlatformDataModel
{
	internal const string DB_KEY_ACCOUNT_ID = "ssoaid";
	internal const string DB_KEY_EMAIL = "e";
	internal const string DB_KEY_PHOTO = "pic";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	public const string FRIENDLY_KEY_EMAIL = "email";
	public const string FRIENDLY_KEY_PHOTO = "photo";
	
	[BsonElement(DB_KEY_ACCOUNT_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public string AccountId { get; init; }
	
	[BsonElement(DB_KEY_EMAIL), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_EMAIL), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Email { get; init; }
	
	public string FamilyName { get; init; }
	public string GivenName { get; init; }
	
	[BsonIgnore]
	[JsonIgnore]
	public string EncryptedToken { get; init; }
	
	[BsonElement(DB_KEY_PHOTO), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PHOTO), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Photo { get; init; }
	
	[BsonIgnore]
	[JsonIgnore]
	public string Source { get; set; }

	public static implicit operator SsoData(GoogleJsonWebSignature.Payload payload) => new SsoData()
	{
		// EncryptedToken = token,
		AccountId = payload.Subject,
		Email = payload.Email,
		Photo = payload.Picture
	};
}