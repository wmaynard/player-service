using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class AbTestService : PlatformMongoService<Component>
	{
		public AbTestService() : base("c_abTest") { }
	}
}