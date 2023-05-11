using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;

public class DeviceMismatchException : PlatformException
{
    public DeviceMismatchException() : base("Device information does not match", code: ErrorCode.DeviceMismatch) { }
}