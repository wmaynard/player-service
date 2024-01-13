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

		public PlariumAccount Verify(string code = null, string token = null)
		{
			if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(token))
				return null;
			
			// Plarium can use either the code OR the token to log in.  Only use one or the other; and prevent a request that
			// tries to use both.
			if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(token))
				throw new PlatformException("You can use a Plarium code or a Plarium token, but not both.");

			return !string.IsNullOrWhiteSpace(code)
				? VerifyCode(code)
				: VerifyToken(token);
		}
		
		public PlariumAccount VerifyCode(string plariumCode)
		{
			if (string.IsNullOrWhiteSpace(plariumCode))
				return null;
			string authToken = null;
			
			string tokenUrl = _dynamicConfig.Require<string>("plariumTokenUrl");

			string failure = null;
			_apiService
				.Request(tokenUrl)
				.ExpectNonJson()
				.AddHeader("game_id", PlatformEnvironment.Require<string>("PLARIUM_GAME"))
				.AddHeader("secret_key", PlatformEnvironment.Require<string>("PLARIUM_SECRET"))
				.SetPayload(new RumbleJson 
	            {
		            { "clientId", 143},
		            {"code", plariumCode},
		            {"redirectUri", "towersandtitansdl://plariumplay/"},
		            {"privateKey", PlatformEnvironment.Require<string>(key: "PLARIUM_PRIVATE_KEY")},
		            {"grantType", "authorization_code"}
	            })
				.OnSuccess(response => authToken = response.AsString)
				.OnFailure(response => failure = $"Failed to fetch Plarium token; {response?.Optional<string>("message")}")
				.Post();

			if (!string.IsNullOrWhiteSpace(failure))
				throw new PlatformException(failure, code: ErrorCode.ApiFailure);

			return VerifyToken(authToken);
		}

		public PlariumAccount VerifyToken(string plariumToken)
		{
			if (string.IsNullOrWhiteSpace(plariumToken))
				return null;
			string url = _dynamicConfig.Require<string>("plariumAuthUrl");
			_apiService
				.Request(url)
				.AddHeader("game_id", PlatformEnvironment.Require<string>("PLARIUM_GAME"))
				.AddHeader("secret_key", PlatformEnvironment.Require<string>("PLARIUM_SECRET"))
				.SetPayload(new RumbleJson 
				{
					{ "auth_token", plariumToken }
				})
				.OnSuccess(_ => Log.Local(Owner.Will, "Successfully validated Plarium token."))
				.OnFailure(_ => Log.Error(Owner.Will, "Failed to validate Plarium token."))
				.Post(out RumbleJson response, out int code);
			
			try
			{
				return new PlariumAccount
				{
					Id = response.Require<string>("plid"),
					Email = response.Require<string>("login")
				};
			}
			catch (Exception e)
			{
				throw new PlatformException(message: "Error occurred when validating Plarium token.", inner: e);
			}
		}
	}
}

