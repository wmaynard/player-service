using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class ComponentStoreService : PlatformMongoService<Component>
	{
		public ComponentStoreService() : base("c_store") { }
	}
}