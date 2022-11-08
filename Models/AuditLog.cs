using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Data;

namespace PlayerService.Models;

public class AuditLog : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_COMPONENT_NAME = "name";
	internal const string DB_KEY_ENTRIES = "data";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	public const string FRIENDLY_KEY_COMPONENT_NAME = "component";
	public const string FRIENDLY_KEY_ENTRIES = "entries";
	
	[SimpleIndex]
	[BsonElement(DB_KEY_ACCOUNT_ID), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AccountId { get; set; }
	
	[SimpleIndex]
	[BsonElement(DB_KEY_COMPONENT_NAME), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_COMPONENT_NAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ComponentName { get; set; }
	
	[BsonElement(DB_KEY_ENTRIES), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ENTRIES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<Entry> Entries { get; init; }

	public AuditLog() => Entries = new List<Entry>();

	public class Entry : PlatformDataModel
	{
		internal const string DB_KEY_CURRENT_VERSION = "cv";
		internal const string DB_KEY_NEXT_VERSION = "nv";
		internal const string DB_KEY_TIMESTAMP = "ts";
		internal const string DB_KEY_IS_INVALID = "error";
		internal const string DB_KEY_NO_VERSION_PROVIDED = "warn";
		
		public const string FRIENDLY_KEY_CURRENT_VERSION = "currentVersion";
		public const string FRIENDLY_KEY_NEXT_VERSION = "nextVersion";
		public const string FRIENDLY_KEY_TIMESTAMP = "timestamp";
		public const string FRIENDLY_KEY_IS_INVALID = "invalid";
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

		public Entry()
		{
			Timestamp = Rumble.Platform.Common.Utilities.Timestamp.UnixTime;
			CurrentVersion = 0;
			NextVersion = 0;
		}

		public Entry(int currentVersion, int nextVersion)
		{
			CurrentVersion = currentVersion;
			NextVersion = nextVersion;
		}
	}
}