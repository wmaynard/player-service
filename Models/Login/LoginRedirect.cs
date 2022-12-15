using System.Text.Json.Serialization;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class LoginRedirect : PlatformDataModel
{
    public string Url { get; set; }

    public LoginRedirect(string url)
    {
        Url = url;
    }
}