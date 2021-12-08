using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class TutorialService : PlatformMongoService<Component>
	{
		public TutorialService() : base("c_tutorial") { }
	}
}