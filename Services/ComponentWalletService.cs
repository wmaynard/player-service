using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentWalletService : PlatformMongoService<Component>
	{
		public ComponentWalletService() : base("c_wallet") { }
	}
}