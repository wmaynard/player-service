using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;

namespace PlayerService.Models;

public class AuditLog : PlatformDataModel
{
	internal const string DB_KEY_CURRENT_VERSION = "cv";
	internal const string DB_KEY_NEXT_VERSION = "nv";
	internal const string DB_KEY_TIMESTAMP = "ts";
	internal const string DB_KEY_IS_INVALID = "error";
	internal const string DB_KEY_NO_VERSION_PROVIDED = "warn";

	public const string FRIENDLY_KEY_CURRENT_VERSION = "currentVersion";
	public const string FRIENDLY_KEY_NEXT_VERSION = "nextVersion";
	public const string FRIENDLY_KEY_TIMESTAMP = "timestamp";
	public const string FRIENDLY_KEY_IS_INVALID = "isValid";
	public const string FRIENDLY_KEY_NO_VERSION_PROVIDED = "versionNotProvided";
	
	[BsonElement(DB_KEY_CURRENT_VERSION), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CURRENT_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int CurrentVersion { get; set; }
	
	[BsonElement(DB_KEY_NEXT_VERSION), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NEXT_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int NextVersion { get; set; }
	
	[BsonElement(DB_KEY_TIMESTAMP), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIMESTAMP), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long Timestamp { get; set; }
	
	[BsonElement(DB_KEY_IS_INVALID), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_IS_INVALID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool IsInvalid => NextVersion != default && NextVersion != CurrentVersion + 1;

	[BsonElement(DB_KEY_NO_VERSION_PROVIDED), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NO_VERSION_PROVIDED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool NoVersionProvided => NextVersion == default;

	public AuditLog()
	{
		Timestamp = UnixTime;
		CurrentVersion = 0;
		NextVersion = 0;
	}
	public AuditLog(int currentVersion, int nextVersion) : this()
	{
		CurrentVersion = currentVersion;
		NextVersion = nextVersion;
	}
}