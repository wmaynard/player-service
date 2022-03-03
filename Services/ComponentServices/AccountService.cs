using PlayerService.Models;

namespace PlayerService.Services.ComponentServices;

public class AccountService : ComponentService
{
	public const string DB_KEY_SCREENNAME = "accountName";
	public AccountService() : base(Component.ACCOUNT) { }
}