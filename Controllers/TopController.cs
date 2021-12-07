using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PlayerService.Models;
using PlayerService.Models.Responses;
using Rumble.Platform.Common.Web;
using PlayerService.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.CSharp.Common.Interop;

namespace PlayerService.Controllers
{
	[ApiController, Route("player"), RequireAuth]
	public class TopController : PlatformController
	{
		private readonly InstallationService _installService;
		private readonly DynamicConfigService _dynamicConfigService;
		
		private DynamicConfigClient _config;

		private DynamicConfigClient dynamicConfig = new DynamicConfigClient(
			secret: PlatformEnvironment.Variable("RUMBLE_KEY"),
			configServiceUrl: PlatformEnvironment.Variable("RUMBLE_CONFIG_SERVICE_URL"),
			gameId: PlatformEnvironment.Variable("GAME_GUKEY")
		);

		public TopController(InstallationService installService, DynamicConfigService configService, IConfiguration config) : base(config)
		{
			_installService = installService;
			_dynamicConfigService = configService;
			
				
				
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
			string deviceType = Optional<string>("deviceType");
			string osVersion = Optional<string>("osVersion");
			string systemLanguage = Optional<string>("systemLanguage");
			string screenname = Optional<string>("screenName");
			string mergeAccountId = Optional<string>("mergeAccountId");
			string mergeToken = Optional<string>("mergeToken");

			LaunchResponse response = new LaunchResponse();
			response.RequestId = requestId;
			response.AccountId = installId;
			response.RemoteAddr = "foo"; // TODO
			response.GeoIPAddr = "foo"; // TODO
			response.Country = "getCountry"; // TODO
			
			GenericData config = _dynamicConfigService.GameConfig;
			
			Dictionary<string, string> clientVars = ExtractClientVars(
				clientVersion, 
				prefixes: config.Require<string>("clientVarPrefixesCSharp").Split(','), 
				configs: config
			);

			if (clientVars != null)
				response.ClientVars = clientVars;

			if (false)
			{
				// if (dynamicConfigService.getConfig('canvas').list('blacklistCountries').contains(responseData.country as String)) {
				// response.ErrorCode = "geoblocked";
				// response.SupportUrl = gameConfig["supportUrl"];
				// return Ok(response);
			}
			

			return Ok(response);
		}

		private Dictionary<string, string> ExtractClientVars(string clientVersion, string[] prefixes, params GenericData[] configs)
		{
			List<string> clientVersions = new List<string>();
			if (clientVersion != null)
			{
				clientVersions.Add(clientVersion);
				while (clientVersion.IndexOf('.') > 0)
					clientVersions.Add(clientVersion = clientVersion[..clientVersion.LastIndexOf('.')]);
			}

			Dictionary<string, string> clientvars = new Dictionary<string, string>();
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
						if (key.StartsWith(defaultVar) && !clientvars.ContainsKey(defaultKey))
							clientvars[defaultKey] = config.Require<string>(key);
						foreach(string it in versionVars)
							if (key.StartsWith(it))
								clientvars[key.Replace(it, "")] = config.Require<string>(key);
					}
			}
			return clientvars;
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