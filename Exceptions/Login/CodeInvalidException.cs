using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class CodeInvalidException : PlatformException
{
    public string Email { get; set; }
    public CodeInvalidException(string email) : base("Code is invalid.", code: ErrorCode.ConfirmationCodeInvalid)
    {
        Email = email;
    }
}