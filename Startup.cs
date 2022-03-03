using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Apis.Auth.AspNetCore3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService
{
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

		// public override void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		// {
		// 	base.Configure(app, env);
		// 	app.UseHttpsRedirection();
		// 	app.UseAuthentication();
		// }
	}
}