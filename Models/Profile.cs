using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
		[BsonIgnoreExtraElements]
	public class Profile : PlatformCollectionDocument
	{
		private const string DB_KEY_ACCOUNT_ID = "aid";
		private const string DB_KEY_CREATED_TIMESTAMP = "cd";
		private const string DB_KEY_CLIENT_TYPE = "clientType";
		private const string DB_KEY_CLIENT_VERSION = "clientVersion";
		private const string DB_KEY_DEVICE_TYPE = "deviceType";
		private const string DB_KEY_DISCRIMINATOR = "discriminator";
		private const string DB_KEY_LANGUAGE = "systemLanguage";
		private const string DB_KEY_MERGE_ACCOUNT_ID = "mergeAccountId";
		private const string DB_KEY_MERGE_TOKEN = "mergeToken";
		private const string DB_KEY_MODIFIED_TIMESTAMP = "lu";
		private const string DB_KEY_OPERATING_SYSTEM = "osVersion";
		private const string DB_KEY_PROFILE_ID = "pid";
		private const string DB_KEY_REQUEST_ID = "requestId";
		private const string DB_KEY_SCREENNAME = "screenName";
		private const string DB_KEY_TYPE = "type";
		
		public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
		public const string FRIENDLY_KEY_CREATED_TIMESTAMP = "CreatedTimestamp";
		public const string FRIENDLY_KEY_CLIENT_TYPE = "ClientType";
		public const string FRIENDLY_KEY_CLIENT_VERSION = "ClientVersion";
		public const string FRIENDLY_KEY_DEVICE_TYPE = "DeviceType";
		public const string FRIENDLY_KEY_DISCRIMINATOR = "Discriminator";
		public const string FRIENDLY_KEY_LANGUAGE = "SystemLanguage";
		public const string FRIENDLY_KEY_MERGE_ACCOUNT_ID = "MergeAccountId";
		public const string FRIENDLY_KEY_MERGE_TOKEN = "TransferToken";
		public const string FRIENDLY_KEY_MODIFIED_TIMESTAMP = "ModifiedTimestamp";
		public const string FRIENDLY_KEY_OPERATING_SYSTEM = "OperatingSystem";
		public const string FRIENDLY_KEY_PROFILE_ID = "ProfileId";
		public const string FRIENDLY_KEY_REQUEST_ID = "RequestId";
		public const string FRIENDLY_KEY_SCREENNAME = "Screenname";
		public const string FRIENDLY_KEY_TYPE = "Type";

		[BsonElement(DB_KEY_ACCOUNT_ID), BsonRepresentation(BsonType.ObjectId)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
		public string AccountId { get; set; }
		
		[BsonElement(DB_KEY_CLIENT_TYPE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ClientType { get; private set; }
		
		[BsonElement(DB_KEY_CLIENT_VERSION), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_VERSION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ClientVersion { get; private set; }
		
		[BsonElement(DB_KEY_CREATED_TIMESTAMP), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_CREATED_TIMESTAMP), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long CreatedTimestamp { get; private set; }
		
		[BsonElement(DB_KEY_DEVICE_TYPE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DEVICE_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string DeviceType { get; private set; }
		
		[BsonElement(DB_KEY_DISCRIMINATOR), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DISCRIMINATOR), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public long? Discriminator { get; private set; }
		
		[BsonElement(DB_KEY_LANGUAGE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_LANGUAGE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Language { get; private set; }
		
		[BsonElement(DB_KEY_MERGE_ACCOUNT_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MERGE_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string MergeAccountId { get; private set; }
		
		[BsonElement(DB_KEY_MERGE_TOKEN), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MERGE_TOKEN), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string MergeToken { get; private set; }
		
		[BsonElement(DB_KEY_MODIFIED_TIMESTAMP), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MODIFIED_TIMESTAMP), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long ModifiedTimestamp { get; private set; }
		
		[BsonElement(DB_KEY_OPERATING_SYSTEM), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_OPERATING_SYSTEM), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string OperatingSystem { get; private set; }
		
		[BsonElement(DB_KEY_PROFILE_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PROFILE_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ProfileId { get; private set; }
		
		[BsonElement(DB_KEY_REQUEST_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_REQUEST_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string RequestId { get; private set; }
		
		[BsonElement(DB_KEY_SCREENNAME), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCREENNAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ScreenName { get; private set; }
		
		[BsonElement(DB_KEY_TYPE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Type { get; private set; }
		
		#region PROBABLY_UNUSED_FIELDS
		// These are all keys found on some data points, but not those that are created in recent usage of player-service.
		// They're likely old and can be dropped, but we should hold on to them just in case.  As a backlog task, we can
		// remove these fields and delete any relevant keys in mongo.
		
		private const string DB_KEY_ADVERTISING_ID = "advertisingId";
		public const string FRIENDLY_KEY_ADVERTISING_ID = "AdvertisingId";
		[BsonElement(DB_KEY_ADVERTISING_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ADVERTISING_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string AdvertisingId { get; private set; }

		private const string DB_KEY_GAME = "game";
		public const string FRIENDLY_KEY_GAME = "Game";
		[BsonElement(DB_KEY_GAME), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_GAME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string GameKey { get; private set; }
		
		private const string DB_KEY_SECRET = "secret";
		public const string FRIENDLY_KEY_SECRET = "Secret";
		[BsonElement(DB_KEY_SECRET), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SECRET), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Secret { get; private set; }

		private const string DB_KEY_SCREENNAME_OLD = "sn";
		public const string FRIENDLY_KEY_SCREENNAME_OLD = "ScreennameDeprecated";
		[BsonElement(DB_KEY_SCREENNAME_OLD), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCREENNAME_OLD), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ScreenName_Deprecated { get; private set; }
		#endregion PROBABLY_UNUSED_FIELDS

		private Profile()
		{
			CreatedTimestamp = UnixTime;
		}

		public Profile(Installation install) : this()
		{
			CreatedTimestamp = install.CreatedTimestamp;
			ModifiedTimestamp = install.ModifiedTimestamp;
			ClientVersion = install.ClientVersion;
			ProfileId = install.InstallId;
			AccountId = install.AccountId;
			Type = TYPE_INSTALL; // TODO: const or enum
		}

		public Profile(string ssoId, string ssoType) : this()
		{
			ProfileId = ssoId;
			Type = ssoType;
		}
		
		
		public const string TYPE_INSTALL = "installId";
		public const string TYPE_GOOGLE = "googlePlay";
		public const string TYPE_GAME_CENTER = "gameCenter";
		public const string TYPE_APPLE_ID = "appleId";
	}
}
// Summary
// bool HasPurchases
// string lastWorldProgress (world campaigns field)
// Screenname / Discriminator
