using System;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Services;
using Rumble.Platform.Common.Utilities.Serializers;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
	public class Installation : PlatformCollectionDocument
	{
		internal const string DB_KEY_ACCOUNT_MERGED_TO = "ma";
		internal const string DB_KEY_CLIENT_VERSION = "cv";
		internal const string DB_KEY_CREATED = "cd";
		internal const string DB_KEY_DATA_VERSION = "dv";
		internal const string DB_KEY_DEVICE_TYPE = "dt";
		internal const string DB_KEY_INSTALL_ID = "lsi";
		internal const string DB_KEY_TRANSFER_TOKEN = "mt";
		internal const string DB_KEY_MERGED_VERSION = "mv";
		internal const string DB_KEY_MODIFIED = "lc";
		internal const string DB_KEY_PREVIOUS_DATA_VERSION = "ldv";
		internal const string DB_KEY_SCREENNAME = "sn";
		internal const string DB_KEY_UPDATED = "lu";
		
		internal const string FRIENDLY_KEY_ACCOUNT_MERGED_TO = "accountMergedTo";
		internal const string FRIENDLY_KEY_CLIENT_VERSION = "clientVersion";
		internal const string FRIENDLY_KEY_CREATED = "dateCreated";
		internal const string FRIENDLY_KEY_DATA_VERSION = "dataVersion";
		internal const string FRIENDLY_KEY_DEVICE_TYPE = "deviceType";
		internal const string FRIENDLY_KEY_INSTALL_ID = "lastSavedInstallId";
		internal const string FRIENDLY_KEY_TRANSFER_TOKEN = "mergeTransactionId";
		internal const string FRIENDLY_KEY_MERGED_VERSION = "mergeVersion";
		internal const string FRIENDLY_KEY_MODIFIED = "lastChanged";
		internal const string FRIENDLY_KEY_PREVIOUS_DATA_VERSION = "lastDataVersion";
		internal const string FRIENDLY_KEY_SCREENNAME = "screenname";
		internal const string FRIENDLY_KEY_UPDATED = "lastUpdated";
		
		[BsonElement(DB_KEY_ACCOUNT_MERGED_TO), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_MERGED_TO), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string AccountMergedTo { get; set; }
		
		[BsonElement(DB_KEY_CLIENT_VERSION), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ClientVersion { get; set; }
		
		[BsonElement(DB_KEY_CREATED), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CREATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long CreatedTimestamp { get; set; }
		
		[BsonElement(DB_KEY_DATA_VERSION), BsonSaveAsString, BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string DataVersion { get; set; }
		
		[BsonElement(DB_KEY_DEVICE_TYPE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DEVICE_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string DeviceType { get; set; }
		
		[BsonElement(DB_KEY_INSTALL_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_INSTALL_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string InstallId { get; set; }
		//
		[BsonElement(DB_KEY_TRANSFER_TOKEN), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TRANSFER_TOKEN), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string TransferToken { get; set; }
		
		[BsonElement(DB_KEY_MERGED_VERSION), BsonSaveAsString, BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MERGED_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string MergeVersion { get; set; } // Merge Version?
		
		[BsonElement(DB_KEY_MODIFIED), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MODIFIED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long ModifiedTimestamp { get; set; }
		
		[BsonElement(DB_KEY_PREVIOUS_DATA_VERSION), BsonSaveAsString, BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PREVIOUS_DATA_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string PreviousDataVersion { get; set; }
		
		[BsonElement(DB_KEY_SCREENNAME), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCREENNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Screenname { get; set; }
		
		[BsonElement(DB_KEY_UPDATED), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_UPDATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long UpdatedTimestamp { get; set; }

		public Installation(string screenname)
		{
			CreatedTimestamp = UnixTime;
			Screenname = screenname;
			// ModifiedTimestamp = CreatedTimestamp;
			// UpdatedTimestamp = CreatedTimestamp;
		}

		public void GenerateRecoveryToken() => TransferToken = Guid.NewGuid().ToString();

		// This is a sanity check because using "install.Id" is confusing and hard to understand.
		// This is a temporary kluge because this model should be called `Account`... it's unfortunate we have a component called `Account` as well, but no way around it.
		[BsonIgnore]
		[JsonIgnore]
		public string AccountId => Id; 
	}
}