using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using PlayerService.Exceptions;
using PlayerService.Models;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services.ComponentServices;

public abstract class ComponentService : PlatformMongoService<Component>
{
	private new string Name { get; set; } // TODO: done to silence warning, but needs to be renamed.
	protected ComponentService(string name) : base("c_" + name) => Name = name;

	public Component Lookup(string accountId)
	{
		Component output = FindOne(component => component.AccountId == accountId) ?? Create(new Component(accountId));
		output.Name = Name;
		return output;
	}

	public Task<Component> LookupAsync(string accountId)
	{
		Component output = _collection
			.FindAsync(component => component.AccountId == accountId).Result.FirstOrDefault()
			?? Create(new Component(accountId));
		output.Name = Name;
		return Task.FromResult(output);
	}

	public void Delete(Player player) => _collection.DeleteMany(new FilterDefinitionBuilder<Component>().Eq(Component.DB_KEY_ACCOUNT_ID, player.AccountId));

	public List<Component> Find(IEnumerable<string> accountIds) => _collection
		.Find(Builders<Component>.Filter.In(component => component.AccountId, accountIds))
		.ToList();

	public async Task<bool> UpdateAsync(string accountId, GenericData data, IClientSessionHandle session, int? version, int retries = 5)
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
			
			UpdateDefinitionBuilder<Component> builder = Builders<Component>.Update;
			UpdateDefinition<Component> update = builder.Set(component => component.Data, data);
			if (VersionNumberProvided(accountId, version))
			{
				update = builder.Combine(update, builder.Set(component => component.Version, version));
			}

			await _collection
				.FindOneAndUpdateAsync<Component>(
					session: session,
					filter: component => component.AccountId == accountId,
					update: update,
					options: new FindOneAndUpdateOptions<Component>()
					{
						IsUpsert = true,
						ReturnDocument = ReturnDocument.After
					}
				);
			return true;
		}
		catch (MongoCommandException e)
		{
			Log.Local(Owner.Will, e.Message);
			if (retries > 0)
				return await UpdateAsync(accountId, data, session, version, --retries);
			Log.Error(Owner.Will, $"Could not update component {Name}.", data: new
			{
				Detail = $"Session state invalid, even after retrying {retries} times with exponential backoff."
			}, exception: e);
			return false;
		}
	}

	public bool VersionNumberProvided(string accountId, int? version)
	{
		// Getting the current version for any update might be useful, but until it's requested,
		// we'll return early to avoid one Mongo hit.
		if (version is null or 0)
		{
			// await Record(accountId, new AuditLog());
			return false;
		}

		int current = _collection
			.Find(filter: component => component.AccountId == accountId)
			.Project(Builders<Component>.Projection.Expression(component => component.Version))
			.First();

		// await Record(accountId, new AuditLog(current, (int)version));
		
		if (current != version - 1)
			throw new ComponentVersionException(Name, currentVersion: current, updateVersion: (int)version);

		return true;
	}
}