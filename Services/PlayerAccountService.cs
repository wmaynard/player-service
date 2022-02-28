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
		public PlayerAccountService() : base("player") { }

		public Player Find(string accountId) => FindOne(player => player.Id == accountId);

		public Player[] DirectoryLookup(params string[] accountIds) => _collection
			.Find(Builders<Player>.Filter.In(player => player.Id, accountIds))
			.ToList()
			.ToArray();

		/// <summary>
		/// When using SSO, this update gets called, which unifies all screennames.  New devices generate new screennames and need to be updated
		/// to reflect the account they're linked to.
		/// </summary>
		/// <param name="screenname"></param>
		/// <param name="accountId"></param>
		/// <returns></returns>
		public int SyncScreenname(string screenname, string accountId) => (int)_collection.UpdateMany(
			filter: player => player.Id == accountId || player.AccountIdOverride == accountId,
			update: Builders<Player>.Update.Set(player => player.Screenname, screenname)
		).ModifiedCount;
	}
}