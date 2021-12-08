using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class HeroService : PlatformMongoService<Component>
	{
		public HeroService() : base("c_hero") { }
	}
}