using System;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions.Login;
using PlayerService.Services;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class LoginDiagnosis : PlatformDataModel
{
    public bool AccountLocked { get; set; }
    public bool EmailNotLinked { get; set; }
    public bool EmailNotConfirmed { get; set; }
    public bool EmailCodeExpired { get; set; }
    public bool EmailInUse { get; set; }
    public bool EmailInvalid { get; set; }
    public bool PasswordInvalid { get; set; }
    public bool CodeInvalid { get; set; }
    public bool DuplicateAccount { get; set; }
    public bool DeviceMismatch { get; set; }
    public bool Maintenance { get; set; }
    public bool Other { get; set; }
    public string Message { get; set; }
    public bool NotFound { get; set; }
    public bool InvalidSso { get; set; }
    public bool TokenUnavailable { get; set; }
    
    public ErrorCode Code { get; set; }
    [BsonIgnore]
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string StackTrace { get; set; }
    
    public RumbleJson Data { get; set; }

    public LoginDiagnosis(PlatformException ex)
    {
        Data = new RumbleJson();
        
        Maintenance = ex.Code == ErrorCode.DownForMaintenance;
        EmailNotLinked = ex.Code == ErrorCode.RumbleAccountMissing;
        EmailNotConfirmed = ex.Code == ErrorCode.RumbleAccountUnconfirmed;
        EmailCodeExpired = ex.Code == ErrorCode.ConfirmationCodeExpired;
        EmailInUse = ex.Code == ErrorCode.AccountAlreadyOwned;
        PasswordInvalid = ex.Code == ErrorCode.Unauthorized;
        CodeInvalid = ex.Code == ErrorCode.ConfirmationCodeInvalid;
        DuplicateAccount = ex.Code is ErrorCode.MongoUnexpectedFoundCount or ErrorCode.AccountAlreadyLinked;
        DeviceMismatch = ex.Code == ErrorCode.DeviceMismatch;
        EmailInvalid = ex.Code == ErrorCode.EmailInvalidOrBanned;
        AccountLocked = ex.Code == ErrorCode.Locked;
        
        NotFound = ex.Code is ErrorCode.GoogleAccountMissing or ErrorCode.PlariumAccountMissing or ErrorCode.AppleAccountMissing;
        InvalidSso = ex.Code is ErrorCode.GoogleValidationFailed or ErrorCode.PlariumValidationFailed or ErrorCode.AppleValidationFailed;
        TokenUnavailable = ex.Code == ErrorCode.ExternalLibraryFailure;

        Message = ex.Message;
        Code = ex.Code;
        
        EvaluateOther();

        if (!PlatformEnvironment.IsProd)
            StackTrace = ex.StackTrace;
        if (AccountLocked)
            Data["secondsRemaining"] = ((LockoutException)ex).SecondsRemaining;
        if (Other)
            Log.Warn(Owner.Will, $"Unable to provide a detailed login diagnosis.  {ex.Message}.", exception: ex);
    }

    private void EvaluateOther()
    {
        try
        {
            Other = !GetType()
                .GetProperties()
                .Where(info => info.PropertyType == typeof(bool) && info.Name != nameof(Other))
                .Select(info => (bool)(info.GetValue(this) ?? false))
                .Aggregate((a, b) => a || b);
        }
        catch (Exception e)
        {
            Log.Warn(Owner.Will, "Unable to parse other bools from the login diagnosis; Other will be true.", exception: e);
            Other = true;
        }
    }
}