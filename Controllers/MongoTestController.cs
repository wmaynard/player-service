using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PlayerService.Models;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Controllers;

[ApiController, Route("player/v2/mongoTest"), RequireAuth(AuthType.ADMIN_TOKEN)]
public class MongoTestController : PlatformController
{
#pragma warning disable
	private readonly BulkTestService _bulkService;
	private readonly CollectionTestService _hulkService;
#pragma warning restore
    
	[HttpGet, IgnorePerformance]
	public ActionResult MongoItemsTest()
	{
		if (PlatformEnvironment.IsProd)
			throw new PlatformException(message: "Forbidden", code: ErrorCode.Unauthorized);
		
		int itemCount = Require<int>("itemCount");
		int tests = Require<int>("tests");
		
		Log.Local(Owner.Will, "Deleting all records", emphasis: Log.LogType.CRITICAL);
		_bulkService.DeleteAll();
		_hulkService.DeleteAll();
		
		Log.Local(Owner.Will, "Fetching Preacher", emphasis: Log.LogType.INFO);
		GenericData failure = null;
		// Read Taylor's account in prod for a wealth of items
		_apiService
			.Request("https://platform-b.prod.tower.rumblegames.com/player/v2/admin/details")
			.AddAuthorization(token: PlatformEnvironment.Require<string>("PROD_ADMIN_TOKEN"))
			.AddParameter(key: "accountId", value: "61396d1daf4b0ccddac01f26")
			.OnFailure((_, response) =>
			{
				failure = response.AsGenericData;
				Log.Local(Owner.Will, response.ErrorCode.ToString(), emphasis: Log.LogType.ERROR);
			})
			.Get(out GenericData response, out int code);

		if (!code.Between(200, 299))
			return Problem(failure);
		
		Log.Local(Owner.Will, "Preacher account retrieved.  Beginning benchmarks.");

		Item[] _base = response.Require<Item[]>(key: "items");
		List<Item> expando = new List<Item>();
		Log.Local(Owner.Will, "Multiplying items");

		while (expando.Count < itemCount)
			foreach (Item item in _base.Where(i => i.Type == "equipment"))
			{
				Item copy = PlatformDataModel.FromJSON<Item>(item.JSON);
				copy.ChangeId();
				expando.Add(copy);
				if (expando.Count >= itemCount)
					break;
			}

		Item[] items = expando.DistinctBy(item => item.Id).ToArray();
		Log.Local(Owner.Will, $"Multiplied: {expando.Count}, Items: {items.Length}");

		List<GenericData> bulks = new List<GenericData>();
		List<GenericData> hulks = new List<GenericData>();

		for (int i = 0; i < tests; i++)
		{
			Log.Local(Owner.Will, $"{tests - i} tests remaining.");
			// string account = $"account_{i}";
			bulks.Add(_bulkService.TestInsert("61396d1daf4b0ccddac01f26", items));
			hulks.Add(_hulkService.TestInsert("61396d1daf4b0ccddac01f26", items));
		}

		return Ok(new
		{
			Data = new GenericData
			{
				{ "itemCount", items.Length },
				{ "testCount", tests },
				{ "bulk", new GenericData
				{
					{ "avgPreparedTime", bulks.Average(data => data.Require<long>("prepared")) },
					{ "avgInsertedTime", bulks.Average(data => data.Require<long>("inserted")) },
					{ "avgDeletedTime", bulks.Average(data => data.Require<long>("deleted")) },
					{ "avgTotalTime", bulks.Average(data => data.Require<long>("total")) },
					{ "medianPreparedTime", bulks.Median(data => data.Require<long>("prepared")) },
					{ "medianInsertedTime", bulks.Median(data => data.Require<long>("inserted")) },
					{ "medianDeletedTime", bulks.Median(data => data.Require<long>("deleted")) },
					{ "medianTotalTime", bulks.Median(data => data.Require<long>("total")) }
				}},
				{ "component", new GenericData
				{
					{ "avgPreparedTime", hulks.Average(data => data.Require<long>("prepared")) },
					{ "avgInsertedTime", hulks.Average(data => data.Require<long>("inserted")) },
					{ "avgDeletedTime", hulks.Average(data => data.Require<long>("deleted")) },
					{ "avgTotalTime", hulks.Average(data => data.Require<long>("total")) },
					{ "medianPreparedTime", hulks.Median(data => data.Require<long>("prepared")) },
					{ "medianInsertedTime", hulks.Median(data => data.Require<long>("inserted")) },
					{ "medianDeletedTime", hulks.Median(data => data.Require<long>("deleted")) },
					{ "medianTotalTime", hulks.Median(data => data.Require<long>("total")) }
				}},
				{ "bulkVsComponent", new GenericData
				{
					{ "avgPreparedTime", bulks.Average(data => data.Require<long>("prepared")) - hulks.Average(data => data.Require<long>("prepared")) },
					{ "avgInsertedTime", bulks.Average(data => data.Require<long>("inserted")) - hulks.Average(data => data.Require<long>("inserted")) },
					{ "avgDeletedTime", bulks.Average(data => data.Require<long>("deleted")) - hulks.Average(data => data.Require<long>("deleted")) },
					{ "avgTotalTime", bulks.Average(data => data.Require<long>("total")) - hulks.Average(data => data.Require<long>("total")) },
					{ "medianPreparedTime", bulks.Median(data => data.Require<long>("prepared")) - hulks.Median(data => data.Require<long>("prepared")) },
					{ "medianInsertedTime", bulks.Median(data => data.Require<long>("inserted")) - hulks.Median(data => data.Require<long>("inserted")) },
					{ "medianDeletedTime", bulks.Median(data => data.Require<long>("deleted")) - hulks.Median(data => data.Require<long>("deleted")) },
					{ "medianTotalTime", bulks.Median(data => data.Require<long>("total")) - hulks.Median(data => data.Require<long>("total")) }
				}}
			}
		});
	}
}

public static class LINQExtension
{
	// source: https://stackoverflow.com/a/10738416
	public static double? Median<TColl, TValue>(this IEnumerable<TColl> source, Func<TColl, TValue> selector) => source.Select(selector).Median();
	public static double? Median<T>(this IEnumerable<T> source)
	{
		if(Nullable.GetUnderlyingType(typeof(T)) != null)
			source = source.Where(x => x != null);

		if (!source.Any())
			return null;

		int count = source.Count();

		source = source.OrderBy(n => n);

		int midpoint = count / 2;
		return count % 2 == 0
			? (Convert.ToDouble(source.ElementAt(midpoint - 1)) + Convert.ToDouble(source.ElementAt(midpoint))) / 2.0
			: Convert.ToDouble(source.ElementAt(midpoint));
	}
}


public class BulkTestService : PlatformMongoService<Item>
{
	public BulkTestService() : base("item_bulk_test") {}
	
	public GenericData TestInsert(string accountId, Item[] items)
	{
		long start = Timestamp.UnixTimeMS;
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
		
		long prepared = Timestamp.UnixTimeMS;
		// _collection.BulkWrite(bulk);
		_collection.InsertMany(items);

		long inserted = Timestamp.UnixTimeMS;
		_collection.DeleteMany(filter: item => item.AccountId == accountId);

		long deleted = Timestamp.UnixTimeMS;
		
		return new GenericData
		{
			{ "itemsProcessed", items.Length },
			{ "prepared", prepared - start },
			{ "inserted", inserted - prepared },
			{ "deleted", deleted - inserted },
			{ "total", Timestamp.UnixTimeMS - start }
		};
	}
	
}
public class CollectionTestService : PlatformMongoService<MongoTestModelItemCollection>
{
	public CollectionTestService() : base("item_collection_test") {}

	public GenericData TestInsert(string accountId, Item[] items)
	{
		long start = Timestamp.UnixTimeMS;
		
		MongoTestModelItemCollection test = new MongoTestModelItemCollection()
		{
			AccountId = accountId,
			Items = items
		};

		long prepared = Timestamp.UnixTimeMS;
		
		_collection.InsertOne(test);

		long inserted = Timestamp.UnixTimeMS;
		_collection.DeleteOne(filter: document => document.Id == test.Id);

		long deleted = Timestamp.UnixTimeMS;
		
		return new GenericData
		{
			{ "itemsProcessed", items.Length },
			{ "prepared", prepared - start },
			{ "inserted", inserted - prepared },
			{ "deleted", deleted - inserted },
			{ "total", Timestamp.UnixTimeMS - start }
		};
	}
}

public class MongoTestModelItemCollection : PlatformCollectionDocument
{
	[SimpleIndex(dbKey: "aid", name: "AccountId")]
	[BsonElement("aid")]
	[JsonPropertyName("accountId")]
	public string AccountId { get; set; }
	
	[JsonPropertyName("items")]
	public Item[] Items { get; set; }
	
}