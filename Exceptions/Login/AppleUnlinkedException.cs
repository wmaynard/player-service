using PlayerService.Models.Login;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class AppleUnlinkedException : PlatformException
{
    public AppleAccount Account { get; init; }

    public AppleUnlinkedException(AppleAccount account = null) : base("Apple account not yet linked.", code: ErrorCode.AppleAccountMissing)
    {
        Account = account;
    }
}