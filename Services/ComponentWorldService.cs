using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentWorldService : PlatformMongoService<Component>
	{
		public ComponentWorldService() : base("c_world") { }
	}
}