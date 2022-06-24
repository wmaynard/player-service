using System;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.Serializers;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models;

// TODO: IMPORTANT: When GPG profiles move to a different account, we need to update all oaids to match!
// TODO: Invalidate all other tokens, or change token-service to only keep 1 valid token.
[BsonIgnoreExtraElements]
public class Player : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID_OVERRIDE = "oaid";
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

	internal const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	internal const string FRIENDLY_KEY_ACCOUNT_ID_OVERRIDE = "accountIdOverride";
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
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_UPDATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long UpdatedTimestamp { get; set; }
	
	/// <summary>
	/// This is used exclusively by the admin portal at the moment - it's a frontend-only field that is never stored in MongoDB.
	/// This is needed to join the discriminator to /admin/search Player results.
	/// </summary>
	[BsonIgnore]
	[JsonInclude, JsonPropertyName("discriminator"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Discriminator { get; internal set; }
	/// <summary>
	/// Another feature for frontend readability.  Creates the fully-qualified screenname (e.g. JoeMcFugal#1234).
	/// </summary>
	[BsonIgnore]
	[JsonInclude, JsonPropertyName("username")]
	public string Username => (Screenname ?? "") + (Discriminator != null ? $"#{Discriminator}" : "");
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName("linkedAccounts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Player[] LinkedAccounts { get; internal set; }
	
	[BsonIgnore]
	[JsonIgnore]
	public bool IsLinkedAccount => !string.IsNullOrWhiteSpace(AccountIdOverride) && AccountIdOverride != Id;

	public Player(string screenname)
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
	public string AccountId => AccountIdOverride ?? Id;
	
	[BsonElement(DB_KEY_ACCOUNT_ID_OVERRIDE), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID_OVERRIDE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AccountIdOverride { get; set; }
	
	
	[BsonIgnore]
	[JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public float SearchWeight { get; private set; }

	/// <summary>
	/// Sanitizes the output for /player/launch.  AccountIdOverride has caused confusion, so for clarity, the override
	/// will just be provided as the ID for the game server.
	/// </summary>
	internal void PrepareIdForOutput()
	{
		if (string.IsNullOrWhiteSpace(AccountIdOverride))
			return;
		Id = AccountIdOverride;
		AccountIdOverride = null;
	}

	/// <summary>
	/// Uses arbitrary values to decide how relevant a search term is to this player.  Screennames are heavily favored over all other fields; consider a situation where we have 
	/// a user with the screenname "Deadpool", and someone is searching with the term "dead".  Being all hex characters, we probably don't want Mongo IDs ranking before
	/// "Deadpool".  For future reference, any user-generated field that we search on should rank earlier than ID fields.  If a portal user wants to search by ID, they're
	/// likely to use the full ID, which shouldn't match user-generated fields anyway.
	/// </summary>
	/// <param name="term">The term to search for.</param>
	/// <returns>An arbitrary search score indicating how relevant the search term is to this Player.</returns>
	internal float WeighSearchTerm(string term)
	{
		if (term == null)
			return 0;

		const float WEIGHT_SCREENNAME = 1_000;
		const float WEIGHT_ID = 50;
		const float WEIGHT_ID_OVERRIDE = 25;
		const float WEIGHT_ID_INSTALL = 10;

		term = term.ToLower();
		
		float output = 0;
		int termWeight = (int)Math.Pow(term.Length, 2);	// longer terms carry more weight.  Useful if we want to search on multiple terms separately later.
		
		float scoreLength(string field, string t) => t.Length / (float)field.Length;  // Modify the base score based on the term's position in the field and the field length.
		float scorePosition(string field, string t) => 1 - field.IndexOf(t, StringComparison.Ordinal) * (1 / (float)field.Length); // Reduce score based on its index in the field to favor earlier matches.
		float weigh(string field, float baseWeight) => field != null
			? baseWeight * termWeight * scoreLength(field, term) * scorePosition(field, term)
			: 0;

		if (Screenname != null && Screenname.ToLower().Contains(term))
			output += weigh(Screenname.ToLower(), baseWeight: WEIGHT_SCREENNAME);
		if (Id.Contains(term))
			output += weigh(Id, baseWeight: WEIGHT_ID);
		if (AccountIdOverride != null && AccountIdOverride.Contains(term))
			output += weigh(AccountIdOverride, baseWeight: WEIGHT_ID_OVERRIDE);
		if (InstallId.Contains(term))
			output += weigh(InstallId, baseWeight: WEIGHT_ID_INSTALL);
		output += termWeight;
		
		return SearchWeight = output;  // If we later evaluate search terms separately later, remove this assignment.
	}
}