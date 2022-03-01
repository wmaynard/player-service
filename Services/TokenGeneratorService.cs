using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.TraceSource;
using PlayerService.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;

namespace PlayerService.Services
{
	public class TokenGeneratorService : PlatformService
	{
		private readonly DynamicConfigService _dynamicConfigService;
		private readonly APIService _apiService;
		public TokenGeneratorService(DynamicConfigService dynamicConfigService, APIService apiService)
		{
			_dynamicConfigService = dynamicConfigService;
			_apiService = apiService;
		}

		public string Generate(string accountId, string screenname, int discriminator, GeoIPData geoData = null, string email = null)
		{
			if (accountId == null || screenname == null || discriminator < 0)
				throw new InvalidUserException(accountId, screenname, discriminator);

			string url = $"{_dynamicConfigService.PlatformUrl}secured/token/generate";
			string token = _dynamicConfigService.GameConfig.Require<string>("playerServiceToken");
			PlatformRequest request = PlatformRequest.Post(
				url: url,
				headers: new Dictionary<string, string>() {{"Authorization", $"Bearer {token}"}}
			);

			GenericData payload = new GenericData()
			{
				{ "aid", accountId },
				{ "screenname", screenname },
				{ "origin", "player-service-v2" },
				{ "email", email },
				{ "discriminator", discriminator },
				{ "ipAddress", geoData?.IPAddress },
				{ "countryCode", geoData?.CountryCode }
			};

			GenericData response = request.Send(payload);

			// GenericData result = _apiService
			// 	.Request(url)
			// 	.AddHeader("Authorization", $"Bearer {token}")
			// 	.SetPayload(new GenericData()
			// 	{
			// 		{ "aid", accountId },
			// 		{ "screenname", screenname },
			// 		{ "origin", "player-service-v2" },
			// 		{ "email", email },
			// 		{ "discriminator", discriminator },
			// 		{ "ipAddress", geoData?.IPAddress },
			// 		{ "countryCode", geoData?.CountryCode }
			// 	})
			// 	.OnSuccess((sender, args) =>
			// 	{
			// 		Log.Local(Owner.Will, "Successful request!");
			// 	})
			// 	.OnFailure((sender, args) =>
			// 	{
			// 		Log.Local(Owner.Will, "Request failed!");
			// 	}).Post();

			try
			{
				return response.Require<GenericData>("authorization").Require<string>("token");
			}
			catch (KeyNotFoundException)
			{
				throw new TokenGenerationException(response?.Optional<string>("message"));
			}
			catch (Exception e)
			{
				Log.Error(Owner.Will, "An unexpected error occurred when generating a token.", data: new
				{
					Url = url,
					Response = response,
					Payload = payload
				}, exception: e);
				throw;
			}
		}
	}
}