using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentStoreService : PlatformMongoService<Component>
	{
		public ComponentStoreService() : base("c_store") { }
	}
}