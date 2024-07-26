using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.JsonTools;

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
}