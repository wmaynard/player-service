using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class RumbleAccount : PlatformDataModel
{
    private const string DB_KEY_CODE = "code";
    private const string DB_KEY_CODE_EXPIRATION = "exp";
    private const string DB_KEY_EMAIL = "email";
    private const string DB_KEY_HASH = "hash";
    private const string DB_KEY_STATUS = "status";
    private const string DB_KEY_USERNAME = "username";
    
    private const string FRIENDLY_KEY_CODE = "code";
    private const string FRIENDLY_KEY_CODE_EXPIRATION = "expiration";
    private const string FRIENDLY_KEY_EMAIL = "email";
    private const string FRIENDLY_KEY_STATUS = "status";
    private const string FRIENDLY_KEY_USERNAME = "username";

    [BsonElement(DB_KEY_CODE)]
    [JsonPropertyName(FRIENDLY_KEY_CODE)]
    public string ConfirmationCode { get; set; }
    
    [BsonElement(DB_KEY_CODE_EXPIRATION)]
    [JsonPropertyName(FRIENDLY_KEY_CODE_EXPIRATION)]
    public long CodeExpiration { get; set; }
    
    [BsonElement(DB_KEY_EMAIL)]
    [JsonPropertyName(FRIENDLY_KEY_EMAIL)]
    public string Email { get; set; }
     
    
    [BsonElement(DB_KEY_HASH)]
    [JsonIgnore]
    public string Hash { get; set; }
    
    [BsonElement(DB_KEY_STATUS)]
    [JsonPropertyName(FRIENDLY_KEY_STATUS)]
    public AccountStatus Status { get; set; }
    
    [BsonElement(DB_KEY_USERNAME)]
    [JsonPropertyName(FRIENDLY_KEY_USERNAME)]
    public string Username { get; set; }   

    [Flags]
    public enum AccountStatus
    {
        None                = 0b0000,
        NeedsConfirmation   = 0b0001, 
        Confirmed           = 0b0010,
        ResetRequested      = 0b0110,
        ResetPrimed         = 0b1010,
    }

    public static string GenerateCode(int segments = 2)
    {
        List<int> digits = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        List<string> codes = new List<string>();

        while (segments-- > 0)
        {
            if (digits.Count <= 4)
                digits = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            codes.Add(GenerateCodePart(ref digits));
        }

        return string.Join('-', codes);
    }

    private static string GenerateCodePart(ref List<int> digits)
    {
        Random rando = new Random();

        int digit1 = digits[rando.Next(0, digits.Count)];
        digits.Remove(digit1);

        int digit2 = digits[rando.Next(0, digits.Count)];
        digits.Remove(digit2);

        int repeater = rando.Next(0, 100) < 50
            ? digit1
            : digit2;

        return rando.Next(0, 100) switch
        {
            < 33 => $"{repeater}{digit1}{digit2}",
            < 66 => $"{digit1}{repeater}{digit2}",
            _ => $"{digit1}{digit2}{repeater}"
        };
    }
}