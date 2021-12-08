using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentQuestService : PlatformMongoService<Component>
	{
		public ComponentQuestService() : base("c_quest") { }
	}
}