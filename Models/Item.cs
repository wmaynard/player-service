using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace PlayerService.Models;
public class Item : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_ITEM_ID = "iid";
	internal const string DB_KEY_DATA = "data";
	internal const string DB_KEY_TYPE = "type";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
	public const string FRIENDLY_KEY_ITEM_ID = "iid";
	public const string FRIENDLY_KEY_DATA = "data";
	public const string FRIENDLY_KEY_TYPE = "type";
	public const string FRIENDLY_KEY_DELETE = "delete";

	[SimpleIndex(DB_KEY_ACCOUNT_ID, "accountId")]
	[BsonElement(DB_KEY_ACCOUNT_ID), BsonRepresentation(BsonType.ObjectId)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
	public string AccountId { get; set; }
	
	[BsonElement(DB_KEY_ITEM_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ITEM_ID)]
	[SimpleIndex(DB_KEY_ITEM_ID, FRIENDLY_KEY_ITEM_ID)]
	public string ItemId { get; set; }
	
	[BsonElement(DB_KEY_DATA)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RumbleJson Data { get; set; }
	
	[BsonElement(DB_KEY_TYPE)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Type { get; set; }
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DELETE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool MarkedForDeletion { get; set; }

	[BsonIgnore]
	[JsonIgnore]
	internal IdMap Map => new IdMap
	{
		Id = Id,
		ItemId = ItemId
	};

	internal class IdMap : PlatformDataModel
	{
		public string Id { get; set; }
		
		[BsonElement(DB_KEY_ITEM_ID)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ITEM_ID)]
		public string ItemId { get; set; }
	}
}