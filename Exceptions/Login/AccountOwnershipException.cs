using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class AccountOwnershipException : PlatformException
{
    public string Type { get; init; }
    public string CurrentId { get; init; }
    public string OtherId { get; init; }
    public string Reason { get; init; }
    
    public AccountOwnershipException(string provider, string reason = null) : base("Account is linked to a different player.", code: ErrorCode.AccountAlreadyOwned)
    {
        Type = provider;
        Reason = reason;
    }
    public AccountOwnershipException(string provider, string currentId, string otherId, string reason = null) : this(provider, reason)
    {
        CurrentId = currentId;
        OtherId = otherId;
    }
}