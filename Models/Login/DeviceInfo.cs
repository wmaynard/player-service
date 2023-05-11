using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Models.Login;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class DeviceInfo : PlatformDataModel
{
    private const string DB_KEY_CLIENT_VERSION = "vClient";
    private const string DB_KEY_DATA_VERSION = "vData";
    private const string DB_KEY_PRIVATE_KEY_CONFIRMED = "secret";
    private const string DB_KEY_HASH_STATUS = "hstat";
    private const string DB_KEY_INSTALL_ID = "install";
    private const string DB_KEY_LANGUAGE = "lang";
    private const string DB_KEY_OPERATING_SYSTEM_VERSION = "vOS";
    private const string DB_KEY_TYPE = "t";

    public const string FRIENDLY_KEY_CLIENT_VERSION = "clientVersion";
    public const string FRIENDLY_KEY_DATA_VERSION = "dataVersion";
    public const string FRIENDLY_KEY_PRIVATE_KEY = "privateKey";
    public const string FRIENDLY_KEY_INSTALL_ID = "installId";
    public const string FRIENDLY_KEY_LANGUAGE = "language";
    public const string FRIENDLY_KEY_OS_VERSION = "osVersion";
    public const string FRIENDLY_KEY_TYPE = "type";
    
    [BsonElement(DB_KEY_CLIENT_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_CLIENT_VERSION)]
    [StringLength(30, MinimumLength = 0)]
    public string ClientVersion { get; set; }
    
    [BsonElement(DB_KEY_DATA_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA_VERSION)]
    public string DataVersion { get; set; }
    
    [BsonElement(DB_KEY_INSTALL_ID)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_INSTALL_ID)]
    [SimpleIndex]
    [CompoundIndex(group: Player.INDEX_KEY_SEARCH, priority: 3)]
    public string InstallId { get; set; }
    
    [BsonElement(DB_KEY_LANGUAGE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_LANGUAGE)]
    public string Language { get; set; }
    
    [BsonElement(DB_KEY_OPERATING_SYSTEM_VERSION)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_OS_VERSION)]
    public string OperatingSystem { get; set; }
    
    [BsonElement(DB_KEY_TYPE)]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE)]
    public string Type { get; set; }

    // On hashing here: ConfirmedPrivateKey is designed to be set exactly once.
    // When a record does not have one, it's exposed publicly in the login response under the below field, PrivateKey.
    // The expectation is that the client stores this value and includes it in its device information in all future
    // requests, as a kind of paired key to go along with InstallId to secure accounts.  This field should never
    // be exposed once it's acknowledged by the client.
    [BsonElement(DB_KEY_PRIVATE_KEY_CONFIRMED)]
    [JsonIgnore]
    public string ConfirmedPrivateKey { get; set; }
    
    [BsonIgnore]
    [JsonInclude, JsonPropertyName(FRIENDLY_KEY_PRIVATE_KEY), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string PrivateKey { get; set; }

    public void CalculatePrivateKey() => PrivateKey ??= string.Join("", SHA256
        .Create()
        .ComputeHash(buffer: Encoding.UTF8.GetBytes($"{GetType().FullName}|{InstallId}|{ClientVersion}|{DataVersion}|{Language}|{OperatingSystem}|{Type}"))
        .Select(b => b.ToString("x2")));

    public void Compare(DeviceInfo other, out bool devicesIdentical, out bool keysAuthorized)
    {
        devicesIdentical = Equals(other);
        keysAuthorized = string.IsNullOrWhiteSpace(ConfirmedPrivateKey) && string.IsNullOrWhiteSpace(other?.ConfirmedPrivateKey)
            || !string.IsNullOrWhiteSpace(ConfirmedPrivateKey) && ConfirmedPrivateKey == other?.PrivateKey
            || !string.IsNullOrWhiteSpace(other?.ConfirmedPrivateKey) && other?.ConfirmedPrivateKey == PrivateKey;
    }

    public override bool Equals(object obj)
    {
        if (obj is not DeviceInfo other)
            return false;
        
        return InstallId == other.InstallId
            && Language == other.Language
            && Type == other.Type
            && ClientVersion == other.ClientVersion
            && DataVersion == other.DataVersion
            && OperatingSystem == other.OperatingSystem;
    }

    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();

        Language = Language?.Limit(10);
        Type = Type?.Limit(20);
        ClientVersion = ClientVersion?.Limit(20);
        DataVersion = DataVersion?.Limit(20);
        InstallId = InstallId.Limit(50);
        PrivateKey = PrivateKey?.Limit(50);

        if (PrivateKey != null)
            PrivateKey = Crypto.Encode(PrivateKey);
    }
}