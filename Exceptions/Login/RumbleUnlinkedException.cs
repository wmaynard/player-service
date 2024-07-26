using PlayerService.Models.Login;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class RumbleUnlinkedException : PlatformException
{
    public RumbleAccount Account { get; init; }
    public string Email { get; init; }

    public RumbleUnlinkedException(RumbleAccount account = null) : base("Rumble account not yet linked.", code: ErrorCode.RumbleAccountMissing)
    {
        Account = account?.Prune();
    }

    public RumbleUnlinkedException(string email) : this()
    {
        Email = email;
    }
}