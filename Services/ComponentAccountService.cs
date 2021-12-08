using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ComponentAccountService : PlatformMongoService<Component>
	{
		public ComponentAccountService() : base("c_account") { }
	}
}