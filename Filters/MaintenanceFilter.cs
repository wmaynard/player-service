using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MongoDB.Driver;
using PlayerService.Models;
using PlayerService.Models.Login;
using PlayerService.Services;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

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
        
        // TD-16915: Add whitelist support for maintenance bypass
        // If dynamic config has values specified for whitelisted domains or exact email addresses, those players should be able to enter
        // the game even if maintenance mode is switched on.
        BadRequestObjectResult denial = new BadRequestObjectResult(new LoginDiagnosis(new MaintenanceException()))
        {
            StatusCode = StatusCodes.Status423Locked
        };

        string[] whitelist = (config.Optional<string>("maintenanceWhitelist") ?? "")
            .Split(',')
            .Select(address => address.Trim())
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .ToArray();

        if (!whitelist.Any())
        {
            context.Result = denial;
            return;
        }

        if (context.TryGetToken(out TokenInfo token) && whitelist.Any(domain => token.Email.EndsWith(domain)))
            return;

        if (url.EndsWith("/login") && context.TryGetBody(out RumbleJson body))
        {
            GetService(out PlayerAccountService playerService);

            Player player = playerService.FromDevice(body.Require<DeviceInfo>(Player.FRIENDLY_KEY_DEVICE));

            if (player?.Email != null && whitelist.Any(domain => player.Email.EndsWith(domain)))
                return;
        }

        context.Result = denial;
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}