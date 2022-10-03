using System;
using System.Collections.Generic;
using PlayerService.Exceptions;
using PlayerService.Models;
using PlayerService.Services.ComponentServices;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;

namespace PlayerService.Services;

public class TokenGeneratorService : PlatformService
{
	private readonly DynamicConfigService _dynamicConfigService;
	private readonly ApiService _apiService;
	private readonly AccountService _accountService;
	
	public TokenGeneratorService(AccountService accountService, ApiService apiService, DynamicConfigService dynamicConfigService)
	{
		_accountService = accountService;
		_apiService = apiService;
		_dynamicConfigService = dynamicConfigService;
	}

	public string Generate(string accountId, string screenname, int discriminator, GeoIPData geoData = null, string email = null)
	{
		if (accountId == null || screenname == null || discriminator < 0)
			throw new InvalidUserException(accountId, screenname, discriminator);

		string url = PlatformEnvironment.Url("/secured/token/generate");
		string token = _dynamicConfigService.GameConfig.Require<string>("playerServiceToken");

		Component account = _accountService.Lookup(accountId);

		RumbleJson payload = new RumbleJson
		{
			{ TokenInfo.FRIENDLY_KEY_ACCOUNT_ID, accountId },
			{ TokenInfo.FRIENDLY_KEY_SCREENNAME, screenname },
			{ "origin", "player-service-v2" },
			{ TokenInfo.FRIENDLY_KEY_EMAIL_ADDRESS, email },
			{ TokenInfo.FRIENDLY_KEY_DISCRIMINATOR, discriminator },
			{ "ipAddress", geoData?.IPAddress },
			{ "countryCode", geoData?.CountryCode }
		};
		_apiService
			.Request(url)
			.AddAuthorization(token)
			.SetPayload(payload)
			.OnFailure(response =>
			{
				Log.Error(Owner.Will, "Unable to generate token.", data: new
				{
					Payload = payload,
					Response = response,
					Url = response.RequestUrl
				});
			})
			.Post(out RumbleJson json, out int code);
		try
		{
			return json.Require<RumbleJson>("authorization").Require<string>("token");
		}
		catch (KeyNotFoundException)
		{
			throw new TokenGenerationException(json?.Optional<string>("message"));
		}
		catch (NullReferenceException)
		{
			throw new TokenGenerationException("Response was null.");
		}
		catch (Exception e)
		{
			Log.Error(Owner.Will, "An unexpected error occurred when generating a token.", data: new
			{
				Url = url,
				Response = json
			}, exception: e);
			throw;
		}
	}
}