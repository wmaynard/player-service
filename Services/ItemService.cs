using System.Linq;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ItemService : PlatformMongoService<Item>
	{
		public ItemService() : base("items") { }

		public Item[] GetItemsFor(string accountId) => Find(item => item.AccountId == accountId).ToArray();

		public void UpdateItem(Item item)
		{
			if (item.Id != null)
			{
				Update(item);
				return;
			}
			
			StartTransactionIfRequested(out IClientSessionHandle session);
			UpdateDefinition<Item> update = Builders<Item>.Update
				.Set(i => i.Data, item.Data)
				.Set(i => i.Type, item.Type)
				.Set(i => i.AccountId, item.AccountId);
			
			if (session != null)
				_collection.FindOneAndUpdate<Item>(session,
					filter: dbItem => dbItem.AccountId == item.AccountId && dbItem.ItemId == item.ItemId,
					update: update, 
					options: new FindOneAndUpdateOptions<Item>() { ReturnDocument = ReturnDocument.After, IsUpsert = true}
				);
			else
				_collection.FindOneAndUpdate<Item>(
					filter: dbItem => dbItem.AccountId == item.AccountId && dbItem.ItemId == item.ItemId,
					update: update, 
					options: new FindOneAndUpdateOptions<Item>() { ReturnDocument = ReturnDocument.After, IsUpsert = true}
				);
		}
	}
}