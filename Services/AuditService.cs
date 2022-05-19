using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Services;

namespace PlayerService.Services;

public class AuditService : PlatformMongoService<AuditLog>
{
	public AuditService() : base("audits"){}

	public async Task<AuditLog> Record(string accountId, string component, int currentVersion = 0, int updateVersion = 0) => await _collection
		.FindOneAndUpdateAsync<AuditLog>(
			filter: audit => audit.AccountId == accountId && audit.ComponentName == component,
			update: Builders<AuditLog>.Update.AddToSet(log => log.Entries, new AuditLog.Entry
			{
				CurrentVersion = currentVersion,
				NextVersion = updateVersion
			}),
			options: new FindOneAndUpdateOptions<AuditLog>
			{
				IsUpsert = true,
				ReturnDocument = ReturnDocument.After
			}
		);


	public List<AuditLog> AuditPlayer(string accountId) => _collection.Find(audit => audit.AccountId == accountId).ToList();
}