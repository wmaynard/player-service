using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public abstract class ComponentService : PlatformMongoService<Component>
	{
		protected ComponentService(string name) : base("c_" + name) { }

		public Component Lookup(string accountId) => FindOne(component => component.AccountId == accountId) ?? Create(new Component(accountId));
	}
}