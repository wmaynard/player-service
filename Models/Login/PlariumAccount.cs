using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

[BsonIgnoreExtraElements]
public class PlariumAccount : PlatformDataModel
{
	private const string DB_KEY_PLARIUM_ID = "plid";
	private const string DB_KEY_LOGIN     = "login";

	public const string FRIENDLY_KEY_PLARIUM_ID = "plariumId";
	public const string FRIENDLY_KEY_LOGIN      = "login";
	
	[BsonElement(DB_KEY_PLARIUM_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PLARIUM_ID)]
	public string Id { get; set; }
	
	[BsonElement(DB_KEY_LOGIN)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_LOGIN)]
	[CompoundIndex(group: Player.INDEX_KEY_SEARCH, priority: 8)]
	public string Login { get; set; }

	public PlariumAccount(string plariumId, string login)
	{
		Id = plariumId;
		Login = login;
	}

	public static PlariumAccount ValidateCode(string code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return null;
		}

		string token = PlariumService.Instance.VerifyCode(code);

		return PlariumService.Instance.VerifyToken(token);
	}

	public static PlariumAccount ValidateToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}

		return PlariumService.Instance.VerifyToken(token);
	}
}