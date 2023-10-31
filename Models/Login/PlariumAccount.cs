using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

[BsonIgnoreExtraElements]
public class PlariumAccount : PlatformDataModel, ISsoAccount
{
	private const string DB_KEY_PLARIUM_ID = "plid";
	private const string DB_KEY_EMAIL     = "email";

	public const string FRIENDLY_KEY_PLARIUM_ID = "plariumId";
	public const string FRIENDLY_KEY_EMAIL      = "email";
	
	[BsonElement(DB_KEY_PLARIUM_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PLARIUM_ID)]
	[SimpleIndex]
	public string Id { get; set; }
	
	[BsonElement(DB_KEY_EMAIL)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_EMAIL)]
	[CompoundIndex(group: Player.INDEX_KEY_SEARCH, priority: 8)]
	public string Email { get; set; }
	
	// TODO: This is a backcompat field added on 6/27/23.  Remove it when no longer necessary.
	[BsonElement("login")]
	[JsonIgnore]
	public string Login { get; set; }

	public PlariumAccount(string plariumId, string email)
	{
		Id = plariumId;
		Email = email;
	}

	public static PlariumAccount ValidateCode(string code)
	{
		string token = PlariumService.Instance.VerifyCode(code);

		return PlariumService.Instance.VerifyToken(token);
	}

	public static PlariumAccount ValidateToken(string token) => PlariumService.Instance.VerifyToken(token);

	private static PlariumAccount FromRequest(string token, string code)
	{
		if (!string.IsNullOrWhiteSpace(token))
			return ValidateToken(token);
		if (!string.IsNullOrWhiteSpace(code))
			return ValidateCode(code);
		throw new PlatformException(message: $"Request did not contain one of two required fields: {SsoData.FRIENDLY_KEY_PLARIUM_CODE} or {SsoData.FRIENDLY_KEY_PLARIUM_TOKEN}.");
	}

	public static PlariumAccount FromRequest(RumbleJson body) => FromRequest(
		token: body?.Optional<string>(SsoData.FRIENDLY_KEY_PLARIUM_TOKEN),
		code: body?.Optional<string>(SsoData.FRIENDLY_KEY_PLARIUM_CODE)
	);
}