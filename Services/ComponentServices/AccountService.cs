using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public class AccountService : ComponentService
	{
		public const string DB_KEY_SCREENNAME = "accountName";
		public AccountService() : base(Component.ACCOUNT) { }
	}
}