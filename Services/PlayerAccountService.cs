using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using PlayerService.Models;
using PlayerService.Models.Login;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services;

public class PlayerAccountService : PlatformMongoService<Player>
{
	private readonly NameGeneratorService _nameGenerator;
	public PlayerAccountService(NameGeneratorService nameGenerator) : base("players") => _nameGenerator = nameGenerator; 

	public Player Find(string accountId) => FindOne(player => player.Id == accountId);

	public List<Player> DirectoryLookup(params string[] accountIds) => _collection
		.Find(Builders<Player>.Filter.In(player => player.Id, accountIds))
		.ToList();

	/// <summary>
	/// When using SSO, this update gets called, which unifies all screennames.  New devices generate new screennames and need to be updated
	/// to reflect the account they're linked to.
	/// </summary>
	/// <param name="screenname"></param>
	/// <param name="accountId"></param>
	/// <returns></returns>
	public int SyncScreenname(string screenname, string accountId)
	{
		int affected = (int)_collection.UpdateMany(
			filter: player => player.Id == accountId || player.ParentId == accountId,
			update: Builders<Player>.Update.Set(player => player.Screenname, screenname)
		).ModifiedCount;
		
		// TODO: project accountId / accountIdOverride, use that in AccountService to update field in components

		return affected;
	}

	public Player FromDevice(DeviceInfo device, bool isUpsert = false)
	{
		Player output = isUpsert
			? _collection.FindOneAndUpdate<Player>(
				filter: player => player.Device.InstallId == device.InstallId,
				update: Builders<Player>.Update
					.Set(player => player.Device, device)
					.Set(player => player.LastLogin, Timestamp.UnixTime),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = true,
					ReturnDocument = ReturnDocument.After
				}
			)
			: _collection
				.Find(player => player.Device.InstallId == device.InstallId)
				.FirstOrDefault();

		if (output?.ParentId != null)
			output.Parent = _collection
				.Find(player => player.Id == output.ParentId)
				.FirstOrDefault();
		return output?.Parent ?? output;
	}

	public Player[] FromSso(SsoData sso)
	{
		if (sso == null)
			return Array.Empty<Player>();
		
		FilterDefinitionBuilder<Player> builder = Builders<Player>.Filter;

		List<FilterDefinition<Player>> filters = new List<FilterDefinition<Player>>();
		// builder.ElemMatch(field: player => player.GoogleAccount, filter: )

		if (sso.GoogleAccount != null)
			filters.Add(builder.Eq(player => player.GoogleAccount.Id, sso.GoogleAccount.Id));
		if (sso.AppleAccount != null)
			filters.Add(builder.Eq(player => player.AppleAccount.Id, sso.AppleAccount.Id));
		if (sso.RumbleAccount != null)
			filters.Add(builder.And(
				builder.Eq(player => player.RumbleAccount.Username, sso.RumbleAccount.Username),
				builder.Eq(player => player.RumbleAccount.Hash, sso.RumbleAccount.Hash),
				builder.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
			));
		
		if (!filters.Any())
			return Array.Empty<Player>();
		return _collection
			.Find(builder.Or(filters))
			.ToList()
			.ToArray();
	}

	public Player FromGoogle(GoogleAccount google)
	{
		List<Player> accounts = _collection
			.Find(Builders<Player>.Filter.Eq(player => player.GoogleAccount.Id, google.Id))
			.ToList();

		if (accounts.Count > 1)
			throw new PlatformException("Found more than one Google account.");
		return accounts.FirstOrDefault();
	}

	public Player FromRumble(RumbleAccount rumble)
	{
		long deleted = DeleteUnconfirmedAccounts();
		if (deleted > 0)
			Log.Local(Owner.Will, $"Deleted {deleted} old rumble accounts.");

		long usernameCount = _collection
			.CountDocuments(Builders<Player>.Filter.Or(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, rumble.Username),
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, rumble.Email)
			));

		List<Player> accounts = _collection
			.Find(
				Builders<Player>.Filter.And(
					Builders<Player>.Filter.Or(
						Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, rumble.Username),
						Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, rumble.Email)
					),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Hash, rumble.Hash)
				)
			)
			.ToList();

		return accounts.Count switch
		{
			0 when usernameCount > 0 => throw new PlatformException("Invalid password"),
			> 1 => throw new PlatformException("Found more than one Rumble account."),
			_ => accounts.FirstOrDefault()
		};
	}

	public Player UpdateHash(string username, string oldHash, string newHash) =>
		(oldHash == null
			? _collection.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, username),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetPrimed)
				),
				update: Builders<Player>.Update
					.Set(player => player.RumbleAccount.Hash, newHash)
					.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = false,
					ReturnDocument = ReturnDocument.After
				})
			: _collection.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, username),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Hash, oldHash)
				),
				update: Builders<Player>.Update.Set(player => player.RumbleAccount.Hash, newHash),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = false,
					ReturnDocument = ReturnDocument.After
				})
		) ?? throw new PlatformException("Account not found.");

	public Player AttachRumble(Player player, RumbleAccount rumble)
	{
		rumble.Status = RumbleAccount.AccountStatus.NeedsConfirmation;
		rumble.CodeExpiration = Timestamp.UnixTime + 15 * 60;
		rumble.ConfirmationCode = RumbleAccount.GenerateCode(segments: 10);
		player.RumbleAccount = rumble;
		Update(player);

		return player;
	}

	public Player AttachGoogle(Player player, GoogleAccount google)
	{
		player.GoogleAccount = google;
		Update(player);
		return player;
	}

	public long DeleteUnconfirmedAccounts() => _collection
		.UpdateMany(
			filter: Builders<Player>.Filter.And(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation),
				Builders<Player>.Filter.Lte(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
			),
			update: Builders<Player>.Update.Unset(player => player.RumbleAccount)
		).ModifiedCount;

	public Player UseConfirmationCode(string id, string code)
	{
		Player output = _collection
			.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.Id, id),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.ConfirmationCode, code),
					Builders<Player>.Filter.Gt(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
				),
				update: Builders<Player>.Update
					.Set(player => player.RumbleAccount.CodeExpiration, default)
					.Set(player => player.RumbleAccount.ConfirmationCode, null)
					.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = false,
					ReturnDocument = ReturnDocument.After
				}
			);
		
		return output;
	}

	public Player BeginReset(string email) =>_collection
		.FindOneAndUpdate(
			filter: Builders<Player>.Filter.And(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, email)
			),
			update: Builders<Player>.Update
				.Set(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime * 15 * 60)
				.Set(player => player.RumbleAccount.ConfirmationCode, RumbleAccount.GenerateCode(segments: 2))
				.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetRequested),
			options: new FindOneAndUpdateOptions<Player>
			{
				IsUpsert = false,
				ReturnDocument = ReturnDocument.After
			}
		) ?? throw new PlatformException("Account not found.");
	
	public Player CompleteReset(string username, string code)
	{
		return _collection.FindOneAndUpdate(
			filter: Builders<Player>.Filter.And(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, username),
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.ConfirmationCode, code),
				Builders<Player>.Filter.Gt(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
			),
			update: Builders<Player>.Update
				.Unset(player => player.RumbleAccount.ConfirmationCode)
				.Unset(player => player.RumbleAccount.CodeExpiration)
				.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetPrimed),
			options: new FindOneAndUpdateOptions<Player>
			{
				IsUpsert = false,
				ReturnDocument = ReturnDocument.After
			}) ?? throw new PlatformException("Account not found.");
	}

	public string SetLinkCode(string[] ids)
	{
		string[] overrides = _collection
			.Find(Builders<Player>.Filter.In(player => player.ParentId, ids.Where(id => !string.IsNullOrWhiteSpace(id))))
			.Project(Builders<Player>.Projection.Expression(player => player.Id))
			.ToList()
			.ToArray();
		
		string code = Guid.NewGuid().ToString();
		_collection
			.UpdateMany(
				filter: Builders<Player>.Filter.In(player => player.Id, ids.Union(overrides)),
				update: Builders<Player>.Update
					.Set(player => player.LinkCode, code)
					.Set(player => player.LinkExpiration, Timestamp.UnixTime + 15 * 60)
			);
		return code;
	}

	public Player LinkAccounts(string accountId)
	{
		Player player = Find(accountId)
			?? throw new PlatformException("Account not found.");

		if (string.IsNullOrEmpty(player.LinkCode))
			throw new PlatformException("No link code found.");

		if (player.LinkExpiration <= Timestamp.UnixTime)
			throw new PlatformException("Link code is expired.");
		
		List<Player> others = _collection
			.Find(Builders<Player>.Filter.And(
				Builders<Player>.Filter.Ne(account => account.Id, player.Id),
				Builders<Player>.Filter.Or(
					Builders<Player>.Filter.Eq(account => account.LinkCode, player.LinkCode),
					Builders<Player>.Filter.Eq(account => account.ParentId, player.Id)
				)
			))
			.ToList();

		if (!others.Any())
			throw new PlatformException("No other accounts found.");

		List<GoogleAccount> googles = others
			.Select(other => other.GoogleAccount)
			.Union(new [] { player.GoogleAccount })
			.Where(account => account != null)
			.ToList();
		List<AppleAccount> apples = others
			.Select(other => other.AppleAccount)
			.Union(new [] { player.AppleAccount })
			.Where(account => account != null)
			.ToList();
		List<RumbleAccount> rumbles = others
			.Select(other => other.RumbleAccount)
			.Union(new [] { player.RumbleAccount })
			.Where(account => account != null && account.Status.HasFlag(RumbleAccount.AccountStatus.Confirmed)) // TODO: Getter property for this
			.ToList();


		if (googles.Count > 1)
			throw new PlatformException("Multiple Google accounts found.");
		if (apples.Count > 1)
			throw new PlatformException("Multiple Apple accounts found.");
		if (rumbles.Count > 1)
			throw new PlatformException("Multiple Rumble accounts found.");
		
		player.GoogleAccount = googles.FirstOrDefault();
		player.AppleAccount = apples.FirstOrDefault();
		player.RumbleAccount = rumbles.FirstOrDefault();

		Update(player);
		
		_collection
			.UpdateMany(
				filter: Builders<Player>.Filter.In(player => player.Id, others.Select(other => other.Id)),
				update: Builders<Player>.Update
					.Set(other => other.ParentId, player.Id)
					.Unset(other => other.GoogleAccount)
					.Unset(other => other.AppleAccount)
					.Unset(other => other.RumbleAccount)
					.Unset(other => other.LinkCode)
			);
		
		return player;
	}

	public long RemoveExpiredLinkCodes() => _collection.UpdateMany(
			filter: Builders<Player>.Filter.Lte(player => player.LinkExpiration, Timestamp.UnixTime),
			update: Builders<Player>.Update
				.Unset(player => player.LinkCode)
				.Unset(player => player.LinkExpiration)
		).ModifiedCount;

	public Player[] Search(params string[] terms)
	{
		List<Player> output = new List<Player>();

		foreach (string term in terms)
		{
			if (term.CanBeMongoId())
			{
				output = _collection
					.Find(Builders<Player>.Filter.Eq(player => player.Id, term))
					.ToList();
				if (output.Any())
					return output.ToArray();
			}

			output.AddRange(_collection.Find(
					filter: player =>
						player.Id.ToLower().Contains(term)
						|| player.Screenname.ToLower().Contains(term)
						|| player.Device.InstallId.ToLower().Contains(term)
						|| player.ParentId.ToLower().Contains(term)
						|| player.RumbleAccount.Email.Contains(term)
						|| player.RumbleAccount.Username.Contains(term)
						|| player.GoogleAccount.Email.Contains(term)
						|| player.GoogleAccount.Name.Contains(term)
				)
				.Limit(100)
				.ToList()
			);
		}
		
		
		Player.WeighSearchResults(terms, ref output);
		
		return output.ToArray();
	}
}






































