using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;

public class ComponentInvalidException : PlatformException
{
    public string AccountId { get; init; }
    
    public ComponentInvalidException(string accountId, string reason) : base($"Component is invalid: {reason}.", code: ErrorCode.InvalidRequestData)
    {
        AccountId = accountId;
    }
}