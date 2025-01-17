using Google.Apis.Auth.AspNetCore3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using PlayerService.Filters;
using PlayerService.Services;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService;
public class Startup : PlatformStartup
{
	public override void ConfigureServices(IServiceCollection services)
	{
		base.ConfigureServices(services);
		
		// TODO: This is likely unnecessary; it's not needed for portal, which uses the same auth.
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

	protected override PlatformOptions ConfigureOptions(PlatformOptions options) => options
		.SetProjectOwner(Owner.Will)
		.SetRegistrationName("Player Service")
		.SetTokenAudience(Audience.PlayerService)
		.SetPerformanceThresholds(warnMS: 5_000, errorMS: 30_000, criticalMS: 90_000)
		.DisableFeatures(CommonFeature.ConsoleObjectPrinting)
		.SetLogglyThrottleThreshold(suppressAfter: 100, period: 1800)
		.AddFilter<MaintenanceFilter>()
		.AddFilter<PruneFilter>()
		.OnReady(_ => { });
}