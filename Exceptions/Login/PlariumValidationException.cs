using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions.Login;

public class PlariumValidationException : PlatformException
{
	public string EncryptedToken { get; set; }

	public PlariumValidationException(string token, Exception inner = null) :
		base(message: "Plarium token validation failed.", inner, code: ErrorCode.PlariumValidationFailed)
	{
		if (!PlatformEnvironment.IsProd)
			EncryptedToken = token;
	}
}