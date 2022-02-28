using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors.Infrastructure;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices
{
	public abstract class ComponentService : PlatformMongoService<Component>
	{
		private string Name { get; set; }
		protected ComponentService(string name) : base("c_" + name) => Name = name;

		public Component Lookup(string accountId)
		{
			Component output = FindOne(component => component.AccountId == accountId) ?? Create(new Component(accountId));
			output.Name = Name;
			return output;
		}

		public async Task<Component> LookupAsync(string accountId)
		{
			Component output = _collection
				.FindAsync(component => component.AccountId == accountId).Result.FirstOrDefault()
				?? Create(new Component(accountId));
			output.Name = Name;
			return output;
		}

		public void Delete(Player player) => _collection.DeleteMany(new FilterDefinitionBuilder<Component>().Eq(Component.DB_KEY_ACCOUNT_ID, player.AccountId));

		public Component[] Find(IEnumerable<string> accountIds) => _collection
			.Find(Builders<Component>.Filter.In(component => component.AccountId, accountIds))
			.ToList()
			.ToArray();

		public async Task<bool> UpdateAsync(string accountId, GenericData data, IClientSessionHandle session, int retries = 5)
		{
			try
			{
				// TODO: This is a kluge to get 1.13 out.  This should be resolved properly later.
				// This is super janky.  However, Mongo's driver appears to have asynchronous conflicts when using
				// the same session.  Without this Sleep(), there's about a 30-50% chance of the command failing, with a message
				// similar to "The active transaction number is -1."
				// Adding the random sleep makes this almost impossible to reproduce locally, but can still happen.
				// Increasing the duration makes it less likely, but every duration increase also means a longer response time
				// to the client.  Retries are both more reliable and faster.
				Thread.Sleep(new Random().Next(0, (int)Math.Pow(2, 6 - retries)));

				await _collection
					.FindOneAndUpdateAsync<Component>(
						session: session,
						filter: component => component.AccountId == accountId,
						update: Builders<Component>.Update.Set(component => component.Data, data),
						options: new FindOneAndUpdateOptions<Component>()
						{
							IsUpsert = true
						}
					);
				return true;
			}
			catch (MongoCommandException e)
			{
				if (retries > 0)
					return await UpdateAsync(accountId, data, session, --retries);
				Log.Error(Owner.Will, $"Could not update component {Name}.", data: new
				{
					Detail = $"Session state invalid, even after retrying {retries} times with exponential backoff."
				}, exception: e);
				return false;
			}
			
		}
	}
}