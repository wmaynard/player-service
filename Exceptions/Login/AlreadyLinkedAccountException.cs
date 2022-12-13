using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class AlreadyLinkedAccountException : PlatformException
{
    public string Type { get; init; }
    
    public AlreadyLinkedAccountException(string provider) : base("Account already linked.", code: ErrorCode.AccountAlreadyLinked)
    {
        Type = provider;
    }
}