using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Rumble.Platform.Common.Web;

namespace PlayerService.Models
{
	
	public abstract class GroovyUpgradeDocument : PlatformCollectionDocument
	{
		[BsonIgnore]
		[JsonIgnore]
		public bool Upgrade { get; internal set; }
	}
}