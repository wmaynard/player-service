using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models;
[BsonIgnoreExtraElements]
public class Component : PlatformCollectionDocument
{
	public const string AB_TEST = "abTest";
	public const string ACCOUNT = "account";
	public const string EQUIPMENT = "equipment";
	public const string HERO = "hero";
	public const string MULTIPLAYER = "multiplayer";
	public const string QUEST = "quest";
	public const string STORE = "store";
	public const string SUMMARY = "summary";
	public const string TUTORIAL = "tutorial";
	public const string WALLET = "wallet";
	public const string WORLD = "world";
	
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_DATA = "data";
	internal const string DB_KEY_VERSION = "v";
	internal const string DB_KEY_AUDIT_LOGS = "log";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
	public const string FRIENDLY_KEY_DATA = "data";
	public const string FRIENDLY_KEY_NAME = "name";
	public const string FRIENDLY_KEY_VERSION = "version";
	public const string FRIENDLY_KEY_AUDIT_LOGS = "auditLogs";

	[BsonElement(DB_KEY_ACCOUNT_ID), BsonRepresentation(BsonType.ObjectId)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
	public string AccountId { get; private set; }
	
	[BsonElement(DB_KEY_DATA)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA)]
	public GenericData Data { get; set; }
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Name { get; set; }
	
	[BsonElement(DB_KEY_VERSION)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int Version { get; set; }

	[BsonElement(DB_KEY_AUDIT_LOGS)] [JsonInclude, JsonPropertyName(FRIENDLY_KEY_AUDIT_LOGS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<AuditLog> AuditLogs { get; set; }

	public Component(string accountId, string name = null, GenericData data = null)
	{
		AccountId = accountId;
		Name = name;
		Version = 0;
		Data = data ?? new GenericData();
		AuditLogs = new List<AuditLog>();
	}
}


