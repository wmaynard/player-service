using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Google.Apis.Auth.AspNetCore3;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Models.Responses;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using PlayerService.Utilities;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;

namespace PlayerService.Controllers
{
	[ApiController, Route("player/v2"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
#pragma warning disable CS0649
		private readonly PlayerAccountService _playerService;
		private readonly DiscriminatorService _discriminatorService;
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly ItemService _itemService;
		private readonly ProfileService _profileService;
		private readonly NameGeneratorService _nameGeneratorService;
		private readonly TokenGeneratorService _tokenGeneratorService;
		
		// Component Services
		private readonly AbTestService _abTestService;
		private readonly AccountService _accountService;
		private readonly EquipmentService _equipmentService;
		private readonly HeroService _heroService;
		private readonly MultiplayerService _multiplayerService;
		private readonly QuestService _questService;
		private readonly StoreService _storeService;
		private readonly SummaryService _summaryService;
		private readonly TutorialService _tutorialService;
		private readonly WalletService _walletService;
		private readonly WorldService _worldService;
#pragma warning restore CS0649
		private Dictionary<string, ComponentService> ComponentServices { get; init; }


		/// <summary>
		/// Will on 2021.12.16
		/// Normally, so many newlines for a constructor is discouraged.  However, for something that requires so many
		/// different services, I'm willing to make an exception for readability.
		///
		/// I don't think breaking all of a player's data into so many components was good design philosophy; while it
		/// was clearly done to prevent Mongo documents from hitting huge sizes, this does mean that player-service
		/// needs to hit the database very frequently, and updating components requires a separate db hit per component.
		/// 
		/// Most of the components have a very small amount of information, so I'm not sure why they were separated.
		/// If I had to wager a guess, the architect was used to RDBMS-style design where each of these things would be
		/// its own table, but the real strength of Mongo is being able to store full objects in one spot, avoiding the
		/// need to write joins / multiple queries to pull information.
		///
		/// The maximum document size in Mongo is 16 MB, and we're nowhere close to that limit.
		///
		/// This also means that we have many more possible points of failure; whenever we go to update the player record,
		/// we have so many more write operations that can fail, which should trigger a transaction rollback.
		///
		/// Maintaining a transaction with async writes has caused headaches and introduced kluges to get it working, too.
		/// This would be less of a problem with fewer components, or just one gigantic player record.
		///
		/// To retrieve components from a monolithic record, mongo can Project specific fields, and just used the one query.
		/// 
		/// TODO: Compare performance with loadtests: all these collections vs. a monolithic player record
		/// When there's some downtime, it's worth exploring and quantifying just what kind of impact this design has.
		/// Maybe it's more performant than I think.  It's also entirely possible that it requires too much time to change
		/// for TH, but we should re-evaluate it for the next game if that's the case.
		/// </summary>
		[SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")]
		public TopController(IConfiguration config)  : base(config) =>
			ComponentServices = new Dictionary<string, ComponentService>()
			{
				{ Component.AB_TEST, _abTestService },
				{ Component.ACCOUNT, _accountService },
				{ Component.EQUIPMENT, _equipmentService },
				{ Component.HERO, _heroService },
				{ Component.MULTIPLAYER, _multiplayerService },
				{ Component.QUEST, _questService },
				{ Component.STORE, _storeService },
				{ Component.SUMMARY, _summaryService },
				{ Component.TUTORIAL, _tutorialService },
				{ Component.WALLET, _walletService },
				{ Component.WORLD, _worldService }
			};

		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck() => Ok(
			_playerService.HealthCheckResponseObject,
			_discriminatorService.HealthCheckResponseObject,
			_dynamicConfigService.HealthCheckResponseObject,
			_itemService.HealthCheckResponseObject,
			_nameGeneratorService.HealthCheckResponseObject,
			_profileService.HealthCheckResponseObject,
			_tokenGeneratorService.HealthCheckResponseObject
		);

		[HttpPatch, Route("update")]
		public ActionResult Update()
		{
			IClientSessionHandle session = _itemService.StartTransaction();
			GenericData[] components = Require<GenericData[]>("components");
			Item[] items = Optional<Item[]>("items") ?? Array.Empty<Item>();
			foreach (Item item in items)
				item.AccountId = Token.AccountId;

			long totalMS = Timestamp.UnixTimeMS;
			long componentMS = Timestamp.UnixTimeMS;

			List<Task<bool>> tasks = components.Select(data => ComponentServices[data.Require<string>(Component.FRIENDLY_KEY_NAME)]
				.UpdateAsync(
					accountId: Token.AccountId,
					data: data.Require<string>(Component.FRIENDLY_KEY_DATA),
					session: session
				)
			).ToList();


			componentMS = Timestamp.UnixTimeMS - componentMS;

			long itemMS = Timestamp.UnixTimeMS;

			Item[] toSave = items.Where(item => !item.MarkedForDeletion).ToArray();
			Item[] toDelete = items.Where(item => item.MarkedForDeletion).ToArray();

			if (toSave.Any())
				tasks.Add(_itemService.BulkUpdateAsync(toSave, session));
			if (toDelete.Any())
				tasks.Add(_itemService.BulkDeleteAsync(toDelete, session));
			itemMS = Timestamp.UnixTimeMS - itemMS;
			
			Task.WaitAll(tasks.ToArray());

			if (tasks.Select(task => task.Result).Any(success => !success))
			{
				session.AbortTransaction();
				Log.Warn(Owner.Default, "The update was aborted.  One or more updates was unsuccessful.");
				return Problem(detail: "Transaction aborted.");
			}
			
			session.CommitTransaction();

			totalMS = Timestamp.UnixTimeMS - totalMS;

			return Ok(new
			{
				Token = Token, 
				componentTaskCreationMS = componentMS, 
				itemTaskCreationMS = itemMS, 
				totalMS = totalMS
			});
		}

		[HttpGet, Route("read")]
		public ActionResult Read()
		{
			// ~900 ms sequential reads
			// ~250-350ms concurrent reads
			string[] names = Optional<string>("names")?.Split(',');

			List<Task<Component>> tasks = new List<Task<Component>>();

			foreach (string name in names)
			{
				if (!ComponentServices.ContainsKey(name))
					continue;
				tasks.Add(ComponentServices[name].LookupAsync(Token.AccountId));
			}

			Task.WaitAll(tasks.ToArray());

			return Ok(value: new GenericData()
			{
				{ "accountId", Token.AccountId },
				{ "components", tasks.Select(task => task.Result) }
			});
		}

		[HttpPost, Route("launch"), NoAuth]
		public ActionResult Launch()
		{
			string installId = Require<string>("installId");
			string requestId = Optional<string>("requestId") ?? Guid.NewGuid().ToString();
			string clientVersion = Optional<string>("clientVersion");
			string clientType = Optional<string>("clientType");
			string dataVersion = Optional<string>("dataVersion");
			string deviceType = Optional<string>("deviceType");
			string osVersion = Optional<string>("osVersion");
			string systemLanguage = Optional<string>("systemLanguage");
			string screenname = Optional<string>("screenName");
			string mergeAccountId = Optional<string>("mergeAccountId");
			string mergeToken = Optional<string>("mergeToken");
			GenericData sso = Optional<GenericData>("sso");

			Player player = _playerService.FindOne(player => player.InstallId == installId);

			// This block is an upgrade for the new behavior for screennames.
			// if (player.Screenname == null)
			// {
			// 	player.Screenname = screenname ?? ComponentServices[Component.ACCOUNT].Lookup(player.Id)?.Data?.Optional<string>("accountName");
			// 	
			// 	Log.Info(Owner.Will, "Looking up screenname from Account Component.", data: new
			// 	{
			// 		RetrievedName = player.Screenname,
			// 		Detail = "A player record was found, but without a screenname.  This should be a one-time upgrade per account."
			// 	});	
			// 	_playerService.Update(player);
			// }

			player ??= CreateNewAccount(installId, deviceType, clientVersion); // TODO: are these vars used anywhere else?

			Profile[] profiles = _profileService.Find(player.AccountId, sso, out SsoData[] ssoData);
			Profile[] conflictProfiles = profiles
				.Where(profile => profile.AccountId != player.AccountId)
				.ToArray();
			
			// SSO data was provided, but there's no profile match.  We should create a profile for this SSO on this account.
			foreach (SsoData data in ssoData)
			{
				if (profiles.Any(profile => profile.Type == data.Source))
					continue;
				_profileService.Create(new Profile(player.Id, data));
			}

			int discriminator = _discriminatorService.Lookup(player);

			string token = _tokenGeneratorService.Generate(player.AccountId, player.Screenname, discriminator, geoData: GeoIPData, email: ssoData.FirstOrDefault(sso => sso.Email != null)?.Email);

			if (conflictProfiles.Any())
			{
				Player other = _playerService.Find(conflictProfiles.First().AccountId);
				other.GenerateRecoveryToken();
				_playerService.Update(other);

				object response = new
				{
					ErrorCode = "accountConflict",
					AccessToken = token,
					AccountId = player.AccountId,
					ConflictingAccountId = conflictProfiles.First().AccountId,
					ConflictingProfiles = conflictProfiles,
					TransferToken = other.TransferToken,
					SsoData = ssoData
				};
				
				Log.Info(Owner.Default, "Account Conflict", data: response);
				return Ok(response);
			}
			else if (player.InstallId != installId) // TODO: Not sure what this actually accomplishes.
				return Ok(new { ErrorCode = "installConflict", ConflictingAccountId = player.AccountId });
			else // No conflict
			{
				// TODO: #370 saveInstallIdProfile
				foreach (Profile p in profiles)
					_profileService.Update(p); // Are we even modifying anything?
				// TODO: #378 updateAccountData
			}

			return Ok(new
			{
				RemoteAddr = GeoIPData.IPAddress ?? IpAddress, // fallbacks for local dev, since ::1 fails the lookups.
				GeoipAddr = GeoIPData.IPAddress ?? IpAddress,
				Country = GeoIPData.CountryCode,
				ServerTime = Timestamp.UnixTime,
				RequestId = HttpContext.Request.Headers["X-Request-ID"].ToString() ?? Guid.NewGuid().ToString(),
				AccessToken = token,
				Player = player,
				Discriminator = discriminator,
				SsoData = ssoData
			});
		}

		private Player CreateNewAccount(string installId, string deviceType, string clientVersion)
		{
			// string installId = Require<string>("installId");
			string requestId = Optional<string>("requestId") ?? Guid.NewGuid().ToString();
			// string clientVersion = Optional<string>("clientVersion");
			string clientType = Optional<string>("clientType");
			string dataVersion = Optional<string>("dataVersion");
			// string deviceType = Optional<string>("deviceType");
			string osVersion = Optional<string>("osVersion");
			string systemLanguage = Optional<string>("systemLanguage");
			string screenname = Optional<string>("screenName");
			string mergeAccountId = Optional<string>("mergeAccountId");
			string mergeToken = Optional<string>("mergeToken");
			
			Player player = new Player(_nameGeneratorService.Next)
			{
				ClientVersion = clientVersion,
				DeviceType = deviceType,
				InstallId = installId,
				DataVersion = dataVersion,
				PreviousDataVersion = null,
				UpdatedTimestamp = 0
			};
			_playerService.Create(player);
			Profile profile = new Profile(player);
			_profileService.Create(profile);
			if (!PlatformEnvironment.SwarmMode)
				Log.Info(Owner.Default, "New account created.", data: new
				{
					InstallId = player.AccountId,
					ProfileId = profile.Id,
					AccountId = profile.AccountId
				});
			return player;
		}

		// TODO: Explore MongoTransaction attribute
		// TODO: "link" instead of "transfer"?
		[HttpPatch, Route("transfer")]
		public ActionResult Transfer()
		{
			string transferToken = Require<string>("transferToken");
			string[] profileIds = Optional<string[]>("profileIds") ?? new string[]{};
			// TODO: Optional<bool>("cancel")

			Player player = _playerService.Find(Token.AccountId);
			Player other = _playerService.FindOne(p => p.TransferToken == transferToken);

			if (other == null)
				throw new Exception("No player account found for transfer token.");

			if (profileIds.Any()) // Move the specified profile IDs to the requesting player.  This reassigns SSO profiles to other accounts.
			{
				if (player.AccountIdOverride != null)
					Log.Warn(Owner.Default, "AccountIDOverride is not null for a transfer; this should be impossible.", data: new
					{
						Player = Token,
						OtherPlayer = other,
						TransferToken = transferToken
					});
				player.AccountIdOverride = null;
				
				Profile[] sso = _profileService.Find(p => profileIds.Contains(p.ProfileId) && p.Type != Profile.TYPE_INSTALL);
				foreach (Profile profile in sso)
				{
					profile.PreviousAccountIds.Add(other.AccountId);
					profile.AccountId = player.Id;
					_profileService.Update(profile);
				}
				other.AccountMergedTo = player.Id;
				
				_playerService.Update(player);
				_playerService.Update(other);
				Log.Info(Owner.Default, "Profiles transferred.", data: new
				{
					Player = Token,
					PreviousAccount = other,
					Profiles = sso
				});
				
				return Ok(new { TransferredProfiles = sso });
			}
			else
			{
				player.AccountIdOverride = other.AccountId;
				player.Screenname = other.Screenname;
				other.TransferToken = null;
				
				_playerService.Update(player);
				_playerService.Update(other);
				_playerService.SyncScreenname(other.Screenname, other.AccountId); // TODO: Can combine these updates into one query

				_profileService.Create(new Profile(player));

				Token.ScreenName = other.Screenname;
				Log.Info(Owner.Default, "AccountID overridden.", data: new
				{
					Player = Token,
					Account = player
				});
				return Ok(new { Account = player });
			}
		}

		[HttpGet, Route("config"), NoAuth]
		public ActionResult GetConfig()
		{
			string clientVersion = Optional<string>("clientVersion");
			GenericData config = _dynamicConfigService.GameConfig;
			
			GenericData clientVars = ExtractClientVars(
				clientVersion, 
				prefixes: config.Require<string>("clientVarPrefixesCSharp").Split(','), 
				configs: config
			);

			LaunchResponse response = new LaunchResponse();
			response.ClientVars = clientVars;

			return Ok(new
			{
				ClientVersion = clientVersion,
				ClientVars = clientVars
			});
		}
		private GenericData ExtractClientVars(string clientVersion, string[] prefixes, params GenericData[] configs)
		{
			List<string> clientVersions = new List<string>();
			if (clientVersion != null)
			{
				clientVersions.Add(clientVersion);
				while (clientVersion.IndexOf('.') > 0)
					clientVersions.Add(clientVersion = clientVersion[..clientVersion.LastIndexOf('.')]);
			}

			GenericData output = new GenericData();
			foreach (string prefix in prefixes)
			{
				string defaultVar = prefix + "default:";
				string[] versionVars = clientVersions
					.Select(it => prefix + it + ":")
					.ToArray();
				foreach (GenericData config in configs)
					foreach (string key in config.Keys)
					{
						string defaultKey = key.Replace(defaultVar, "");
						if (key.StartsWith(defaultVar) && !output.ContainsKey(defaultKey))
							output[defaultKey] = config.Require<string>(key);
						foreach(string it in versionVars)
							if (key.StartsWith(it))
								output[key.Replace(it, "")] = config.Require<string>(key);
					}
			}
			return output;
		}

		[HttpGet, Route("items")]
		public ActionResult GetItems()
		{
			string[] ids = Optional<string>("ids")?.Split(',');
			string[] types = Optional<string>("types")?.Split(',');

			long itemMS = Timestamp.UnixTimeMS;
			Item[] output = _itemService.GetItemsFor(Token.AccountId, ids, types);
			itemMS = Timestamp.UnixTimeMS - itemMS;
			
			return Ok(new { Items = output, itemMS = itemMS });
		}

		[HttpPatch, Route("screenname")]
		public ActionResult ChangeName()
		{
			string sn = Require<string>("screenname");
			
			Player player = _playerService.Find(Token.AccountId);
			player.Screenname = sn;
			_playerService.Update(player);

			int discriminator = _discriminatorService.Update(player);

			string token = _tokenGeneratorService.Generate(
				accountId: player.AccountId, 
				screenname: player.Screenname, 
				discriminator: discriminator,
				geoData: GeoIPData, 
				email: Token.Email
			);

			return Ok(new
			{
				Player = player,
				AccessToken = token,
				Discriminator = discriminator
			});
		}

		[HttpPost, Route("iostest"), NoAuth]
		public void AppleTest()
		{
			// string token = Require<GenericData>("sso").Require<GenericData>("appleId").Require<string>("token");
			// AppleToken at = new AppleToken(token);
			// at.Decode();
			//
			//
			// GenericData payload = new GenericData();
			// GenericData response = PlatformRequest.Post("https://appleid.apple.com/auth/token", payload: payload).Send();
		}

		[HttpGet, Route("lookup")]
		public ActionResult PlayerLookup()
		{
			// TODO: This is a little janky, and once the SummaryComponent is implemented, this should just return those entries.
			string[] accountIds = Require<string>("accountIds")?.Split(",");
			
			DiscriminatorGroup[] discriminators = _discriminatorService.Find(accountIds);


			Dictionary<string, string> avatars = new Dictionary<string, string>();
			foreach (Component component in ComponentServices[Component.ACCOUNT].Find(accountIds))
				if (!avatars.ContainsKey(component.AccountId) || avatars[component.AccountId] == null)
					avatars[component.AccountId] = component.Data.Optional<string>("accountAvatar");
			// Dictionary<string, string> avatars = ComponentServices[Component.ACCOUNT]
			// 	.Find(accountIds)
			// 	.ToDictionary(
			// 		keySelector: component => component.AccountId,
			// 		elementSelector: component => component.Data.Optional<string>("accountAvatar")
			// 	);

			Dictionary<string, GenericData> output = new Dictionary<string, GenericData>();
			
			foreach (DiscriminatorGroup group in discriminators)
				foreach (DiscriminatorMember member in group.Members.Where(member => accountIds.Contains(member.AccountId)))
					output.Add(member.AccountId, new GenericData()
					{
						{ Player.FRIENDLY_KEY_SCREENNAME, member.ScreenName },
						{ "discriminator", group.Number.ToString().PadLeft(4, '0') },
						{ "accountAvatar", avatars.ContainsKey(member.AccountId) ? avatars[member.AccountId] : null }
					});

			return Ok(new
			{
				Results = output.Select(pair => new GenericData()
				{
					{ pair.Key, pair.Value }
				})
			});
		}

		[HttpDelete, Route("pesticide"), NoAuth]
		public ActionResult KillAllLocusts()
		{
			// TODO: This should require admin, and optimize queries

			Player[] locusts = _playerService.Find(filter: player => player.InstallId.StartsWith("locust-"));

			foreach (Player locust in locusts)
			{
				foreach (ComponentService componentService in ComponentServices.Values)
					componentService.Delete(locust);
				foreach (Profile profile in _profileService.Find(profile => profile.AccountId == locust.AccountId))
					_profileService.Delete(profile.Id);
				_itemService.Delete(locust);
				_playerService.Delete(locust.Id);
			}

			return Ok(new
			{
				LocustsKilled = locusts.Length
			});
		}
	}
}