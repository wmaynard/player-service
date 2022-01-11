using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
	public class Component : PlatformCollectionDocument
	{
		// TODO: Check keys
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
		
		private const string DB_KEY_ACCOUNT_ID = "aid";
		private const string DB_KEY_DATA = "data";

		public const string FRIENDLY_KEY_ACCOUNT_ID = "aid";
		public const string FRIENDLY_KEY_DATA = "data";
		
		[BsonElement(DB_KEY_ACCOUNT_ID), BsonRepresentation(BsonType.ObjectId)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
		public string AccountId { get; private set; }
		
		[BsonElement(DB_KEY_DATA)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA)]
		public GenericData Data { get; set; }

		public Component(string accountId, GenericData data = null)
		{
			AccountId = accountId;
			Data = data ?? new GenericData();
		}
	}
}