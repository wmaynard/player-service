using System;
using System.Linq.Expressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class InstallationService : PlatformMongoService<Installation>
	{
		public InstallationService() : base("player") { }
	}
}