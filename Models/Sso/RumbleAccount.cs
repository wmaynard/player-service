using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;

namespace PlayerService.Models.Sso;

public class RumbleAccount : PlatformDataModel
{
    public string Email { get; set; }
    
    [BsonElement]
    [JsonInclude, JsonPropertyName("username")]
    public string Username { get; set; }    
    
    [BsonElement]
    [JsonInclude, JsonPropertyName("hash")]
    public string Hash { get; set; }
    public string PendingHash { get; set; }
    public long CodeExpiration { get; set; }
    public string ConfirmationCode { get; set; }
    public AccountStatus Status { get; set; }

    [Flags]
    public enum AccountStatus
    {
        None                = 0b0000,
        NeedsConfirmation   = 0b0001, 
        Confirmed           = 0b0010,
        ResetRequested      = 0b0110,
        PasswordResetPrimed      = 0b1010,
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