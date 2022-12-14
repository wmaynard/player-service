using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Models.Login;

public class LoginDiagnosis : PlatformDataModel
{
    public bool EmailNotLinked { get; set; }
    public bool EmailNotConfirmed { get; set; }
    public bool EmailCodeExpired { get; set; }
    public bool EmailInUse { get; set; }
    public bool PasswordInvalid { get; set; }
    public bool CodeInvalid { get; set; }
    public bool DuplicateAccount { get; set; }
    public bool Maintenance { get; set; }
    public bool Other { get; set; }
    public string Message { get; set; }
    public ErrorCode Code { get; set; }

    public LoginDiagnosis(PlatformException ex)
    {
        Maintenance = ex.Code == ErrorCode.DownForMaintenance;
        EmailNotLinked = ex.Code == ErrorCode.RumbleAccountMissing;
        EmailNotConfirmed = ex.Code == ErrorCode.RumbleAccountUnconfirmed;
        EmailCodeExpired = ex.Code == ErrorCode.ConfirmationCodeExpired;
        EmailInUse = ex.Code == ErrorCode.AccountAlreadyOwned;
        PasswordInvalid = ex.Code == ErrorCode.Unauthorized;
        CodeInvalid = ex.Code == ErrorCode.ConfirmationCodeInvalid;
        DuplicateAccount = ex.Code == ErrorCode.MongoUnexpectedFoundCount;
        
        Other = !(Maintenance || EmailNotLinked || EmailNotConfirmed || EmailCodeExpired || PasswordInvalid || DuplicateAccount || CodeInvalid);
        Message = ex.Message;
        Code = ex.Code;
    }
}