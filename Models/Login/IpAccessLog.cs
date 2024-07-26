using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace PlayerService.Models.Login;

public class IpAccessLog : PlatformCollectionDocument
{
    private const string GROUP = "email";
        
    [BsonElement("email")]
    [CompoundIndex(GROUP, priority: 1)]
    public string Email { get; set; }
        
    [BsonElement("ip")]
    [CompoundIndex(GROUP, priority: 2)]
    public string IpAddress { get; set; }
    
    [BsonElement("attempts")]
    public long[] Timestamps { get; set; }
}