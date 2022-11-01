using System.Threading.Tasks;
using Google.Apis.Auth;
using Rumble.Platform.Data;

namespace PlayerService.Models.Sso;

public class GoogleAccount : PlatformDataModel
{
    public string Id { get; set; }
    public string Email { get; set; }
    public bool EmailVerified { get; set; }
    public string HostedDomain { get; set; }
    public string Name { get; set; }
    public string Picture { get; set; }

    private GoogleAccount(GoogleJsonWebSignature.Payload payload)
    {
        Id = payload?.Subject;
        Email = payload?.Email;
        EmailVerified = payload?.EmailVerified ?? false;
        HostedDomain = payload?.HostedDomain;
        Name = payload?.Name;
        Picture = payload?.Picture;

        if (!string.IsNullOrWhiteSpace(Name))
            return;
        
        string name = $"{payload?.GivenName} {payload?.FamilyName}";
        if (!string.IsNullOrWhiteSpace(name))
            Name = name;
    }
    
    public static GoogleAccount ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        
        Task<GoogleJsonWebSignature.Payload> task = GoogleJsonWebSignature.ValidateAsync(token);
        task.Wait();

        return new GoogleAccount(task.Result);
    }
}
