using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class ComponentHeroService : PlatformMongoService<Component>
	{
		public ComponentHeroService() : base("c_hero") { }
	}
}