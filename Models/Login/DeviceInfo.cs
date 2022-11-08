using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class DeviceInfo : PlatformDataModel
{
    private const string DB_KEY_CLIENT_VERSION = "vClient";
    private const string DB_KEY_DATA_VERSION = "vData";
    private const string DB_KEY_INSTALL_ID = "install";
    private const string DB_KEY_LANGUAGE = "lang";
    private const string DB_KEY_OPERATING_SYSTEM_VERSION = "vOS";
    private const string DB_KEY_TYPE = "t";

    public const string FRIENDLY_KEY_CLIENT_VERSION = "clientVersion";
    public const string FRIENDLY_KEY_DATA_VERSION = "dataVersion";
    public const string FRIENDLY_KEY_INSTALL_ID = "installId";
    public const string FRIENDLY_KEY_LANGUAGE = "language";
    public const string FRIENDLY_KEY_OS_VERSION = "osVersion";
    public const string FRIENDLY_KEY_TYPE = "type";
    
    [BsonElement(DB_KEY_CLIENT_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_VERSION)]
    public string ClientVersion { get; set; }
    
    [BsonElement(DB_KEY_DATA_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA_VERSION)]
    public string DataVersion { get; set; }
    
    [BsonElement(DB_KEY_INSTALL_ID)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_INSTALL_ID)]
    [SimpleIndex]
    [CompoundIndex(group: "Ponzu", priority: 10)]
    public string InstallId { get; set; }
    
    [BsonElement(DB_KEY_LANGUAGE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_LANGUAGE)]
    [CompoundIndex(group: "Ponzu", priority: 5)]
    public string Language { get; set; }
    
    [BsonElement(DB_KEY_OPERATING_SYSTEM_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_OS_VERSION)]
    public string OperatingSystem { get; set; }
    
    [BsonElement(DB_KEY_TYPE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE)]
    public string Type { get; set; }
}