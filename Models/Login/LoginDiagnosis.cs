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
    public bool PasswordInvalid { get; set; }
    public bool DuplicateAccount { get; set; }
    public bool Other { get; set; }
    public string Message { get; set; }

    public LoginDiagnosis(PlatformException ex)
    {
        EmailNotLinked = ex.Code == ErrorCode.RumbleAccountMissing;
        EmailNotConfirmed = ex.Code == ErrorCode.RumbleAccountUnconfirmed;
        EmailCodeExpired = ex.Code == ErrorCode.ConfirmationCodeExpired;
        PasswordInvalid = ex.Code == ErrorCode.Unauthorized;
        DuplicateAccount = ex.Code == ErrorCode.MongoUnexpectedFoundCount;
        Other = !(EmailNotLinked || EmailNotConfirmed || EmailCodeExpired || PasswordInvalid || DuplicateAccount);
        Message = ex.Message;
    }
}