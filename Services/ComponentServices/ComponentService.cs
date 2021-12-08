using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public abstract class ComponentService : PlatformMongoService<Component>
	{
		protected ComponentService(string collection) : base(collection) { }

		public Component Lookup(string accountId) => FindOne(component => component.AccountId == accountId);
	}
}