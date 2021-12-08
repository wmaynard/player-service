using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentHeroService : PlatformMongoService<Component>
	{
		public ComponentHeroService() : base("c_hero") { }
	}
}