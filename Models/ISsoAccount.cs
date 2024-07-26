namespace PlayerService.Models;

public interface ISsoAccount
{
    public string Email { get; set; }
    public string Id { get; set; }
    public long AddedOn { get; set; }
    public long RollingLoginTimestamp { get; set; }
    public long WebValidationCount { get; set; }
    public long ClientValidationCount { get; set; }
    public long LifetimeValidationCount { get; set; }
    public string IpAddress { get; set; }
}