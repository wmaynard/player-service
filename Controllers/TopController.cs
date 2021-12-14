using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Models.Responses;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.CSharp.Common.Interop;
using Rumble.Platform.CSharp.Common.Services;

namespace PlayerService.Controllers
{
	[ApiController, Route("player"), RequireAuth]
	public class TopController : PlatformController
	{
		private readonly InstallationService _installService;
		private readonly DiscriminatorService _discriminatorService;
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly ProfileService _profileService;
		private readonly NameGeneratorService _nameGeneratorService;
		
		private DynamicConfigClient _config;

		private DynamicConfigClient dynamicConfig = new DynamicConfigClient(
			secret: PlatformEnvironment.Variable("RUMBLE_KEY"),
			configServiceUrl: PlatformEnvironment.Variable("RUMBLE_CONFIG_SERVICE_URL"),
			gameId: PlatformEnvironment.Variable("GAME_GUKEY")
		);

		public TopController(InstallationService installService, DynamicConfigService configService, DiscriminatorService discriminatorService, 
			ProfileService profileService, NameGeneratorService nameGeneratorService, IConfiguration config) : base(config)
		{
			_installService = installService;
			_discriminatorService = discriminatorService;
			_dynamicConfigService = configService;
			_profileService = profileService;

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
			return Ok(_installService.HealthCheckResponseObject);
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

			LaunchResponse response = new LaunchResponse();
			response.RequestId = requestId;
			response.AccountId = installId;
			response.RemoteAddr = "foo"; // TODO
			response.GeoIPAddr = "foo"; // TODO
			response.Country = "foo"; // TODO
			
			GenericData config = _dynamicConfigService.GameConfig;
			
			GenericData clientVars = ExtractClientVars(
				clientVersion, 
				prefixes: config.Require<string>("clientVarPrefixesCSharp").Split(','), 
				configs: config
			);

			if (clientVars != null)
				response.ClientVars = clientVars;

			Installation install = _installService.FindOne(installation => installation.InstallId == installId);

			if (install == null)
			{
				// oogabooga2
				install = new Installation()
				{
					ClientVersion = clientVersion,
					DeviceType = deviceType,
					InstallId = installId
				};
				Profile profile = new Profile(install);
				_installService.Create(install);
				_profileService.Create(profile);
			}
			
			// TODO: Handle install id not found (new client)

			Profile[] profiles = _profileService.Find(install.Id, sso);
			string accountId = profiles.First().AccountId;
			Profile[] conflictProfiles = profiles
				.Where(profile => profile.AccountId != accountId)
				.ToArray();

			int discriminator = _discriminatorService.Lookup(accountId, out screenname);

			response.AccountId = profiles.First().AccountId;

			if (conflictProfiles.Any())
			{
				response.ErrorCode = "accountConflict";
				Log.Info(Owner.Default, "Account Conflict", data: new
				{
					AccountId = accountId,
					Profiles = profiles,
					ConflictProfiles = conflictProfiles,
					RequestData = Body
				});
				response.ConflictingAccountId = conflictProfiles.First().AccountId;
				
				install.GenerateRecoveryToken();
				_installService.Update(install);
				response.RecoveryToken = install.RecoveryToken;
				Log.Info(Owner.Default, "Merge token generated.", data: new
				{
					Installation = install
				});
			}
			else if (install.InstallId != installId) // TODO: Not sure what this actually accomplishes.
			{
				response.ErrorCode = "installConflict";
				response.ConflictingAccountId = accountId;
			}
			else // No conflict
			{
				// TODO: #370 saveInstallIdProfile
				foreach (Profile p in profiles)
					_profileService.Update(p); // Are we even modifying anything?
				// TODO: #378 updateAccountData
			}
			response.AccessToken = GenerateToken(accountId, screenname, discriminator);
			return Ok(response.ResponseObject);
		}

		[HttpPatch, Route("recover")]
		public ActionResult Recover()
		{
			string recoverToken = Require<string>("recoveryToken");
			string discardId = Require<string>("discardAccountId");
			string keepId = Require<string>("keepAccountId");

			Installation install = new Installation();
			_installService.Update(install);
			
			
			return Ok();
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
	}
}