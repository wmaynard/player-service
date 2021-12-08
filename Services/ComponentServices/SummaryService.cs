using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class SummaryService : PlatformMongoService<Component>
	{
		public SummaryService() : base("c_summary") { }
	}
}