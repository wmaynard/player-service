using System.Collections.Generic;
using System.Linq;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services.ComponentServices;

public class WalletService : ComponentService
{
	public WalletService() : base(Component.WALLET) { }

	public bool SetCurrency(string accountId, string name, long amount, int version)
	{
		Component wallet = Lookup(accountId);
		List<GenericData> currencies = wallet.Data.Require<List<GenericData>>("currencies");
		GenericData currency = currencies.FirstOrDefault(currency => currency.Require<string>("currencyId") == name);
		if (currency != null)
		{
			currencies.Remove(currency);
			currency["amount"] = amount;
			currencies.Add(currency);
		}
		else
		{
			currencies.Add(new GenericData
			{
				{ "currencyId", name },
				{ "amount", amount }
			});
		}
			
		wallet.Data["currencies"] = currencies;
		wallet.Version = version;

		FilterDefinition<Component> filter = Builders<Component>.Filter.And(
			Builders<Component>.Filter.Eq(component => component.Version, version - 1),
			Builders<Component>.Filter.Eq(component => component.AccountId, accountId)
		);

		List<Component> foo = _collection.Find(filter).ToList();

		long affected = _collection.UpdateOne(
			filter: component => component.Version == version - 1 && component.AccountId == accountId,
			update: Builders<Component>.Update.Set(component => component.Data, wallet.Data)
		).ModifiedCount;

		return affected switch
		{
			0 => throw new PlatformException("No records affected.  The component version may have been changed by the server.", code: ErrorCode.MongoRecordNotFound),
			1 => true,
			_ => throw new PlatformException("More than one record was affected!  This should be impossible!", code: ErrorCode.RequiredFieldMissing)
		};
		
		// TODO: Would be great to do this purely in a Mongo query, but this was done for a rapid and temporary fix.
	}
}