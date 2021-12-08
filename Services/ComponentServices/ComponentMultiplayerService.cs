using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class ComponentMultiplayerService : PlatformMongoService<Component>
	{
		public ComponentMultiplayerService() : base("c_multiplayer") { }
	}
}