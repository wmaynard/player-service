using PlayerService.Models.Login;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class RumbleNotConfirmedException : PlatformException
{
    public string Email { get; init; }

    public RumbleNotConfirmedException(string email) : base("Rumble account not yet confirmed.", code: ErrorCode.RumbleAccountMissing)
    {
        Email = email;
    }
}

public class ConfirmationCodeExpiredException : PlatformException
{
    public string Email { get; init; }

    public ConfirmationCodeExpiredException(string email) : base("Confirmation code has expired.", code: ErrorCode.ConfirmationCodeExpired)
    {
        Email = email;
    }

}