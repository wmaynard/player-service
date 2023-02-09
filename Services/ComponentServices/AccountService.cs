using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using PlayerService.Models;
using RCL.Logging;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services.ComponentServices;

public class AccountService : ComponentService
{
	public const string DB_KEY_SCREENNAME = "accountName";
	public AccountService() : base(Component.ACCOUNT) { }

	public int SetScreenname(string accountId, string screenname)
	{
		try
		{
			Component component = _collection
				.Find(Builders<Component>.Filter.Eq(component => component.AccountId, accountId))
				.FirstOrDefault();
			component.Data["accountName"] = screenname;
			component.Version++;
			Update(component);
			
			return 1;
		}
		catch (Exception e)
		{
			Log.Warn(Owner.Will, "Unable to change screenname.", data: new
			{
				AccountId = accountId
			}, exception: e);
			return 0;
		}
	}
}