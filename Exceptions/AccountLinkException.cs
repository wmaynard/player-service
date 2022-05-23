using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions;

public class AccountLinkException : PlatformException
{
	public string TransferToken { get; init; }
	public string[] ProfileIds { get; init; }
	public string AccountToLink { get; init; }
	public string MainAccount { get; init; }

	public AccountLinkException(string message, string requester, string target = null, string transferToken = null, string[] profileIds = null) : base(message, code: ErrorCode.InvalidRequestData)
	{
		AccountToLink = requester;
		MainAccount = target;
		TransferToken = transferToken;
		ProfileIds = profileIds;
	}
}