using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class AbTestService : ComponentService
	{
		public AbTestService() : base(Component.AB_TEST) { }
	}
}