using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography.Xml;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
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

		public DiscriminatorService(AccountService accountService) : base("discriminators")
		{
			_accountService = accountService;
		}

		private DiscriminatorGroup Find(Player player) => FindOne(group => group.Members.Any(member => member.AccountId == player.AccountId));

		private int Assign(Player player)
		{
			List<int> attempted = new List<int>();
			for (int i = 0; i < MAX_ASSIGNMENT_ATTEMPTS; i++)
				if (TryAssign(player.AccountId, player.Screenname, out int output))
					return output;
				else
					attempted.Add(output);
			throw new DiscriminatorUnavailableException(player.AccountId, attempted);
		}
		public int Lookup(Player player) => Find(player)?.Number ?? Assign(player);

		/// <summary>
		/// Attempts to assign a discriminator to a player.  It the player's screenname is already taken by
		/// another accountId, returns false.
		/// </summary>
		/// <returns>True if successful.</returns>
		private bool TryAssign(string accountId, string screenname, out int discriminator)
		{
			// We need to declare local var 'target' too; out parameters can't be used in lambda expressions.
			int target = discriminator = new Random().Next(MINIMUM_VALUE, MAXIMUM_VALUE);

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

		/// <summary>
		/// Updates a player's screenname, assigning a new discriminator if necessary.
		/// </summary>
		public int Update(Player player)
		{
			DiscriminatorGroup existing = Find(player);

			if (!existing.HasScreenname(player.Screenname))
			{
				existing.UpdateMember(player);
				Update(existing);
			}
			else if (existing.Members.First(m => m.ScreenName == player.Screenname).AccountId == player.AccountId)
				Log.Warn(Owner.Default, "Tried to update a screenname, but the screenname is unchanged.");
			else
			{
				existing.RemoveMember(player.AccountId);
				Update(existing);
				return Assign(player);
			}
			return existing.Number;
		}

		public GenericData Search(params string[] accountIds)
		{
			GenericData output = new GenericData();

			DiscriminatorGroup[] groups = _collection
				.Find(Builders<DiscriminatorGroup>.Filter.In("members.aid", accountIds))
				.ToList()
				.ToArray();
			
			foreach (DiscriminatorGroup group in groups)
				foreach (DiscriminatorMember member in group.Members)
					output[member.AccountId] = group.Number;

			return output;
		}
	}
}