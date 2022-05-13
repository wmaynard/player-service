using System;
using System.Reflection;
using Google.Apis.Auth.AspNetCore3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService;
public class Startup : PlatformStartup
{
	public void ConfigureServices(IServiceCollection services)
	{
#if DEBUG
		base.ConfigureServices(services, Owner.Will, warnMS: 5_000, errorMS: 30_000, criticalMS: 180_000);
#else
		base.ConfigureServices(services, Owner.Will, warnMS: 5_000, errorMS: 30_000, criticalMS: 90_000);
#endif
		
		// TODO: This might be unnecessary for Google OAuth with the new solution; will need to test it.
		services.AddAuthentication(o =>
		{
			o.DefaultChallengeScheme = GoogleOpenIdConnectDefaults.AuthenticationScheme;
			o.DefaultForbidScheme = GoogleOpenIdConnectDefaults.AuthenticationScheme;
			o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
		})
		.AddGoogleOpenIdConnect(options =>
		{
			options.ClientId = PlatformEnvironment.Require<string>("GOOGLE_CLIENT_ID");
			options.ClientSecret = PlatformEnvironment.Require<string>("GOOGLE_APP_SECRET");
		});
	}
}