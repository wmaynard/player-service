using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.Data.Serializers;
using JsonIgnore = System.Text.Json.Serialization.JsonIgnoreAttribute;

namespace PlayerService.Models;

// TODO: IMPORTANT: When GPG profiles move to a different account, we need to update all oaids to match!
// TODO: Invalidate all other tokens, or change token-service to only keep 1 valid token.
[BsonIgnoreExtraElements]
public class Player : PlatformCollectionDocument
{
	private const string DB_KEY_APPLE_ACCOUNT = "apple";
	private const string DB_KEY_CREATED = "created";
	private const string DB_KEY_DEVICE = "device";
	private const string DB_KEY_GOOGLE_ACCOUNT = "google";
	private const string DB_KEY_LAST_LOGIN = "login";
	private const string DB_KEY_LINK_CODE = "linkCode";
	private const string DB_KEY_LINK_CODE_EXPIRATION = "linkExp";
	private const string DB_KEY_PARENT_ID = "parent";
	private const string DB_KEY_RUMBLE_ACCOUNT = "rumble";
	private const string DB_KEY_SCREENNAME = "sn";
	
	public const string FRIENDLY_KEY_APPLE_ACCOUNT = "appleAccount";
	public const string FRIENDLY_KEY_CREATED = "createdOn";
	public const string FRIENDLY_KEY_DEVICE = "deviceInfo";
	public const string FRIENDLY_KEY_DISCRIMINATOR = "discriminator";
	public const string FRIENDLY_KEY_GOOGLE_ACCOUNT = "googleAccount";
	public const string FRIENDLY_KEY_LAST_LOGIN = "lastLogin";
	public const string FRIENDLY_KEY_PARENT_ID = "parentId";
	public const string FRIENDLY_KEY_RUMBLE_ACCOUNT = "rumbleAccount";
	public const string FRIENDLY_KEY_SCREENNAME = "screenname";
	public const string FRIENDLY_KEY_SEARCH_WEIGHT = "weight";
	public const string FRIENDLY_KEY_TOKEN = "token";

	internal const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	
	[BsonElement(DB_KEY_APPLE_ACCOUNT)]
	[JsonPropertyName(FRIENDLY_KEY_APPLE_ACCOUNT)]
	public AppleAccount AppleAccount { get; set; }
	
	[BsonElement(DB_KEY_CREATED)]
	[JsonPropertyName(FRIENDLY_KEY_CREATED)]
	public long CreatedTimestamp { get; set; }
	
	[BsonElement(DB_KEY_DEVICE)]
	[JsonPropertyName(FRIENDLY_KEY_DEVICE)]
	public DeviceInfo Device { get; set; }
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DISCRIMINATOR)]
	public int? Discriminator { get; internal set; }
	
	[BsonElement(DB_KEY_GOOGLE_ACCOUNT)]
	[JsonPropertyName(FRIENDLY_KEY_GOOGLE_ACCOUNT)]
	public GoogleAccount GoogleAccount { get; set; }
	
	[BsonElement(DB_KEY_LAST_LOGIN)]
	[JsonPropertyName(FRIENDLY_KEY_LAST_LOGIN)]
	public long LastLogin { get; set; }
	
	[BsonElement(DB_KEY_LINK_CODE)]
	[JsonIgnore]
	public string LinkCode { get; set; }
	
	[BsonElement(DB_KEY_LINK_CODE_EXPIRATION), BsonIgnoreIfDefault]
	[JsonIgnore]
	public long LinkExpiration { get; set; }
	
	[BsonIgnore]
	[JsonIgnore]
	public Player Parent { get; set; }
	
	[BsonElement(DB_KEY_PARENT_ID), BsonIgnoreIfNull]
	[JsonPropertyName(FRIENDLY_KEY_PARENT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	// [TextIndex]
	public string ParentId { get; set; }
	
	[BsonElement(DB_KEY_RUMBLE_ACCOUNT)]
	[JsonPropertyName(FRIENDLY_KEY_RUMBLE_ACCOUNT)]
	public RumbleAccount RumbleAccount { get; set; }

	[BsonElement(DB_KEY_SCREENNAME)]
	[JsonPropertyName(FRIENDLY_KEY_SCREENNAME)]
	[SimpleIndex]
	// [TextIndex]
	public string Screenname { get; set; }
	
	[BsonIgnore]
	[JsonPropertyName(FRIENDLY_KEY_SEARCH_WEIGHT), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public float SearchWeight { get; private set; }

	[BsonIgnore]
	[JsonPropertyName(FRIENDLY_KEY_TOKEN)]
	public string Token { get; set; }
	
	// This is a sanity check because using "install.Id" is confusing and hard to understand.
	// This is a temporary kluge because this model should be called `Account`... it's unfortunate we have a component called `Account` as well, but no way around it.
	[BsonIgnore]
	[JsonIgnore]
	public string AccountId => ParentId ?? Id;
	
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
		const float WEIGHT_LOGIN_NAME = 100;
		const float WEIGHT_ID = 50;
		const float WEIGHT_ID_OVERRIDE = 25;
		const float WEIGHT_ID_INSTALL = 10;
		const float WEIGHT_EMAIL = 100;
		const float WEIGHT_REAL_NAME = 200;

		term = term.ToLower();
		
		float output = 0;
		int termWeight = (int)Math.Pow(term.Length, 2);	// longer terms carry more weight.  Useful if we want to search on multiple terms separately later.
		
		float scoreLength(string field, string t) => t.Length / (float)field.Length;  // Modify the base score based on the term's position in the field and the field length.
		float scorePosition(string field, string t) => 1 - field.IndexOf(t, StringComparison.Ordinal) * (1 / (float)field.Length); // Reduce score based on its index in the field to favor earlier matches.
		float weigh(string field, float baseWeight) => output += 
			field != null && field.ToLower().Contains(term)
				? baseWeight * termWeight * scoreLength(field, term) * scorePosition(field, term)
				: 0;
		
		// TODO: Exact matches?

		weigh(Screenname, WEIGHT_SCREENNAME);
		weigh(Id, WEIGHT_ID);
		weigh(ParentId, WEIGHT_ID_OVERRIDE);
		weigh(Device?.InstallId, WEIGHT_ID_INSTALL);
		weigh(RumbleAccount?.Email, WEIGHT_EMAIL);
		weigh(RumbleAccount?.Username, WEIGHT_LOGIN_NAME);
		weigh(GoogleAccount?.Email, WEIGHT_EMAIL);
		weigh(GoogleAccount?.Name, WEIGHT_REAL_NAME);

		output += termWeight;
		
		return SearchWeight = output;  // If we later evaluate search terms separately later, remove this assignment.
	}

	internal static void WeighSearchResults(string[] terms, ref List<Player> players)
	{
		foreach (Player player in players)
			foreach (string term in terms)
				player.WeighSearchTerm(term);

		players = players
			.OrderByDescending(player => player.SearchWeight)
			.ToList();

		double TotalWeight = players.Sum(player => player.SearchWeight);
		foreach (Player player in players)
			player.SearchConfidence = 100f * (float)(player.SearchWeight / TotalWeight);
	}
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName("confidence"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public float SearchConfidence { get; set; }

	/// <summary>
	/// This MUST be called before returning it to the client to avoid spilling sensitive data.
	/// </summary>
	public Player Prune()
	{
		RumbleAccount?.Prune();
		return this;
	}
}