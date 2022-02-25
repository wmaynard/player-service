using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
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
		
		public TokenGeneratorService(DynamicConfigService dynamicConfigService) => _dynamicConfigService = dynamicConfigService;
		
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

			GenericData response = request.Send(new GenericData()
			{
				{"aid", accountId},
				{"screenname", screenname},
				{"origin", "player-service-v2"},
				{"email", email},
				{"discriminator", discriminator},
				{"ipAddress", geoData?.IPAddress},
				{"countryCode", geoData?.CountryCode}
			});
			try
			{
				return response.Require<GenericData>("authorization").Require<string>("token");
			}
			catch (KeyNotFoundException)
			{
				throw new TokenGenerationException(response?.Optional<string>("message"));
			}
		}
	}
}