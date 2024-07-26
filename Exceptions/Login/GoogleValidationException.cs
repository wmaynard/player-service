using System;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions.Login;

public class GoogleValidationException : PlatformException
{
    public string EncryptedToken { get; set; }
    public GoogleValidationException(string token, Exception inner = null) : base("Google token validation failed.", inner, code: ErrorCode.GoogleValidationFailed)
    {
        if (!PlatformEnvironment.IsProd)
            EncryptedToken = token;
    }
}