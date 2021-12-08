using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class ComponentAbTestService : PlatformMongoService<Component>
	{
		public ComponentAbTestService() : base("c_abTest") { }
	}
}