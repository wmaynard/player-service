using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public abstract class ComponentService : PlatformMongoService<Component>
	{
		private string Name { get; set; }
		protected ComponentService(string name) : base("c_" + name) => Name = name;

		public Component Lookup(string accountId)
		{
			Component output = FindOne(component => component.AccountId == accountId) ?? Create(new Component(accountId));
			output.Name = Name;
			return output;
		}
	}
}