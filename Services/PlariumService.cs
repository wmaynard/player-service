using System;
using PlayerService.Models.Login;
using RCL.Logging;
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

		public PlariumService()
		{
			Instance = this;
		}
		
		public PlariumAccount Verify(string authToken)
		{
			string url = _dynamicConfig.Require<string>(key: "plariumAuthUrl");
			_apiService
				.Request(url)
				.OnSuccess(_ => Log.Local(owner: Owner.Nathan, message: "Successfully validated Plarium token."))
				.OnFailure(_ => Log.Error(owner: Owner.Nathan, message: "Failed to validate Plarium token."))
				.Post(out RumbleJson response, out int code);

			PlariumAccount plariumAccount = null;
			try
			{
				plariumAccount = response.ToModel<PlariumAccount>();
			}
			catch (Exception e)
			{
				Log.Error(owner: Owner.Nathan, message: "Error occurred when validating Plarium token.", data: $"Response: {response}.", exception: e);
				throw new PlatformException(message: "Error occurred when validating Plarium token.", inner: e);
			}

			return plariumAccount;
		}
	}
}

