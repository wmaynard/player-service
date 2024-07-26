using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions.Login;

public class InvalidPasswordException : PlatformException
{
    public string Username { get; init; }
    public string Reason { get; init; }

    public InvalidPasswordException(string username, string reason = null) : base("Invalid password.", code: ErrorCode.Unauthorized)
    {
        Username = username;
        Reason = reason;
    }
}



