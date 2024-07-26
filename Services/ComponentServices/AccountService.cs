using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services.ComponentServices;

public class AccountService : ComponentService
{
	public const string DB_KEY_SCREENNAME = "accountName";
	public AccountService() : base(Component.ACCOUNT) { }

	// NOTE: This is a kluge; the game server may overwrite this component if their session is active.
	// TD-14516: Screenname changes from Portal do not affect the account screen in-game.
	public int SetScreenname(string accountId, string screenname, bool fromAdmin)
	{
		try
		{
			Component component = _collection
				.Find(Builders<Component>.Filter.Eq(component => component.AccountId, accountId))
				.FirstOrDefault();
			component.Data["accountName"] = screenname;
			if (fromAdmin)
				component.Version++;
			Update(component);
			
			return 1;
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Will, "Unable to change screenname in account component", data: new
			{
				AccountId = accountId
			}, exception: e);
			return 0;
		}
	}

	public override long ProcessGdprRequest(TokenInfo token, string dummyText)
	{
		if (string.IsNullOrWhiteSpace(token.AccountId))
			return 0;
		Component account = Find(new[] { token.AccountId }).FirstOrDefault();

		if (account == null)
			return 0;

		account.Data["deviceInfo"] = null;
		account.Data["accountName"] = dummyText;
		
		Update(account);

		return 1;
	}
}