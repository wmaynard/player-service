using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using PlayerService.Controllers;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using PlayerService.Models.Login;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Services;

public class PlayerAccountService : PlatformMongoService<Player>
{
	public const long CODE_EXPIRATION = 15 * 60; // 15 minutes

	private readonly AccountService _accountService;
	private readonly ApiService _apiService;
	private readonly DynamicConfig _config;
	private readonly NameGeneratorService _nameGenerator;
	private readonly DiscriminatorService _discriminatorService;
	private readonly LockoutService _lockoutService;

	public PlayerAccountService(ApiService api, DynamicConfig config, NameGeneratorService nameGenerator, AccountService account, DiscriminatorService discriminator, LockoutService lockout) : base("players")
	{
		_apiService = api;
		_config = config;
		_nameGenerator = nameGenerator;
		_accountService = account;
		_discriminatorService = discriminator;
		_lockoutService = lockout;
	}  

	public Player Find(string accountId) => FindOne(player => player.Id == accountId);

	/// <summary>
	/// Returns true if there are any documents in the database matching the provided device installId.  This is used to circumvent
	/// the upsert behavior in FromDevice for maintenance mode.
	/// </summary>
	/// <param name="device">The device information to look up.</param>
	/// <returns>True if the device already exists.</returns>
	public bool InstallIdExists(DeviceInfo device) => device?.InstallId != null && _collection
		.CountDocuments(Builders<Player>.Filter.Eq(player => player.Device.InstallId, device.InstallId)) > 0;

	public List<Player> DirectoryLookup(params string[] accountIds) => _collection
		.Find(Builders<Player>.Filter.In(player => player.Id, accountIds))
		.ToList();

	/// <summary>
	/// When using SSO, this update gets called, which unifies all screennames.  New devices generate new screennames and need to be updated
	/// to reflect the account they're linked to.
	/// </summary>
	/// <param name="screenname"></param>
	/// <param name="accountId"></param>
	/// <param name="fromAdmin">If true, this will increase the component version number; rejecting any other
	/// immediately-following component writes.</param>
	/// <returns></returns>
	public int SyncScreenname(string screenname, string accountId, bool fromAdmin = false)
	{
		// Change the screenname in the discriminator data first.  If this fails, the screenname change
		// should also fail, and the exception will cause this to exit early.
		_discriminatorService.Assign(accountId, screenname);
		
		int affected = (int)_collection.UpdateMany(
			filter: player => player.Id == accountId || player.ParentId == accountId,
			update: Builders<Player>.Update.Set(player => player.Screenname, screenname)
		).ModifiedCount;

		// NOTE: This is a kluge; the game server may overwrite this component if their session is active.
		// TD-14516: Screenname changes from Portal do not affect the account screen in-game.
		_accountService.SetScreenname(accountId, screenname, fromAdmin);

		return affected;
	}

	public Player FromDevice(DeviceInfo device, bool isUpsert = false)
	{
		// Before 2023.05.05, a potential security hole was the fact that hitting login with a random ID theoretically
		// could have resulted in accessing another player's account.  While very unlikely with a 32-digit hex GUID,
		// it's certainly still possible to guess an installId.  To combat this, we calculate an initial hash.
		// Device information will change over time, but the initial hash acts as a second secret that can continue to
		// persist.
		// The idea here is that 
		Player stored = null;
		UpdateDefinition<Player> update = Builders<Player>.Update
			.Set(player => player.Device.ClientVersion, device.ClientVersion)
			.Set(player => player.Device.DataVersion, device.DataVersion)
			.Set(player => player.Device.Language, device.Language)
			.Set(player => player.Device.OperatingSystem, device.OperatingSystem)
			.Set(player => player.Device.Type, device.Type)
			.Set(player => player.LastLogin, Timestamp.UnixTime)
			.SetOnInsert(player => player.Screenname, _nameGenerator.Next);

		stored = _collection
			.Find(Builders<Player>.Filter.Eq(player => player.Device.InstallId, device.InstallId))
			.Limit(1)
			.FirstOrDefault();
		
		device.Compare(stored?.Device, out bool devicesIdentical, out bool keysAuthorized);
		
		// One of these conditions must be true; no record exists on the database, keysAuthorized is always true.
		if (!(devicesIdentical || keysAuthorized))
			throw new DeviceMismatchException();

		if (!string.IsNullOrWhiteSpace(device.PrivateKey))
		{
			// If the devices are identical, our DB record exists, but the keys are wrong.  This could be an attack.
			if (devicesIdentical && !keysAuthorized)
				throw new DeviceMismatchException();

			stored = _collection.FindOneAndUpdate(
				filter: Builders<Player>.Filter.Eq(player => player.Device.InstallId, device.InstallId),
				update: string.IsNullOrWhiteSpace(stored?.Device.ConfirmedPrivateKey)
					// The stored record does not have a confirmed key yet; lock it in now.  This should happen exactly once per record.
					? update.Set(player => player.Device.ConfirmedPrivateKey, device.PrivateKey) 
					: update,
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = true,
					ReturnDocument = ReturnDocument.After
				}
			);
		}
		else // The client doesn't know its initial hash; only look up the record by installId.
		{
			
			stored = _collection.FindOneAndUpdate(
				filter: Builders<Player>.Filter.Eq(player => player.Device.InstallId, device.InstallId),
				update: update,
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = true,
					ReturnDocument = ReturnDocument.After
				}
			);
			
			stored?.Device?.CalculatePrivateKey();
		}
		
		if (stored?.ParentId != null)
			stored.Parent = _collection
				.Find(player => player.Id == stored.ParentId)
				.FirstOrDefault();

		return stored?.Parent ?? stored;
	}

	public Player[] FromSso(SsoData sso, string ipAddress)
	{
		if (sso == null)
			return Array.Empty<Player>();

		bool useGoogle = sso.GoogleAccount != null;
		bool useApple = sso.AppleAccount != null;
		bool usePlarium = sso.PlariumAccount != null;
		bool useRumble = sso.RumbleAccount != null;
		
		FilterDefinitionBuilder<Player> builder = Builders<Player>.Filter;

		List<FilterDefinition<Player>> filters = new List<FilterDefinition<Player>>();

		if (useGoogle)
			filters.Add(builder.Eq(player => player.GoogleAccount.Id, sso.GoogleAccount.Id));
		if (useApple)
			filters.Add(builder.Eq(player => player.AppleAccount.Id, sso.AppleAccount.Id));
		if (usePlarium)
			filters.Add(builder.Eq(player => player.PlariumAccount.Id, sso.PlariumAccount.Id));
		if (useRumble && _lockoutService.EnsureNotLockedOut(sso.RumbleAccount.Username, ipAddress))
			filters.Add(builder.And(
				builder.Eq(player => player.RumbleAccount.Username, sso.RumbleAccount.Username),
				builder.Eq(player => player.RumbleAccount.Hash, sso.RumbleAccount.Hash),
				builder.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
			));
		
		if (!filters.Any())
			return Array.Empty<Player>();
		
		Player[] output = _collection
			.Find(builder.Or(filters))
			.ToList()
			.ToArray();

		if (useGoogle && !output.Any(player => player.GoogleAccount != null))
			throw new GoogleUnlinkedException();
		if (useApple && !output.Any(player => player.AppleAccount != null))
			throw new AppleUnlinkedException();
		if (usePlarium && !output.Any(player => player.PlariumAccount != null))
			throw new PlariumUnlinkedException();
		if (useRumble && !output.Any(player => player.RumbleAccount != null))
			throw DiagnoseEmailPasswordLogin(sso.RumbleAccount.Email, sso.RumbleAccount.Hash);
		
		return output;
	}

	public Player FromApple(AppleAccount apple)
	{
		List<Player> accounts = _collection
			.Find(Builders<Player>.Filter.Eq(player => player.AppleAccount.Id, apple.Id))
			.ToList();
		
		return accounts.Count <= 1
			? accounts.FirstOrDefault()
			: throw new RecordsFoundException(1, accounts.Count);
	}

	public Player FromGoogle(GoogleAccount google)
	{
		List<Player> accounts = _collection
			.Find(Builders<Player>.Filter.Eq(player => player.GoogleAccount.Id, google.Id))
			.ToList();
		
		return accounts.Count <= 1
			? accounts.FirstOrDefault()
			: throw new RecordsFoundException(1, accounts.Count);
	}

	public Player FromPlarium(PlariumAccount plarium)
	{
		List<Player> accounts = _collection
            .Find(Builders<Player>.Filter.Eq(player => player.PlariumAccount.Id, plarium.Id))
            .ToList();

		return accounts.Count <= 1
			? accounts.FirstOrDefault()
			: throw new RecordsFoundException(1, accounts.Count);
	}

	public Player FromRumble(RumbleAccount rumble, bool mustExist = true, bool mustNotExist = false)
	{
		long deleted = DeleteUnconfirmedAccounts();
		if (deleted > 0)
			Log.Local(Owner.Will, $"Deleted {deleted} old rumble accounts.");

		long usernameCount = _collection
			.CountDocuments(Builders<Player>.Filter.And(
				Builders<Player>.Filter.Or(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, rumble.Username),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, rumble.Email)
				),
				Builders<Player>.Filter.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
			));

		if (mustNotExist && usernameCount > 0)
			throw new AccountOwnershipException("Rumble", "The username or email is already in use.");

		List<Player> accounts = _collection
			.Find(
				Builders<Player>.Filter.And(
					Builders<Player>.Filter.Or(
						Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, rumble.Username),
						Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, rumble.Email)
					),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Hash, rumble.Hash),
					Builders<Player>.Filter.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed))
			)
			.ToList();

		return accounts.Count switch
		{
			0 when usernameCount > 0 && mustExist => throw DiagnoseEmailPasswordLogin(rumble.Email, rumble.Hash),
			> 1 => throw new RecordsFoundException(1, accounts.Count),
			_ => accounts.FirstOrDefault()
		};
	}

	public Player UpdateHash(string username, string oldHash, string newHash, string callingAccountId)
	{
		UpdateDefinition<Player> update = Builders<Player>.Update.Set(player => player.RumbleAccount.Hash, newHash);
		FilterDefinition<Player> filter;

		FilterDefinition<Player> regex = Builders<Player>.Filter.Regex(player => player.RumbleAccount.Username, new BsonRegularExpression($"^{Regex.Escape(username)}$", "i"));

		// The previous hash is known; the filter can use the old hash and we don't need to worry about account status.
		if (!string.IsNullOrWhiteSpace(oldHash))
			filter = Builders<Player>.Filter.And(
				regex,
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Hash, oldHash)
			);
		else
		{
			// Since the previous hash is unknown, our filter must be primed to accept the new hash.
			filter = Builders<Player>.Filter.And(
				regex,
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetPrimed)
			);
			// If we the incoming account ID is specified, we can add it here to the confirmed IDs.
			// The only way to get to this step without the old hash is to go through 2FA already, so we can skip it.
			if (!string.IsNullOrWhiteSpace(callingAccountId))
				update = update.AddToSet(player => player.RumbleAccount.ConfirmedIds, callingAccountId);
			update = update.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed);
		}
		
		return _collection.FindOneAndUpdate(
			filter: filter,
			update: update,
			options: new FindOneAndUpdateOptions<Player>
			{
				IsUpsert = false,
				ReturnDocument = ReturnDocument.After
			}
		) ?? throw new RecordNotFoundException(CollectionName, "Account not found.");
	}

	public Player AttachRumble(Player player, RumbleAccount rumble)
	{
		rumble.Status = RumbleAccount.AccountStatus.NeedsConfirmation;
		rumble.CodeExpiration = Timestamp.UnixTime + CODE_EXPIRATION;
		rumble.ConfirmationCode = RumbleAccount.GenerateCode(segments: 10);
		player.RumbleAccount = rumble;
		Update(player);

		_apiService
			.Request("/dmz/player/account/confirmation")
			.AddAuthorization(_config.AdminToken)
			.SetPayload(new RumbleJson
			{
				{ "email", rumble.Email },
				{ "accountId", player.Id },
				{ "code", rumble.ConfirmationCode },
				{ "expiration", rumble.CodeExpiration }
			})
			.OnFailure(response =>
			{
				player.RumbleAccount.EmailBanned = true;
				Log.Error(Owner.Will, "Unable to send Rumble account confirmation email.", new
				{
					Response = response
				});
			})
			.Post();

		GenerateToken(player);
		return player;
	}

	public void SendLoginNotification(Player player, string email) => _apiService
		.Request("/dmz/player/account/notification")
		.AddAuthorization(_config.AdminToken)
		.SetPayload(new RumbleJson
		{
			{ "email", email },
			{ "device", player.Device.Type }
		})
		.OnFailure(response => Log.Error(Owner.Will, "Unable to send Rumble login account notification.", new
		{
			Response = response
		}))
		.Post();

	public Player AttachApple(Player player, AppleAccount apple)
	{
		player.AppleAccount = apple;
		Update(player);
		GenerateToken(player);
		return player;
	}

	public Player AttachGoogle(Player player, GoogleAccount google)
	{
		player.GoogleAccount = google;
		Update(player);
		GenerateToken(player);
		return player;
	}

	public Player AttachPlarium(Player player, PlariumAccount plarium)
	{
		player.PlariumAccount = plarium;
		Update(player);
		GenerateToken(player);
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
					Builders<Player>.Filter.Lte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation),
					Builders<Player>.Filter.Gt(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
				),
				update: Builders<Player>.Update
					.Set(player => player.RumbleAccount.CodeExpiration, default)
					.Set(player => player.RumbleAccount.ConfirmationCode, null)
					.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
					.AddToSet(player => player.RumbleAccount.ConfirmedIds, id),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = false,
					ReturnDocument = ReturnDocument.After
				}
			);
		
		if (output != null)
			_apiService
				.Request("/dmz/player/account/welcome")
				.AddAuthorization(_config.AdminToken)
				.SetPayload(new RumbleJson
				{
					{ "email", output.RumbleAccount?.Email }
				})
				.OnFailure(response => Log.Error(Owner.Will, "Unable to send welcome email.", data: new
				{
					Player = output,
					Response = response
				}))
				.Post();

		return output;
	}

	public Player UseTwoFactorCode(string id, string code)
	{
		string linkCode = _collection
			.Find(Builders<Player>.Filter.Eq(player => player.Id, id))
			.Project(Builders<Player>.Projection.Expression(player => player.LinkCode))
			.FirstOrDefault();

		Player output = _collection
			.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.LinkCode, linkCode),
					Builders<Player>.Filter.Ne(player => player.RumbleAccount, null),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.ConfirmationCode, code),
					Builders<Player>.Filter.Gt(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
				),
				update: Builders<Player>.Update
					.AddToSet(player => player.RumbleAccount.ConfirmedIds, id)
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

	public Player SendTwoFactorNotification(string email)
	{
		Player output = _collection
			.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, email),
					Builders<Player>.Filter.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
				),
				update: Builders<Player>.Update
					.Set(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime + CODE_EXPIRATION)
					.Set(player => player.RumbleAccount.ConfirmationCode, RumbleAccount.GenerateCode(segments: 2))
					.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsTwoFactor),
				options: new FindOneAndUpdateOptions<Player>
				{
					ReturnDocument = ReturnDocument.After
				}
			);

		_apiService
			.Request("/dmz/player/account/2fa")
			.AddAuthorization(_config.AdminToken)
			.SetPayload(new RumbleJson
			{
				{ "email", email },
				{ "code", output.RumbleAccount?.ConfirmationCode },
				{ "expiration", output.RumbleAccount?.CodeExpiration }
			})
			.OnFailure(response => Log.Error(Owner.Will, "Unable to send 2FA code.", data: new
			{
				Player = output,
				Email = email
			}))
			.Post();

		return output;
	}

	public long ClearUnconfirmedAccounts(RumbleAccount rumble) => _collection
		.UpdateMany(
			filter: Builders<Player>.Filter.And( 
				Builders<Player>.Filter.Or(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, rumble.Email),
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, rumble.Username)
				),
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.NeedsConfirmation)
			),
			update: Builders<Player>.Update.Unset(player => player.RumbleAccount)
		).ModifiedCount;

	public Player BeginReset(string email)
	{
		Player output = _collection
			.FindOneAndUpdate(
				filter: Builders<Player>.Filter.And(
					Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, email),
					Builders<Player>.Filter.Gte(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.Confirmed)
				),
				update: Builders<Player>.Update
					.Set(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime + CODE_EXPIRATION)
					.Set(player => player.RumbleAccount.ConfirmationCode, RumbleAccount.GenerateCode(segments: 2))
					.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetRequested),
				options: new FindOneAndUpdateOptions<Player>
				{
					IsUpsert = false,
					ReturnDocument = ReturnDocument.After
				}
			) ?? throw new RumbleUnlinkedException(email);

		_apiService
			.Request("/dmz/player/account/reset")
			.AddAuthorization(_config.AdminToken)
			.SetPayload(new RumbleJson
			{
				{ "email", email },
				{ "accountId", output.Id },
				{ "code", output.RumbleAccount?.ConfirmationCode },
				{ "expiration", output.RumbleAccount?.CodeExpiration }
			})
			.OnFailure(response => Log.Error(Owner.Will, "Unable to send password reset email.", data: new
			{
				Player = output,
				Response = response
			}))
			.Post();

		return output;
	}
	
	public Player CompleteReset(string username, string code, string accountId = null)
	{
		UpdateDefinition<Player> update = Builders<Player>.Update
			.Unset(player => player.RumbleAccount.ConfirmationCode)
			.Unset(player => player.RumbleAccount.CodeExpiration)
			.Set(player => player.RumbleAccount.Status, RumbleAccount.AccountStatus.ResetPrimed);

		if (accountId != null)
			update = update.AddToSet(player => player.RumbleAccount.ConfirmedIds, accountId);
		
		return _collection.FindOneAndUpdate(
			filter: Builders<Player>.Filter.And(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, username),
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.ConfirmationCode, code),
				Builders<Player>.Filter.Gt(player => player.RumbleAccount.CodeExpiration, Timestamp.UnixTime)
			),
			update: update,
			options: new FindOneAndUpdateOptions<Player>
			{
				IsUpsert = false,
				ReturnDocument = ReturnDocument.After
			}) ?? throw DiagnoseEmailPasswordLogin(username, null, code);
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
					.Set(player => player.LinkExpiration, Timestamp.UnixTime + CODE_EXPIRATION)
			);
		return code;
	}

	/// <summary>
	/// Uses a code on the database to complete account links; responsible for tying new accounts to SSO.
	/// </summary>
	/// <param name="accountId"></param>
	/// <returns></returns>
	/// <exception cref="RecordNotFoundException"></exception>
	/// <exception cref="WindowExpiredException"></exception>
	/// <exception cref="RecordsFoundException"></exception>
	public Player LinkAccounts(string accountId)
	{
		Player player = Find(accountId)
			?? throw new RecordNotFoundException(CollectionName, "No player account found with specified ID.", data: new RumbleJson
			{
				{ "accountId", accountId }
			});

		if (string.IsNullOrEmpty(player.LinkCode))
			throw new RecordNotFoundException(CollectionName, "No matching link code found.", data: new RumbleJson
			{
				{ "accountId", accountId }
			});

		if (player.LinkExpiration <= Timestamp.UnixTime)
			throw new WindowExpiredException("Link code is expired.");
		
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
			throw new RecordNotFoundException(CollectionName, "No other accounts found to link.", data: new RumbleJson
			{
				{ "accountId", accountId }
			});

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
		List<PlariumAccount> plariums = others
            .Select(other => other.PlariumAccount)
            .Union(new [] { player.PlariumAccount })
            .Where(account => account != null)
            .ToList();
		List<RumbleAccount> rumbles = others
			.Select(other => other.RumbleAccount)
			.Union(new [] { player.RumbleAccount })
			.Where(account => account != null && account.Status.HasFlag(RumbleAccount.AccountStatus.Confirmed)) // TODO: Getter property for this
			.ToList();


		if (googles.Count > 1)
			throw new RecordsFoundException(1, googles.Count, "Multiple Google accounts found.");
		if (apples.Count > 1)
			throw new RecordsFoundException(1, apples.Count, "Multiple Apple accounts found.");
		if (plariums.Count > 1)
			throw new RecordsFoundException(1, plariums.Count, "Multiple Plarium accounts found.");
		if (rumbles.Count > 1)
			throw new RecordsFoundException(1, rumbles.Count, "Multiple Rumble accounts found.");
		
		player.GoogleAccount = googles.FirstOrDefault();
		player.AppleAccount = apples.FirstOrDefault();
		player.PlariumAccount = plariums.FirstOrDefault();
		player.RumbleAccount = rumbles.FirstOrDefault();

		Update(player);
		
		_collection
			.UpdateMany(
				filter: Builders<Player>.Filter.In(player => player.Id, others.Select(other => other.Id)),
				update: Builders<Player>.Update
					.Set(other => other.ParentId, player.Id)
					.Unset(other => other.GoogleAccount)
					.Unset(other => other.AppleAccount)
					.Unset(other => other.PlariumAccount)
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

			BsonRegularExpression regex = new BsonRegularExpression(new Regex(term, RegexOptions.IgnoreCase));
			output.AddRange(_collection.Find(Builders<Player>.Filter.Or(
					Builders<Player>.Filter.Regex(player => player.Id, regex),
					Builders<Player>.Filter.Regex(player => player.Device.InstallId, regex),
					Builders<Player>.Filter.Regex(player => player.ParentId, regex),
					Builders<Player>.Filter.Regex(player => player.RumbleAccount.Email, regex),
					Builders<Player>.Filter.Regex(player => player.RumbleAccount.Username, regex),
					Builders<Player>.Filter.Regex(player => player.GoogleAccount.Email, regex),
					Builders<Player>.Filter.Regex(player => player.GoogleAccount.Name, regex),
					Builders<Player>.Filter.Regex(player => player.AppleAccount.Email, regex),
					Builders<Player>.Filter.Regex(player => player.PlariumAccount.Email, regex),
					Builders<Player>.Filter.Regex(player => player.Screenname, regex)
				))
				.Limit(100)
				.ToList()
			);
		}

		foreach (Player parent in output.Where(player => string.IsNullOrWhiteSpace(player.ParentId)))
			parent.Children = output
				.Where(player => player.ParentId == parent.Id)
				.Select(player => player.Id)
				.ToList();

		output = output
			.Where(player => string.IsNullOrWhiteSpace(player.ParentId))
			.DistinctBy(player => player.AccountId)
			.ToList();

		Player.WeighSearchResults(terms, ref output);
		
		return output.ToArray();
	}
	
	public Player FromToken(TokenInfo token) => _collection
		.Find(Builders<Player>.Filter.Eq(player => player.Id, token?.AccountId))
		.FirstOrDefault()
		?? throw new RecordNotFoundException(CollectionName, "Account not found.");

	public long DeleteRumbleAccount(string email) => _collection
		.UpdateMany(
			filter: Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, email),
			update: Builders<Player>.Update.Unset(player => player.RumbleAccount)
		).ModifiedCount;
	
	public long DeleteRumbleAccountById(string playerId) => _collection
		.UpdateOne(
			filter: Builders<Player>.Filter.Eq(player => player.Id, playerId),
			update: Builders<Player>.Update.Unset(player => player.RumbleAccount)
		).ModifiedCount;

	public long DeleteAllRumbleAccounts() => _collection
		.UpdateMany(
			filter: player => true,
			update: Builders<Player>.Update.Unset(player => player.RumbleAccount)
		).ModifiedCount;
	
	public long DeleteAppleAccount(string email) => _collection
		.UpdateMany(
			filter: Builders<Player>.Filter.Eq(player => player.AppleAccount.Email, email),
			update: Builders<Player>.Update.Unset(player => player.AppleAccount)
		).ModifiedCount;
	
	public long DeleteAppleAccountById(string playerId) => _collection
		.UpdateOne(
			filter: Builders<Player>.Filter.Eq(player => player.Id, playerId),
			update: Builders<Player>.Update.Unset(player => player.AppleAccount)
		).ModifiedCount;
	
	public long DeleteAllAppleAccounts() => _collection
		.UpdateMany(
			filter: player => true,
			update: Builders<Player>.Update.Unset(player => player.AppleAccount)
		).ModifiedCount;

	public long DeleteGoogleAccount(string email) => _collection
		.UpdateMany(
			filter: Builders<Player>.Filter.Eq(player => player.GoogleAccount.Email, email),
			update: Builders<Player>.Update.Unset(player => player.GoogleAccount)
		).ModifiedCount;
	
	public long DeleteGoogleAccountById(string playerId) => _collection
		.UpdateOne(
			filter: Builders<Player>.Filter.Eq(player => player.Id, playerId),
			update: Builders<Player>.Update.Unset(player => player.GoogleAccount)
		).ModifiedCount;
	
	public long DeleteAllGoogleAccounts() => _collection
		.UpdateMany(
			filter: player => true,
			update: Builders<Player>.Update.Unset(player => player.GoogleAccount)
		).ModifiedCount;
	
	public long DeletePlariumAccount(string email) => _collection
        .UpdateMany(
            filter: Builders<Player>.Filter.Eq(player => player.PlariumAccount.Email, email),
            update: Builders<Player>.Update.Unset(player => player.PlariumAccount)
        ).ModifiedCount;
	
	public long DeletePlariumAccountById(string playerId) => _collection
       .UpdateOne(
			filter: Builders<Player>.Filter.Eq(player => player.Id, playerId),
			update: Builders<Player>.Update.Unset(player => player.PlariumAccount)
		).ModifiedCount;

	public long DeleteAllPlariumAccounts(params string[] installIds)
	{
		if (PlatformEnvironment.IsProd)
			return _collection
				.UpdateMany(
					filter: player => true,
					update: Builders<Player>.Update.Unset(player => player.PlariumAccount)
				).ModifiedCount;

		FilterDefinition<Player> filter = Builders<Player>.Filter.Or(
			Builders<Player>.Filter.Exists(player => player.PlariumAccount.Email, true),
			Builders<Player>.Filter.In(player => player.Device.InstallId, installIds)
		);

		string[] matches = _collection
			.Find(filter)
			.Project(player => player.Id)
			.ToList()
			.ToArray();

		
		long output = _collection
			.UpdateMany(
				filter: filter,
				update: Builders<Player>.Update
					.Unset(player => player.PlariumAccount)
					.Set(player => player.Device.InstallId, $"plariumDeleted_{Guid.NewGuid().ToString()}")
			).ModifiedCount;

		_collection.UpdateMany(
			filter: Builders<Player>.Filter.In(player => player.ParentId, matches),
			update: Builders<Player>.Update.Unset(player => player.ParentId)
		);

		return output;
	}

	private PlatformException DiagnoseEmailPasswordLogin(string email, string hash, string code = null)
	{
		RumbleAccount[] accounts = GetRumbleAccountsByEmail(email);

		int confirmed = accounts.Count(rumble => rumble.Status == RumbleAccount.AccountStatus.Confirmed);

		bool waitingOnConfirmation = confirmed == 0 && accounts.Any(rumble => rumble.Status == RumbleAccount.AccountStatus.NeedsConfirmation && rumble.CodeExpiration > Timestamp.UnixTime);
		bool allExpired = confirmed == 0 && !accounts.Any(rumble => rumble.Status == RumbleAccount.AccountStatus.NeedsConfirmation && rumble.CodeExpiration > Timestamp.UnixTime);
		bool codeInvalid = !allExpired && code != null && !accounts.Any(rumble => rumble.ConfirmationCode == code);

		PlatformException output = confirmed switch
		{
			0 when accounts.Length == 0 => new RumbleUnlinkedException(email),
			0 when waitingOnConfirmation => new RumbleNotConfirmedException(email),
			0 when allExpired => new ConfirmationCodeExpiredException(email),
			0 => new RumbleUnlinkedException(email),
			1 when codeInvalid => new CodeInvalidException(email),
			1 => new InvalidPasswordException(email),
			_ => new RecordsFoundException(0, 1, confirmed, "Found more than one confirmed Rumble account for an email address!")
		};

		return output;
	}

	private RumbleAccount[] GetRumbleAccountsByEmail(string email) => _collection
		.Find(Builders<Player>.Filter.Or(
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Email, email),
				Builders<Player>.Filter.Eq(player => player.RumbleAccount.Username, email)
		))
		.Project(Builders<Player>.Projection.Expression(player => player.RumbleAccount))
		.Limit(1_000)
		.SortByDescending(player => player.RumbleAccount.Status)
		.ThenByDescending(player => player.RumbleAccount.CodeExpiration)
		.ToList()
		.ToArray();

	/// <summary>
	/// Manually links two accounts together.
	/// </summary>
	/// <param name="childId"></param>
	/// <param name="parentId"></param>
	/// <param name="force"></param>
	/// <returns></returns>
	public Player LinkPlayerAccounts(string childId, string parentId, bool force, TokenInfo token)
	{
		Player child = Find(childId);
		Player parent = Find(parentId);

		if (!string.IsNullOrWhiteSpace(child.ParentId))
			if (force)
				Log.Warn(Owner.Will, "Account was previously linked to another account, but the force flag allows a link override.", data: new
				{
					Child = child,
					Parent = parent,
					Token = token
				});
			else
				throw new PlatformException("Account is already linked to another account.");

		if (parent.GoogleAccount != null || parent.RumbleAccount != null || parent.AppleAccount != null)
			if (force)
				Log.Warn(Owner.Will, "Parent account has SSO, but the force flag allows a link override.", data: new
				{
					Child = child,
					Parent = parent,
					Token = token
				});
			else
				throw new PlatformException("Parent account has SSO; no link can be made without the force flag.");

		// Link the two accounts together.
		parent.Children ??= new List<string>();
		parent.Children.Add(child.Id);
		child.ParentId = parent.AccountId;
		
		Update(parent);
		Update(child);
		
		return child;
	}

	public override void Update(Player model, bool createIfNotFound = false) => _collection.UpdateOne(
		filter: Builders<Player>.Filter.Eq(player => player.Id, model.Id),
		update: Builders<Player>.Update
			.Set(player => player.AppleAccount, model.AppleAccount)
			.Set(player => player.GoogleAccount, model.GoogleAccount)
			.Set(player => player.PlariumAccount, model.PlariumAccount)
			.Set(player => player.RumbleAccount, model.RumbleAccount)
			.Set(player => player.CreatedOn, model.CreatedOn)
			.Set(player => player.LastLogin, model.LastLogin)
			.Set(player => player.LinkCode, model.LinkCode)
			.Set(player => player.LocationData, model.LocationData)
			.Set(player => player.Device.ClientVersion, model.Device.ClientVersion)
			.Set(player => player.Device.DataVersion, model.Device.DataVersion)
			.Set(player => player.Device.Language, model.Device.Language)
			.Set(player => player.Device.OperatingSystem, model.Device.OperatingSystem)
			.Set(player => player.Device.Type, model.Device.Type)
			.Set(player => player.LastLogin, Timestamp.UnixTime)
			.Set(player => player.Screenname, model.Screenname),
		options: new UpdateOptions
		{
			IsUpsert = createIfNotFound
		}
	);

	public string GenerateToken(string accountId) => GenerateToken(Find(accountId));

	public string GenerateToken(Player player) => player.Token ??= _apiService.GenerateToken(
		player.AccountId,
		player.Screenname,
		email: player.Email,
		discriminator: _discriminatorService.Lookup(player),
		audiences: AccountController.TOKEN_AUDIENCE
	);
	public long RenamePlariumAccountLogins()
	{
		List<Player> players = _collection.Find(Builders<Player>.Filter.And(
			Builders<Player>.Filter.Exists(player => player.PlariumAccount.Email, false),
			Builders<Player>.Filter.Exists(player => player.PlariumAccount.Login)
		)).ToList();

		foreach (Player player in players)
		{
			player.PlariumAccount.Email = player.PlariumAccount.Login;
			Update(player);
		}
		
		return players.Count;
	}

	// Context: https://rumblegames.slack.com/archives/C05B8SDC8DV/p1691102158578829
	// This fully severs account access when a GDPR request 
	public override long ProcessGdprRequest(TokenInfo token, string dummyText)
	{
		long ct = _collection.CountDocuments(Builders<Player>.Filter.Or(
			Builders<Player>.Filter.Eq(player => player.Id, token.AccountId),
			Builders<Player>.Filter.Eq(player => player.ParentId, token.AccountId)
		));
		long output = _collection
			.UpdateMany(filter: Builders<Player>.Filter.Or(
					Builders<Player>.Filter.Eq(player => player.Id, token.AccountId),
					Builders<Player>.Filter.Eq(player => player.ParentId, token.AccountId)
				),
				update: Builders<Player>.Update
					.Set(player => player.AppleAccount, null)
					.Set(player => player.GoogleAccount, null)
					.Set(player => player.RumbleAccount, null)
					.Set(player => player.PlariumAccount, null)
					.Set(player => player.LocationData, null)
					.Set(player => player.Screenname, dummyText)
					.Set(player => player.Device.Language, dummyText)
					.Set(player => player.Device.OperatingSystem, dummyText)
					.Set(player => player.Device.InstallId, dummyText)
					.Set(player => player.ParentId, null)
			).ModifiedCount;
		return output;
	}
}