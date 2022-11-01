using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.Data.Serializers;

namespace PlayerService.Models;

// TODO: IMPORTANT: When GPG profiles move to a different account, we need to update all oaids to match!
// TODO: Invalidate all other tokens, or change token-service to only keep 1 valid token.
[BsonIgnoreExtraElements]
public class Player : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID_OVERRIDE = "oaid";
	internal const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	internal const string FRIENDLY_KEY_ACCOUNT_ID_OVERRIDE = "accountIdOverride";
	internal const string FRIENDLY_KEY_DISCRIMINATOR = "discriminator";

	[BsonElement("created"), JsonPropertyName("created"), BsonIgnoreIfDefault, JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long CreatedTimestamp { get; set; }
	
	[BsonElement("login"), JsonPropertyName("lastLogin"), JsonInclude]
	public long LastLogin { get; set; }
	
	[BsonElement("linkCode"), JsonPropertyName("linkCode"), BsonIgnoreIfNull, JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string LinkCode { get; set; }
	
	[BsonElement("linkExpiration"), BsonIgnoreIfDefault, JsonIgnore]
	public long LinkExpiration { get; set; }

	[BsonElement("sn"), JsonPropertyName("screenname"), BsonIgnoreIfNull, JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Screenname { get; set; }
	
	[BsonElement("device"), JsonPropertyName("device"), JsonInclude]
	public DeviceInfo Device { get; set; }
	
	[BsonElement("google"), JsonPropertyName("googleAccount")]
	public GoogleAccount GoogleAccount { get; set; }
	
	[BsonElement("apple"), JsonPropertyName("appleAccount")]
	public AppleAccount AppleAccount { get; set; }
	
	[BsonElement("rumble"), JsonPropertyName("rumbleAccount")]
	public RumbleAccount RumbleAccount { get; set; }
	
	[BsonIgnore, JsonIgnore]
	public Player Parent { get; set; }
	
	[BsonElement("link"), JsonPropertyName("linkedAccountId"), BsonIgnoreIfDefault, JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AccountIdOverride { get; set; }
	
	[BsonIgnore]
	[JsonPropertyName("token")]
	public string Token { get; set; }
	
	
	
	
	
	
	
	
	
	
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DISCRIMINATOR), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Discriminator { get; internal set; }
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName("linkedAccounts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Player[] LinkedAccounts { get; internal set; }
	
	[BsonIgnore]
	[JsonIgnore]
	public bool IsLinkedAccount => !string.IsNullOrWhiteSpace(AccountIdOverride) && AccountIdOverride != Id;

	public Player(string screenname)
	{
		CreatedTimestamp = Timestamp.UnixTime;
		Screenname = screenname;
		// ModifiedTimestamp = CreatedTimestamp;
		// UpdatedTimestamp = CreatedTimestamp;
	}

	public void GenerateRecoveryToken() => LinkCode = Guid.NewGuid().ToString();

	// This is a sanity check because using "install.Id" is confusing and hard to understand.
	// This is a temporary kluge because this model should be called `Account`... it's unfortunate we have a component called `Account` as well, but no way around it.
	[BsonIgnore]
	[JsonIgnore]
	public string AccountId => AccountIdOverride ?? Id;
	

	
	
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
		if (Device.InstallId.Contains(term))
			output += weigh(Device.InstallId, baseWeight: WEIGHT_ID_INSTALL);
		output += termWeight;
		
		return SearchWeight = output;  // If we later evaluate search terms separately later, remove this assignment.
	}

	internal void UpdateSso(SsoData sso)
	{
		if (sso == null)
			return;

		if (GoogleAccount?.Id != sso.GoogleAccount?.Id)
			GoogleAccount = sso.GoogleAccount;
		if (AppleAccount?.Id != sso.AppleAccount?.Id)
			AppleAccount = sso.AppleAccount;

		if (RumbleAccount.Status != RumbleAccount.AccountStatus.None)
			return;
		
		RumbleAccount = sso.RumbleAccount;
		RumbleAccount.Status = RumbleAccount.AccountStatus.NeedsConfirmation;
	}
}