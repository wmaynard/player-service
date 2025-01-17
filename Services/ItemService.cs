using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services;

public class ItemService : PlatformMongoService<Item>
{
	public ItemService() : base("items") { }

	public List<Item> GetItemsFor(string accountId, string[] ids = null, string[] types = null)
	{
		ids ??= Array.Empty<string>();
		types ??= Array.Empty<string>();
		
		FilterDefinition<Item> aid = Builders<Item>.Filter.Eq(item => item.AccountId, accountId);
		FilterDefinition<Item> byId = ids.Any()
			? Builders<Item>.Filter.In(item => item.ItemId, ids)
			: null;
		FilterDefinition<Item> byType = types.Any()
			? Builders<Item>.Filter.In(item => item.Type, types)
			: null;
		FilterDefinition<Item> or = byId != null && byType != null
			? Builders<Item>.Filter.Or(byId, byType)
			: null;

		FilterDefinition<Item> and = null;
		if (or != null)
			and = Builders<Item>.Filter.And(aid, or);
		else if (byId != null)
			and = Builders<Item>.Filter.And(aid, byId);
		else if (byType != null)
			and = Builders<Item>.Filter.And(aid, byType);

		return _collection
			.Find(filter: and ?? aid)
			.ToList();
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

	public async Task<bool> InsertAsync(IEnumerable<Item> items, IClientSessionHandle session, int retries = 5)
	{
		if (!items.Any())
			return true;
		try
		{
			Thread.Sleep(new Random().Next(0, (int)Math.Pow(2, 6 - retries)));
			await _collection.InsertManyAsync(
				session: session,
				documents: items.Where(item => item.Id == null)
			);
			return true;
		}
		catch (MongoCommandException e)
		{
			if (retries > 0)
				return await BulkUpdateAsync(items, session, --retries);
			Log.Error(Owner.Will, $"Could not insert items.", data: new
			{
				Detail = $"Session state invalid, even after retrying {retries} times with exponential backoff."
			}, exception: e);
			return false;
		}
	}
	
	public async Task<bool> BulkUpdateAsync2(IEnumerable<Item> items, IClientSessionHandle session, int retries = 5)
	{
		if (!items.Any())
			return true;
		List<WriteModel<Item>> bulk = new List<WriteModel<Item>>();

		bulk.AddRange(items.Select(item => new UpdateOneModel<Item>(
			filter: Builders<Item>.Filter.Eq(dbItem => dbItem.Id, item.Id), 
			update: Builders<Item>.Update
				.Set(dbItem => dbItem.AccountId, item.AccountId)
				.Set(dbItem => dbItem.ItemId, item.ItemId)
				.Set(dbItem => dbItem.Type, item.Type)
				.Set(dbItem => dbItem.Data, item.Data)
		)
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
	
	
	public async Task<bool> BulkUpdateAsync(IEnumerable<Item> items, IClientSessionHandle session, int retries = 5)
	{
		if (!items.Any())
			return true;
		List<WriteModel<Item>> bulk = new List<WriteModel<Item>>();

		// var filter = Builders<Item>.Filter.And(
		// 	Builders<Item>.Filter.Eq(item => item.AccountId, item.AccountId),
		// 	Builders<Item>.Filter.Eq(item => item.ItemId, item.ItemId));
		bulk.AddRange(items.Select(item => new UpdateOneModel<Item>(
			Builders<Item>.Filter.And(
				Builders<Item>.Filter.Eq(dbItem => dbItem.AccountId, item.AccountId),
				Builders<Item>.Filter.Eq(dbItem => dbItem.ItemId, item.ItemId)), 
			update: Builders<Item>.Update
				.Set(dbItem => dbItem.AccountId, item.AccountId)
				.Set(dbItem => dbItem.ItemId, item.ItemId)
				.Set(dbItem => dbItem.Type, item.Type)
				.Set(dbItem => dbItem.Data, item.Data)
		)
		{
			IsUpsert = true,
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

	public void Delete(Player player) => _collection
		.DeleteMany(new FilterDefinitionBuilder<Item>().Eq(Item.DB_KEY_ACCOUNT_ID, player.AccountId));
}