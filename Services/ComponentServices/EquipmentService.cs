using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class EquipmentService : PlatformMongoService<Component>
	{
		public EquipmentService() : base("c_equipment") { }
	}
}