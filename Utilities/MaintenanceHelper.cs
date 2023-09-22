using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlayerService.Models.Login;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Utilities;

public static class MaintenanceHelper
{
    public static BadRequestObjectResult CreateMessage(string origin = null)
    {
        if (!string.IsNullOrWhiteSpace(origin))
            Log.Info(Owner.Will, $"An external admin consumer requested a maintenance message.", data: new
            {
                Origin = origin
            });
        return new BadRequestObjectResult(new LoginDiagnosis(new MaintenanceException()))
        {
            StatusCode = StatusCodes.Status423Locked
        };
    }
}