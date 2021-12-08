using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentSummaryService : PlatformMongoService<Component>
	{
		public ComponentSummaryService() : base("c_summary") { }
	}
}