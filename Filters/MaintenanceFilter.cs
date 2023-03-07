using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PlayerService.Models.Login;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Filters;

/// <summary>
/// Checks dynamic config for values relevant to maintenance.  For full documentation, see MAINTENANCE_MODE.md.
/// </summary>
public class MaintenanceFilter : PlatformFilter, IActionFilter
{
    public const string KEY_MAINTENANCE = "maintenance";
    public const string KEY_MAINTENANCE_START = "maintenanceBegins";
    public const string KEY_MAINTENANCE_END = "maintenanceEnds";
    
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Health checks and admin endpoints are exempt from maintenance mode.
        string url = context.HttpContext.Request.Path.ToString();
        if (url.EndsWith("/health") || url.Contains("/admin/"))
            return;
        
        GetService(out DynamicConfig config);
        if (config == null)
        {
            Log.Warn(Owner.Will, "DynamicConfig is null; unable to check for maintenance mode");
            return;
        }

        string maintenancePartialUrl = config.Optional<string>(KEY_MAINTENANCE);
        long? start = config.Optional<long?>(KEY_MAINTENANCE_START);
        long? end = config.Optional<long?>(KEY_MAINTENANCE_END);
        bool maintenanceMode = !string.IsNullOrWhiteSpace(maintenancePartialUrl) && PlatformEnvironment.Url().Contains(maintenancePartialUrl);
        long now = Timestamp.UnixTime;

        if (!maintenanceMode || now < start || now >= end)
            return;

        context.Result = new BadRequestObjectResult(new LoginDiagnosis(new MaintenanceException()));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}