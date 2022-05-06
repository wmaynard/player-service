using System;
using System.Collections.Generic;
using PlayerService.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;

namespace PlayerService.Services;

public class TokenGeneratorService : PlatformService
{
	private readonly DynamicConfigService _dynamicConfigService;
	private readonly ApiService _apiService;
	public TokenGeneratorService(DynamicConfigService dynamicConfigService, ApiService apiService)
	{
		_dynamicConfigService = dynamicConfigService;
		_apiService = apiService;
	}

	public string Generate(string accountId, string screenname, int discriminator, GeoIPData geoData = null, string email = null)
	{
		if (accountId == null || screenname == null || discriminator < 0)
			throw new InvalidUserException(accountId, screenname, discriminator);

		string url = PlatformEnvironment.Url("/secured/token/generate");
		string token = _dynamicConfigService.GameConfig.Require<string>("playerServiceToken");

		_apiService
			.Request(url)
			.AddAuthorization(token)
			.SetPayload(new GenericData
			{
				{ "aid", accountId },
				{ "screenname", screenname },
				{ "origin", "player-service-v2" },
				{ "email", email },
				{ "discriminator", discriminator },
				{ "ipAddress", geoData?.IPAddress },
				{ "countryCode", geoData?.CountryCode }
			})
			.OnFailure((sender, response) =>
			{
				Log.Error(Owner.Will, "Unable to generate token.");
			})
			.Post(out GenericData response, out int code);
		try
		{
			return response.Require<GenericData>("authorization").Require<string>("token");
		}
		catch (KeyNotFoundException)
		{
			throw new TokenGenerationException(response?.Optional<string>("message"));
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
				Response = response
			}, exception: e);
			throw;
		}
	}
}