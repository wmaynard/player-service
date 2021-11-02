using System;
using System.Linq.Expressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Newtonsoft.Json;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class InstallationService : GroovyUpgradeService<Installation>
	{
		public InstallationService() : base("net_installations", "player") { }
	}
}