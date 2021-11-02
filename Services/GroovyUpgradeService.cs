using System;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Driver;
using PlayerService.Models;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public abstract class GroovyUpgradeService<T> : PlatformMongoService<T> where T : GroovyUpgradeDocument
	{
		protected readonly IMongoCollection<T> _oldCollection;
		
		protected GroovyUpgradeService(string newCollection, string oldCollection) : base(newCollection)
		{
			if (newCollection == oldCollection)
				throw new Exception("Collection names cannot be identical for a GroovyUpgradeService.");
			
			_oldCollection = _database.GetCollection<T>(oldCollection); 
		}

		public override void Update(T document)
		{
			if (document.Upgrade)
				Create(document);
			else
				base.Update(document);
		}

		public override T[] Find(Expression<Func<T, bool>> filter)
		{
			T[] output = base.Find(filter);
			T[] old = _oldCollection.Find(filter).ToList().ToArray(); // TODO: This is going to create dupes everywhere
			return output.Union(old).ToArray();
		}

		public override T FindOne(Expression<Func<T, bool>> filter)
		{
			T output = base.FindOne(filter);
			if (output != null)
				return output;
			output = _oldCollection.Find(filter).FirstOrDefault();
			output.Upgrade = true;
			return output;
		}
	}
}