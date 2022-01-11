using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
	public class Item : PlatformCollectionDocument
	{
		private const string DB_KEY_ACCOUNT_ID = "aid";
		private const string DB_KEY_ITEM_ID = "iid";
		private const string DB_KEY_DATA = "data";
		private const string DB_KEY_TYPE = "type";

		public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
		public const string FRIENDLY_KEY_ITEM_ID = "iid";
		public const string FRIENDLY_KEY_DATA = "data";
		public const string FRIENDLY_KEY_TYPE = "type";
		public const string FRIENDLY_KEY_DELETE = "delete";

		[BsonElement(DB_KEY_ACCOUNT_ID), BsonRepresentation(BsonType.ObjectId)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
		public string AccountId { get; set; }
		
		[BsonElement(DB_KEY_ITEM_ID)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ITEM_ID)]
		public string ItemId { get; set; }
		
		[BsonElement(DB_KEY_DATA)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public GenericData Data { get; set; }
		
		[BsonElement(DB_KEY_TYPE)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Type { get; set; }
		
		[BsonIgnore]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DELETE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool MarkedForDeletion { get; set; }
	}
}