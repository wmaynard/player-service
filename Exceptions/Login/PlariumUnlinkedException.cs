using PlayerService.Models.Login;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class PlariumUnlinkedException : PlatformException
{
	public PlariumAccount Account { get; init; }
	
	public PlariumUnlinkedException(PlariumAccount account = null) : base(message: "Plarium account not yet linked.", code: ErrorCode.PlariumAccountMissing)
	{
		Account = account;
	}
}