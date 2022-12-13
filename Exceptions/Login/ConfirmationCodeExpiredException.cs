using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class ConfirmationCodeExpiredException : PlatformException
{
    public string Email { get; init; }

    public ConfirmationCodeExpiredException(string email) : base("Confirmation code has expired.", code: ErrorCode.ConfirmationCodeExpired)
    {
        Email = email;
    }
}