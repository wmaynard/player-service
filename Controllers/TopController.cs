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
using Microsoft.Extensions.Configuration;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Models.Responses;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using PlayerService.Services.ComponentServices;
using PlayerService.Utilities;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.CSharp.Common.Interop;
using Rumble.Platform.CSharp.Common.Services;

namespace PlayerService.Controllers
{
	[ApiController, Route("player/v2"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
		private readonly PlayerAccountService _playerService;
		private readonly DiscriminatorService _discriminatorService;
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly ItemService _itemService;
		private readonly ProfileService _profileService;
		private readonly NameGeneratorService _nameGeneratorService;
		private readonly TokenGeneratorService _tokenGeneratorService;
		
		private Dictionary<string, ComponentService> ComponentServices { get; init; }

		private DynamicConfigClient dynamicConfig = new DynamicConfigClient(
			secret: PlatformEnvironment.Variable("RUMBLE_KEY"),
			configServiceUrl: PlatformEnvironment.Variable("RUMBLE_CONFIG_SERVICE_URL"),
			gameId: PlatformEnvironment.Variable("GAME_GUKEY")
		);

		/// <summary>
		/// Will on 2021.12.16
		/// Normally, so many newlines for a constructor is discouraged.  However, for something that requires so many
		/// different services, I'm willing to make an exception for readability.
		///
		/// I don't think breaking all of a player's data into so many components was good design philosophy; while it
		/// was clearly done to prevent Mongo documents from hitting huge sizes, this does mean that player-service
		/// needs to hit the database very frequently, and updating components requires 2n db hits:
		///		1 hit to retrieve the record
		///		1 hit to store the updated record
		/// Most of the components have a very small amount of information, so I'm not sure why they were separated.
		/// If I had to wager a guess, the architect was used to RDBMS-style design where each of these things would be
		/// its own table, but the real strength of Mongo is being able to store full objects in one spot, avoiding the
		/// need to write joins / multiple queries to pull information.
		///
		/// The maximum document size in Mongo is 16 MB, and we're nowhere close to that limit.
		///
		/// This also means that we have many more possible points of failure; whenever we go to update the player record,
		/// we have so many more write operations that can fail, which should trigger a transaction rollback.  KISS.
		/// 
		/// TODO: Compare performance with loadtests: all these collections vs. a monolithic player record
		/// When there's some downtime, it's worth exploring and quantifying just what kind of impact this design has.
		/// Maybe it's more performant than I think.  It's also entirely possible that it requires too much time to change
		/// for TH, but we should re-evaluate it for the next game if that's the case.
		/// </summary>
		[SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")]
		public TopController(IConfiguration config,
				DynamicConfigService configService,
				DiscriminatorService discriminatorService,
				ItemService itemService,
				NameGeneratorService nameGeneratorService,
				PlayerAccountService playerService,
				ProfileService profileService,
				TokenGeneratorService tokenGeneratorService,
				AbTestService abTestService,				// Component Services
				AccountService accountService,
				EquipmentService equipmentService,
				HeroService heroService,
				MultiplayerService multiplayerService,
				QuestService questService,
				StoreService storeService,
				SummaryService summaryService,
				TutorialService tutorialService,
				WalletService walletService,
				WorldService worldService
			) : base(config)
		{
			_playerService = playerService;
			_discriminatorService = discriminatorService;
			_dynamicConfigService = configService;
			_itemService = itemService;
			_nameGeneratorService = nameGeneratorService;
			_profileService = profileService;
			_tokenGeneratorService = tokenGeneratorService;

			ComponentServices = new Dictionary<string, ComponentService>();
			ComponentServices[Component.AB_TEST] = abTestService;
			ComponentServices[Component.ACCOUNT] = accountService;
			ComponentServices[Component.EQUIPMENT] = equipmentService;
			ComponentServices[Component.HERO] = heroService;
			ComponentServices[Component.MULTIPLAYER] = multiplayerService;
			ComponentServices[Component.QUEST] = questService;
			ComponentServices[Component.STORE] = storeService;
			ComponentServices[Component.SUMMARY] = summaryService;
			ComponentServices[Component.TUTORIAL] = tutorialService;
			ComponentServices[Component.WALLET] = walletService;
			ComponentServices[Component.WORLD] = worldService;
		}

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
			GenericData[] components = Require<GenericData[]>("components");
			foreach (GenericData json in components)
			{
				string name = json.Require<string>("name");
				Component component = ComponentServices[name].FindOne(component => component.AccountId == Token.AccountId);
				component ??= ComponentServices[name].Create(new Component(Token.AccountId));
				component.Data = json.Require<GenericData>(Component.FRIENDLY_KEY_DATA);
				ComponentServices[name].Update(component);
			}

			// Will on 2022.01.06: We're hitting Mongo for EVERY item?  Maybe with a transaction, this builds everything into one query,
			// but if it doesn't then this is miserable for performance.
			Item[] items = Optional<Item[]>("items") ?? Array.Empty<Item>();
			long ms = Timestamp.UnixTimeMS;
			foreach (Item item in items)
			{
				item.AccountId = Token.AccountId;
				if (item.MarkedForDeletion)
					_itemService.Delete(item);
				else
					_itemService.UpdateItem(item);
			}

			ms = Timestamp.UnixTimeMS - ms;

			return Ok(new { Token = Token, itemMS = ms});
		}
		
		[HttpGet, Route("testConflict"), NoAuth]
		public ActionResult TestConflict()
		{
			Player player = CreateNewAccount(Guid.NewGuid().ToString(), "postman", "postman 1.0.0");
			Player player2 = CreateNewAccount(Guid.NewGuid().ToString(), "postman", "postman 2.0.0");

			string ssoKey = Guid.NewGuid().ToString();
			Profile sso = new Profile(player.AccountId, ssoKey, Profile.TYPE_GOOGLE);
			_profileService.Create(sso);

			return Ok(new
			{
				install_1 = player.InstallId,
				install_2 = player2.InstallId,
				message = "Call /player/launch with installId = install_2 and the sso object.",
				sso = new
				{
					googlePlay = new
					{
						idToken = ssoKey
					}
				}
			});
		}

		[HttpGet, Route("read")]
		public ActionResult Read()
		{
			string[] names = Optional<string>("names")?.Split(',');
			
			List<Component> components = ComponentServices
				.Where(pair => names?.Contains(pair.Key) ?? true)
				.Select(pair => pair.Value.Lookup(Token.AccountId))
				.ToList();

			return Ok(value: new GenericData()
			{
				{ "accountId", Token.AccountId },
				{ "components", components }
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

			player ??= CreateNewAccount(installId, deviceType, clientVersion); // TODO: are these vars used anywhere else?
			
			
			// TODO: Handle install id not found (new client)

			Profile[] profiles = _profileService.Find(player.AccountId, sso, out SsoData[] ssoData);
			Profile[] conflictProfiles = profiles
				.Where(profile => profile.AccountId != player.AccountId)
				.ToArray();
			// TODO: If SSO provided and no profile match, create profile for SSO on this account

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
				Country = GeoIPData.Country,
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
				other.TransferToken = null;
				
				_playerService.Update(player);
				_playerService.Update(other);

				_profileService.Create(new Profile(player));
				
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
			
			// TODO: improve performance by only retrieving requested items.
			Item[] items = _itemService.GetItemsFor(Token.AccountId);
			if (types != null)
				items = items.Where(item => types.Contains(item.Type)).ToArray();
			if (ids != null)
				items = items.Where(item => ids.Contains(item.ItemId)).ToArray();
			return Ok(new { Items = items});
		}

		[HttpPost, Route("iostest"), NoAuth]
		public void AppleTest()
		{
			string token = Require<GenericData>("sso").Require<GenericData>("appleId").Require<string>("token");
			AppleToken at = new AppleToken(token);
			at.Decode();
			
			
			GenericData payload = new GenericData();
			GenericData response = PlatformRequest.Post("https://appleid.apple.com/auth/token", payload: payload).Send();
		}

		// [HttpGet, Route("googtest"), NoAuth]
		// public ActionResult GoogleTest([FromServices] IGoogleAuthProvider auth)
		// {
		// 	string token = Require<string>("idToken");
		// 	
		// 	SsoData data = SsoAuthenticator.Google(token);
		// 	
		// 	return Ok(data);
		// }
	}
}