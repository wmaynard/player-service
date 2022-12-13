using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions.Login;

public class AppleValidationException : PlatformException
{
    public string EncryptedToken { get; set; }
    public AppleValidationException(string token, Exception inner = null) : base("Apple token validation failed.", inner, code: ErrorCode.AppleValidationFailed)
    {
        if (!PlatformEnvironment.IsProd)
            EncryptedToken = token;
    }
}