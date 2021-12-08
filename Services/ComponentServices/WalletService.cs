using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class WalletService : PlatformMongoService<Component>
	{
		public WalletService() : base("c_wallet") { }
	}
}