using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class MultiplayerService : PlatformMongoService<Component>
	{
		public MultiplayerService() : base("c_multiplayer") { }
	}
}