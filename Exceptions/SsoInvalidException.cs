using System;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions
{
	public class SsoInvalidException : PlatformException
	{
		public string EncryptedToken { get; init; }
		
		public SsoInvalidException(string token, string provider, Exception inner = null) : base($"({provider}) SSO authentication failed.", inner)
		{
			EncryptedToken = token;
		}
	}
}