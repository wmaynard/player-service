using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class ComponentEquipmentService : PlatformMongoService<Component>
	{
		public ComponentEquipmentService() : base("c_equipment") { }
	}
}