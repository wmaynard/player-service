using System;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models.Responses
{
	public class LaunchResponse : PlatformDataModel
	{
		public bool Success { get; set; }
		public string RemoteAddr { get; set; }
		public string GeoIPAddr { get; set; }
		public string Country { get; set; }
		public long ServerTime => UnixTime;
		public object ClientVars { get; set; }
		public string RequestId { get; set; }
		public string AccountId { get; set; }
		public string ErrorCode { get; set; }
		public string SupportUrl { get; set; }

		public LaunchResponse()
		{
			Success = true;
		}
	}
}