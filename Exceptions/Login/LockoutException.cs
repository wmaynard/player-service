using PlayerService.Services;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions.Login;

public class LockoutException : PlatformException
{
    public string Email { get; init; }
    public string IpAddress { get; init; }
    public long SecondsRemaining { get; init; }

    public LockoutException(string email, string ip, long waitTime) : base("This account is locked.  Try again later.", code: ErrorCode.Locked)
    {
        Email = email;
        IpAddress = ip;
        SecondsRemaining = LockoutService.Cooldown * 60 - (Timestamp.Now - waitTime);
    }
}