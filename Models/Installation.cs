using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using Newtonsoft.Json;
using PlayerService.Services;
using Rumble.Platform.Common.Utilities.Serializers;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
	public class Installation : GroovyUpgradeDocument
	{
		internal const string DB_KEY_ACCOUNT_MERGED_TO = "ma";
		internal const string DB_KEY_CLIENT_VERSION = "cv";
		internal const string DB_KEY_CREATED = "cd";
		internal const string DB_KEY_DATA_VERSION = "dv";
		internal const string DB_KEY_DEVICE_TYPE = "dt";
		internal const string DB_KEY_INSTALL_ID = "lsi";
		internal const string DB_KEY_MERGE_TRANSACTION_ID = "mt";
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
		internal const string FRIENDLY_KEY_MERGE_TRANSACTION_ID = "mergeTransactionId";
		internal const string FRIENDLY_KEY_MERGED_VERSION = "mergeVersion";
		internal const string FRIENDLY_KEY_MODIFIED = "lastChanged";
		internal const string FRIENDLY_KEY_PREVIOUS_DATA_VERSION = "lastDataVersion";
		internal const string FRIENDLY_KEY_SCREENNAME = "screenname";
		internal const string FRIENDLY_KEY_UPDATED = "lastUpdated";
		
		[BsonElement(DB_KEY_ACCOUNT_MERGED_TO), BsonIgnoreIfNull]
		[JsonProperty(FRIENDLY_KEY_ACCOUNT_MERGED_TO, NullValueHandling = NullValueHandling.Ignore)]
		public string AccountMergedTo { get; private set; }
		
		[BsonElement(DB_KEY_CLIENT_VERSION)]
		[JsonProperty(FRIENDLY_KEY_CLIENT_VERSION)]
		public string ClientVersion { get; private set; }
		
		[BsonElement(DB_KEY_CREATED)]
		[JsonProperty(FRIENDLY_KEY_CREATED)]
		public long CreatedTimestamp { get; private set; }
		
		[BsonElement(DB_KEY_DATA_VERSION), BsonSaveAsString]
		[JsonProperty(FRIENDLY_KEY_DATA_VERSION)]
		public string DataVersion { get; private set; }
		
		[BsonElement(DB_KEY_DEVICE_TYPE)]
		[JsonProperty(FRIENDLY_KEY_DEVICE_TYPE)]
		public string DeviceType { get; private set; }
		
		[BsonElement(DB_KEY_INSTALL_ID)]
		[JsonProperty(FRIENDLY_KEY_INSTALL_ID)]
		public string InstallId { get; private set; }
		
		[BsonElement(DB_KEY_MERGE_TRANSACTION_ID), BsonIgnoreIfNull]
		[JsonProperty(FRIENDLY_KEY_MERGE_TRANSACTION_ID, NullValueHandling = NullValueHandling.Ignore)]
		public string MergeTransactionID { get; private set; } // Merge transaction?
		
		[BsonElement(DB_KEY_MERGED_VERSION), BsonSaveAsString, BsonIgnoreIfNull]
		[JsonProperty(FRIENDLY_KEY_MERGED_VERSION, NullValueHandling = NullValueHandling.Ignore)]
		public string MergeVersion { get; private set; } // Merge Version?
		
		[BsonElement(DB_KEY_MODIFIED)]
		[JsonProperty(FRIENDLY_KEY_MODIFIED)]
		public long ModifiedTimestamp { get; private set; }
		
		[BsonElement(DB_KEY_PREVIOUS_DATA_VERSION), BsonSaveAsString]
		[JsonProperty(FRIENDLY_KEY_PREVIOUS_DATA_VERSION)]
		public string PreviousDataVersion { get; private set; }
		
		[BsonElement(DB_KEY_SCREENNAME)]
		[JsonProperty(FRIENDLY_KEY_SCREENNAME)]
		public string Screenname { get; private set; }
		
		[BsonElement(DB_KEY_UPDATED)]
		[JsonProperty(FRIENDLY_KEY_UPDATED)]
		public long UpdatedTimestamp { get; private set; }
	}
}