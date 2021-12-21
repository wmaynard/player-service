using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver.Core.Operations;
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
	[ApiController, Route("player"), RequireAuth]
	public class TopController : PlatformController
	{
		private readonly Services.PlayerAccountService _playerService;
		private readonly DiscriminatorService _discriminatorService;
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly ProfileService _profileService;
		private readonly NameGeneratorService _nameGeneratorService;
		
		private Dictionary<string, ComponentService> ComponentServices { get; init; }
		
		private DynamicConfigClient _config;

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
				// TODO: ItemService
				NameGeneratorService nameGeneratorService,
				PlayerAccountService playerService,
				ProfileService profileService,
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
			_nameGeneratorService = nameGeneratorService;
			_profileService = profileService;

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

			// DynamicConfig dc = new DynamicConfig();
			_config = new DynamicConfigClient(
				configServiceUrl: PlatformEnvironment.Variable("RUMBLE_CONFIG_SERVICE_URL"),
				secret: PlatformEnvironment.Variable("RUMBLE_KEY"),
				gameId: PlatformEnvironment.Variable("GAME_GUKEY")
			);
			_config.Initialize();
		}

		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_playerService.HealthCheckResponseObject);
		}

		[HttpPatch, Route("update")]
		public ActionResult Update()
		{
			GenericData[] components = Require<GenericData[]>("components");

			return Ok();
		}
		
		
		

		[HttpGet, Route("testConflict"), NoAuth]
		public ActionResult TestConflict()
		{
			Player player = CreateNewAccount(Guid.NewGuid().ToString(), "postman", "postman 1.0.0");
			Player player2 = CreateNewAccount(Guid.NewGuid().ToString(), "postman", "postman 2.0.0");
			
			Profile sso = new Profile(player.AccountId, "agoobagoo", Profile.TYPE_GOOGLE);
			_profileService.Create(sso);

			return Ok(new
			{
				install_1 = player.InstallId,
				install_2 = player2.InstallId
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

			Profile[] profiles = _profileService.Find(player.AccountId, sso);
			Profile[] conflictProfiles = profiles
				.Where(profile => profile.AccountId != player.AccountId)
				.ToArray();
			// TODO: If SSO provided and no profile match, create profile for SSO on this account

			int discriminator = _discriminatorService.Lookup(player);
			string token = GenerateToken(player.AccountId, player.Screenname, discriminator);

			if (conflictProfiles.Any())
			{
				// player.GenerateRecoveryToken();
				// _playerService.Update(player);

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
					TransferToken = other.TransferToken
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
				RemoteAddr = "",
				GeoipAddr = "",
				Country = "",
				ServerTime = Timestamp.UnixTime,
				RequestId = Guid.NewGuid().ToString(),
				AccessToken = token,
				Player = player,
				Discriminator = discriminator
			});
		}

		private Player CreateNewAccount(string installId, string deviceType, string clientVersion)
		{
			Player player = new Player(_nameGeneratorService.Next)
			{
				ClientVersion = clientVersion,
				DeviceType = deviceType,
				InstallId = installId
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
		[HttpPatch, Route("transfer")]
		public ActionResult Transfer()
		{
			string transferToken = Require<string>("transferToken");
			string[] profileIds = Optional<string[]>("profileIds") ?? new string[]{};

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

		private static string GenerateToken(string aid, string sn, int discriminator)
		{
			if (aid == null || sn == null || discriminator < 0)
				throw new InvalidUserException(aid, sn, discriminator);

			PlatformRequest request = PlatformRequest.Post(
				url: "https://dev.nonprod.tower.cdrentertainment.com/secured/token/generate",
				headers: new Dictionary<string, string>() {{"Authorization", "Bearer eyJraWQiOiJqd3QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI2MTllOWU0YjhlNGQyYWI2ZjkyY2M5MGUiLCJhdWQiOiI1NzkwMWM2ZGY4MmE0NTcwODAxOGJhNzNiOGQxNjAwNCIsImlzcyI6IlJ1bWJsZSBQbGF5ZXIgU2VydmljZSIsImRpc2MiOjIxNDEsInNuIjoiaW9zIExvZ2luIFBvc3RtYW4gVGVzdCIsImV4cCI6MTYzOTI2NDQwOSwiaWF0IjoxNjM4OTE4ODA5LCJrZXkiOiJqd3QifQ.D3RiUfqmz_j8hyag2t5dHqeLAJVhiLwVX15niu-Ad_hVmhAZoLuTD60yydUGqK-VdugoPVMC9c8jzB1w8tgElrrGuCDpTwEv1hO5VONGUs5WHe1LKuCA2s_m0Fs0WrwP5S26dkEKegckoCRFSDTrAVz7jjgjyeM4-jmsORbJCqcm7B-8IrJ3oH_YfvfCMAptfjKBHDSGQhMJW1CBMK8J7e-4EsPQM3cHypj21Wi2MCGiSaDnv3rb1JpXelpFkDphpuDTC3dfHhHuLTKFdgsdghw273iLHtrRz-2_5RbpxMTK3slaEI95n6eocMuycvwKl-z8d0cKwu0W0HZGYZIjSw"}}
			);

			GenericData payload = new GenericData();
			payload["aid"] = aid;
			payload["screenname"] = sn;
			payload["origin"] = "player-service-v2";
			payload["email"] = "test@test.com";
			payload["discriminator"] = discriminator;

			GenericData response = request.Send(payload);
			return response.Require<GenericData>("authorization").Require<string>("token");
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

		private GenericData OldLaunch(string installId)
		{
			GenericData payload = new GenericData {{ "installId", installId }};
			PlatformRequest old = PlatformRequest.Post(
				url: "https://dev.nonprod.tower.cdrentertainment.com/player/launch",
				payload: payload
			);

			GenericData result = old.Send(out HttpStatusCode code);
			Task<GenericData> task = old.SendAsync();
			task.Wait();
			return task.Result;
		}

		[HttpPost, Route("iostest"), NoAuth]
		public void AppleTest()
		{
			string token = Require<GenericData>("sso").Require<GenericData>("appleId").Require<string>("token");
			AppleToken at = new AppleToken(token);
			at.Decode();
			
			
			GenericData payload = new GenericData();
			GenericData response = PlatformRequest.Post("https://appleid.apple.com/auth/token", payload: payload).Send();
			string foo = "foo";
			foo = "bar";
		}
	}
}