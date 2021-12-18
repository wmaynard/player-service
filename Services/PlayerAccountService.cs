using System;
using System.Linq.Expressions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class PlayerAccountService : PlatformMongoService<Player>
	{
		public PlayerAccountService() : base("player_temp") { }

		public Player Find(string accountId) => FindOne(player => player.Id == accountId);
	}
}