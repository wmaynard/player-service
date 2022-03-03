using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models;
public class DiscriminatorMember : PlatformDataModel
{
	public const string DB_KEY_ACCOUNT_ID = "aid";
	public const string DB_KEY_SCREENNAME = "sn";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	public const string FRIENDLY_KEY_SCREENNAME = "screenname";
	
	[BsonElement(DB_KEY_ACCOUNT_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
	public string AccountId { get; private set; }
	
	[BsonElement(DB_KEY_SCREENNAME)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCREENNAME)]
	public string ScreenName { get; private set; }

	public DiscriminatorMember(string accountId, string screenname)
	{
		AccountId = accountId;
		ScreenName = screenname;
	}

	public void Update(string screenname) => ScreenName = screenname;
}