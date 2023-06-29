using System;
using PlayerService.Models.Login;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Services
{
	public class PlariumService : PlatformService
	{
#pragma warning disable
		private readonly ApiService    _apiService;
		private readonly DynamicConfig _dynamicConfig;
#pragma warning restore
		
		internal static PlariumService Instance { get; private set; }

		public PlariumService() => Instance = this;
		
		public string VerifyCode(string plariumCode)
		{
			if (string.IsNullOrWhiteSpace(plariumCode))
				return null;
			string authToken = null;
			
			string tokenUrl = _dynamicConfig.Require<string>(key: "plariumTokenUrl");

			string failure = null;
			_apiService
				.Request(tokenUrl)
				.ExpectNonJson()
				.AddHeader(key: "game_id", value: PlatformEnvironment.Require<string>("PLARIUM_GAME"))
				.AddHeader(key: "secret_key", value: PlatformEnvironment.Require<string>("PLARIUM_SECRET"))
				.SetPayload(new RumbleJson 
	            {
		            { "clientId", 143},
		            {"code", plariumCode},
		            {"redirectUri", "towersandtitansdl://plariumplay/"},
		            {"privateKey", PlatformEnvironment.Require<string>(key: "PLARIUM_PRIVATE_KEY")},
		            {"grantType", "authorization_code"}
	            })
				.OnSuccess(res =>
				{
				   authToken = res.AsString;
				   Log.Local(owner: Owner.Nathan, message: "Successfully fetched Plarium code.");
				})
				.OnFailure(response => failure = $"Failed to fetch Plarium token; {response?.Optional<string>("message")}")
				.Post();

			if (!string.IsNullOrWhiteSpace(failure))
				throw new PlatformException(failure, code: ErrorCode.ApiFailure);

			return authToken;
		}

		public PlariumAccount VerifyToken(string plariumToken)
		{
			if (string.IsNullOrWhiteSpace(plariumToken))
				return null;
			string url = _dynamicConfig.Require<string>(key: "plariumAuthUrl");
			_apiService
				.Request(url)
				.AddHeader(key: "game_id", value: PlatformEnvironment.Require<string>("PLARIUM_GAME"))
				.AddHeader(key: "secret_key", value: PlatformEnvironment.Require<string>("PLARIUM_SECRET"))
				.SetPayload(new RumbleJson 
				{
					{ "auth_token", plariumToken }
				})
				.OnSuccess(_ => Log.Local(owner: Owner.Nathan, message: "Successfully validated Plarium token."))
				.OnFailure(_ => Log.Error(owner: Owner.Nathan, message: "Failed to validate Plarium token."))
				.Post(out RumbleJson response, out int code);
			
			try
			{
				return new PlariumAccount(
					plariumId: response.Require<string>("plid"), 
					email: response.Require<string>("login")
				);
			}
			catch (Exception e)
			{
				throw new PlatformException(message: "Error occurred when validating Plarium token.", inner: e);
			}
		}
	}
}

