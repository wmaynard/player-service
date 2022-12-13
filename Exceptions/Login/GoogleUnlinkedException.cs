using PlayerService.Models.Login;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class GoogleUnlinkedException : PlatformException
{
    public GoogleAccount Account { get; init; }

    public GoogleUnlinkedException(GoogleAccount account = null) : base("Google account not yet linked.", code: ErrorCode.GoogleAccountMissing)
    {
        Account = account;
    }
}