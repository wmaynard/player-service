using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Services.ComponentServices;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class DiscriminatorService : PlatformMongoService<DiscriminatorGroup>
	{
		private const int MINIMUM_VALUE = 1;
		private const int MAXIMUM_VALUE = 9_999;
		private const int MAX_ASSIGNMENT_ATTEMPTS = 50;

		private readonly AccountService _accountService;

		public DiscriminatorService(AccountService accountService) : base("discriminators_temp")
		{
			_accountService = accountService;
		}

		private DiscriminatorGroup Find(Player player) => FindOne(group => group.Members.Any(member => member.AccountId == player.AccountId));

		public int Lookup(Player player) => Find(player)?.Number ?? TryAssign(player);

		private int TryAssign(Player player)
		{
			List<int> attempted = new List<int>();
			for (int i = 0; i < MAX_ASSIGNMENT_ATTEMPTS; i++)
				if (Assign(player.AccountId, player.Screenname, out int output))
					return output;
				else
					attempted.Add(output);
			throw new DiscriminatorUnavailableException(player.AccountId, attempted);
		}

		private bool Assign(string accountId, string screenname, out int discriminator)
		{
			int target = discriminator = new Random().Next(MINIMUM_VALUE, MAXIMUM_VALUE); // We need a local var too; out parameters can't be used in lambda expressions.

			DiscriminatorGroup[] previous = Find(group => group.Members.Any(member => member.AccountId == accountId));
			DiscriminatorGroup next = FindOne(group => group.Number == target);
			
			// Someone else with the same screenname has this number.  Don't update anything.
			if (next != null && next.Members.Any(member => member.ScreenName == screenname && member.AccountId != accountId))
				return false;
			
			next ??= Create(new DiscriminatorGroup(target));  // We haven't assigned this discriminator to anyone yet.  Create a new DiscriminatorGroup for it.
			next.AddMember(accountId, screenname); // TODO: need to Create on a new group
			
			// Ideally, players are only ever in one group, but just in case, we'll loop through all of them.
			foreach (DiscriminatorGroup group in previous)
			{
				group.RemoveMember(accountId);
				Update(group);
			} 
			Update(next);

			return true;
		}

		public int Update(Player player)
		{
			DiscriminatorGroup existing = Find(player);

			if (!existing.HasScreenname(player.Screenname))
			{
				existing.UpdateMember(player);
				existing.RemoveMember(player.AccountId);					// removes
				existing.AddMember(player.AccountId, player.Screenname);
				Update(existing);
			}
			else if (existing.Members.First(m => m.ScreenName == player.Screenname).AccountId == player.AccountId)
				Log.Warn(Owner.Default, "Tried to update a screenname, but the screenname is unchanged.");
			else
			{
				existing.RemoveMember(player.AccountId);
				Update(existing);
				return TryAssign(player);
			}
			return existing.Number;
		}
	}

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

		public DiscriminatorGroup(int number, DiscriminatorMember member) : this(number)
		{
			Members.Add(member);
		}

		public bool HasScreenname(string screenname) => Members.Any(member => member.ScreenName == screenname);
		// public bool HasMember(string accountId) => Members.Any(member => member.AccountId == accountId);
		public void RemoveMember(string accountId) => Members.RemoveAll(member => member.AccountId == accountId);
		public void AddMember(string accountId, string screenname) => Members.Add(new DiscriminatorMember(accountId, screenname));
		public void UpdateMember(Player player) => Members.First(m => m.AccountId == player.AccountId).Update(player.Screenname);
	}

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
}