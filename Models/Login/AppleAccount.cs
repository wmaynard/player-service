using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class AppleAccount : PlatformDataModel
{
    public string Id { get; set; }
    public static AppleAccount ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return null;
    }
}