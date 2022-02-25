using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ItemService : PlatformMongoService<Item>
	{
		public ItemService() : base("items") { }

		// public Item[] GetItemsFor(string accountId) => Find(item => item.AccountId == accountId).ToArray();
		public Item[] GetItemsFor(string accountId) => _collection
			.Find(filter: new FilterDefinitionBuilder<Item>().Eq(Item.DB_KEY_ACCOUNT_ID, ObjectId.Parse(accountId)))
			.ToList()
			.ToArray();

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

		public async Task<bool> BulkDeleteAsync(Item[] items, IClientSessionHandle session, int retries = 5)
		{
			try
			{
				// See comment in ComponentService.UpdateAsync() for below sleep explanation.
				Thread.Sleep(new Random().Next(0, (int)Math.Pow(2, 6 - retries)));
				await _collection.DeleteManyAsync(session, Builders<Item>.Filter.In(item => item.Id, items.Select(item => item.Id)));
				return true;
			}
			catch (MongoCommandException e)
			{
				if (retries > 0)
					return await BulkDeleteAsync(items, session, --retries);
				Log.Error(Owner.Will, $"Could not delete items.", data: new
				{
					Detail = $"Session state invalid, even after retrying {retries} times with exponential backoff."
				}, exception: e);
				return false;
			}
		}

		public async Task<bool> BulkUpdateAsync(Item[] items, IClientSessionHandle session, int retries = 5)
		{
			List<WriteModel<Item>> bulk = new List<WriteModel<Item>>();
			bulk.AddRange(items.Select(item => new ReplaceOneModel<Item>(Builders<Item>.Filter.Where(dbItem => dbItem.Id == item.Id), item)
			{
				IsUpsert = true
			}));

			try
			{
				// See comment in ComponentService.UpdateAsync() for below sleep explanation.
				Thread.Sleep(new Random().Next(0, (int)Math.Pow(2, 6 - retries)));
				await _collection.BulkWriteAsync(session, bulk);
				return true;
			}
			catch (MongoCommandException e)
			{
				if (retries > 0)
					return await BulkUpdateAsync(items, session, --retries);
				Log.Error(Owner.Will, $"Could not update items.", data: new
				{
					Detail = $"Session state invalid, even after retrying {retries} times with exponential backoff."
				}, exception: e);
				return false;
			}
		}

		public void Delete(Player player) => _collection.DeleteMany(new FilterDefinitionBuilder<Item>().Eq(Item.DB_KEY_ACCOUNT_ID, player.AccountId));
	}
}