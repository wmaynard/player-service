using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentTutorialService : PlatformMongoService<Component>
	{
		public ComponentTutorialService() : base("c_tutorial") { }
	}
}