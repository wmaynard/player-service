using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class WorldService : PlatformMongoService<Component>
	{
		public WorldService() : base("c_world") { }
	}
}