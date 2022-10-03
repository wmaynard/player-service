using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace PlayerService.Models;
public class DiscriminatorGroup : PlatformCollectionDocument
{
	public const string DB_KEY_NUMBER = "number";
	public const string DB_KEY_MEMBERS = "members";

	// TODO: The naming of this collection predates current Platform practices; update these to be consistent with nomenclature rules
	public const string FRIENDLY_KEY_NUMBER = DB_KEY_NUMBER;
	public const string FRIENDLY_KEY_MEMBERS = DB_KEY_MEMBERS;

	[BsonElement(DB_KEY_MEMBERS)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MEMBERS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<DiscriminatorMember> Members { get; private set; }
	
	[BsonElement(DB_KEY_NUMBER)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_NUMBER)]
	public int Number { get; private set; }

	public DiscriminatorGroup(int number)
	{
		Number = number;
		Members = new List<DiscriminatorMember>();
	}
	
	public DiscriminatorGroup(int number, DiscriminatorMember member) : this(number) => Members.Add(member);

	public bool HasScreenname(string screenname) => Members.Any(member => member.ScreenName == screenname);
	// public bool HasMember(string accountId) => Members.Any(member => member.AccountId == accountId);
	public void RemoveMember(string accountId) => Members.RemoveAll(member => member.AccountId == accountId);
	public void AddMember(string accountId, string screenname) => Members.Add(new DiscriminatorMember(accountId, screenname));
	public void UpdateMember(Player player) => Members.First(m => m.AccountId == player.AccountId).Update(player.Screenname);
}