using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using PlayerService.Exceptions;
using PlayerService.Services.ComponentServices;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class DiscriminatorService : PlatformMongoService<DiscriminatorGroup>
	{
		private const int MINIMUM_VALUE = 1;
		private const int MAXIMUM_VALUE = 9_999;
		private const int MAX_ASSIGNMENT_ATTEMPTS = 50;

		private readonly ComponentAccountService _accountService;

		public DiscriminatorService(ComponentAccountService accountService) : base("discriminators")
		{
			_accountService = accountService;
		}

		public int Lookup(string accountId, string screenName)
		{
			DiscriminatorGroup existing = FindOne(group => group.Members.Any(member => member.AccountId == accountId));

			if (existing != null)
				return existing.Number;

			List<int> attempts = new List<int>();
			for (int i = 0; i < MAX_ASSIGNMENT_ATTEMPTS; i++)
				if (Assign(accountId, screenName, out int discriminator))
					return discriminator;
				else
					attempts.Add(discriminator);
			throw new DiscriminatorUnavailableException(accountId, attempts);
		}

		private bool Assign(string accountId, string screenname, out int discriminator)
		{
			int target = discriminator = new Random().Next(MINIMUM_VALUE, MAXIMUM_VALUE); // We need a local var too; out parameters can't be used in lambda expressions.

			DiscriminatorGroup[] previous = Find(group => group.Members.Any(member => member.AccountId == accountId));
			DiscriminatorGroup next = FindOne(group => group.Number == target);
			
			// Someone else with the same screenname has this number.  Don't update anything.
			if (next != null && next.Members.Any(member => member.ScreenName == screenname && member.AccountId != accountId))
				return false;
			
			next ??= new DiscriminatorGroup(target);  // We haven't assigned this discriminator to anyone yet.  Create a new DiscriminatorGroup for it.
			next.Members.Add(new DiscriminatorMember(accountId, screenname));
			
			// Ideally, players are only ever in one group, but just in case, we'll loop through all of them.
			foreach (DiscriminatorGroup group in previous)
			{
				group.RemoveMember(accountId);
				Update(group);
			} 
			Update(next);

			return true;
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
	}

	public class DiscriminatorMember : PlatformDataModel
	{
		public const string DB_KEY_ACCOUNT_ID = "aid";
		public const string DB_KEY_SCREENNAME = "sn";

		public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
		public const string FRIENDLY_KEY_SCREENNAME = "screenName";
		
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
	}
}