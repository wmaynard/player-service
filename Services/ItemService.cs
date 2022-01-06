using System.Linq;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ItemService : PlatformMongoService<Item>
	{
		public ItemService() : base("items") { }

		public Item[] GetItemsFor(string accountId) => Find(item => item.AccountId == accountId).ToArray();
	}
}