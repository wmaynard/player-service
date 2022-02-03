using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ItemService : PlatformMongoService<Item>
	{
		public ItemService() : base("items") { }

		// public Item[] GetItemsFor(string accountId) => Find(item => item.AccountId == accountId).ToArray();
		public Item[] GetItemsFor(string accountId)
		{
			return _collection.Find(new FilterDefinitionBuilder<Item>().Eq(Item.DB_KEY_ACCOUNT_ID, ObjectId.Parse(accountId))).ToList().ToArray();
		}

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

			Item updated = null;
			if (session != null)
				updated = _collection.FindOneAndUpdate<Item>(session,
					filter: dbItem => dbItem.AccountId == item.AccountId && dbItem.ItemId == item.ItemId,
					update: update, 
					options: new FindOneAndUpdateOptions<Item>() { ReturnDocument = ReturnDocument.After, IsUpsert = true}
				);
			else
				updated = _collection.FindOneAndUpdate<Item>(
					filter: dbItem => dbItem.AccountId == item.AccountId && dbItem.ItemId == item.ItemId,
					update: update, 
					options: new FindOneAndUpdateOptions<Item>() { ReturnDocument = ReturnDocument.After, IsUpsert = true}
				);
		}

		public void Delete(Player player)
		{
			_collection.DeleteMany(new FilterDefinitionBuilder<Item>().Eq(Item.DB_KEY_ACCOUNT_ID, player.AccountId));
		}
	}
}