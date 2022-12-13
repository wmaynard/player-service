using System.Collections.Generic;
using System.Linq;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Exceptions.Login;
using PlayerService.Models;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Exceptions.Mongo;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Services.ComponentServices;

public class WalletService : ComponentService
{
	public WalletService() : base(Component.WALLET) { }

	public bool SetCurrency(string accountId, string name, long amount, int version)
	{
		Component wallet = Lookup(accountId);
		List<RumbleJson> currencies = wallet.Data.Require<List<RumbleJson>>("currencies");
		RumbleJson currency = currencies.FirstOrDefault(currency => currency.Require<string>("currencyId") == name);
		if (currency != null)
		{
			currencies.Remove(currency);
			currency["amount"] = amount;
			currencies.Add(currency);
		}
		else
		{
			currencies.Add(new RumbleJson
			{
				{ "currencyId", name },
				{ "amount", amount }
			});
		}
			
		wallet.Data["currencies"] = currencies;
		wallet.Version = version;

		long affected = _collection.UpdateOne(
			filter: component => component.Version == version - 1 && component.AccountId == accountId,
			update: Builders<Component>.Update.Set(component => component.Data, wallet.Data)
		).ModifiedCount;

		return affected switch
		{
			0 => throw new CurrencyNotUpdatedException(accountId, name),
			1 => true,
			_ => throw new RecordsAffectedException(expected: 1, affected)
		};
		
		// TODO: Would be great to do this purely in a Mongo query, but this was done for a rapid and temporary fix.
	}
}