using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class StoreService : PlatformMongoService<Component>
	{
		public StoreService() : base("c_store") { }
	}
}