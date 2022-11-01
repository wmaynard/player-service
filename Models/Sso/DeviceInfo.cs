using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Models.Sso;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Data;

namespace PlayerService.DeviceInfo.Models;

public class DeviceInfo : PlatformDataModel
{
    [BsonElement("cv")]
    [JsonInclude, JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; }
    
    [BsonElement("dv")]
    [JsonInclude, JsonPropertyName("dataVersion")]
    public string DataVersion { get; set; }
    
    [BsonElement("lsi")]
    [JsonInclude, JsonPropertyName("installId")]
    public string InstallId { get; set; }
    
    [BsonElement("lang")]
    [JsonInclude, JsonPropertyName("language")]
    public string Language { get; set; }
    
    [BsonElement("os")]
    [JsonInclude, JsonPropertyName("osVersion")]
    public string OperatingSystem { get; set; }
    
    [BsonElement("t")]
    [JsonInclude, JsonPropertyName("type")]
    public string Type { get; set; }
}