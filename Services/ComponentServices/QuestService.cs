using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class QuestService : PlatformMongoService<Component>
	{
		public QuestService() : base("c_quest") { }
	}
}