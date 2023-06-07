using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PlayerService.Models;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Filters;
using Rumble.Platform.Data;

namespace PlayerService.Filters;

/// <summary>
/// Adds an additional layer of security for pruning sensitive information from accounts in responses.
/// This filter looks for an outgoing objects with sensitive information on 200 responses.  If it finds one, it prunes
/// them before returning data to the client.
///
/// This is ultimately a redundancy and extra safeguard.  Any controller returning potentially sensitive information
/// should be calling Prune() itself.  However, protecting against accidentally leaked hashes is very important
/// and well worth the extra checks.
/// </summary>
public class PruneFilter : PlatformFilter, IActionFilter 
{
    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is not OkObjectResult ok)
            return;

        if (ok.Value is not RumbleJson json)
            return;

        Player player = json.Optional<Player>("player");
        Player[] players = json.Optional<Player[]>("players") ?? Array.Empty<Player>();
        RumbleAccount rumble = json.Optional<RumbleAccount>("rumble");
        
        if (player != null)
            json["player"] = player.Prune();
        if (players.Any())
            json["players"] = players.Select(p => p.Prune());
        if (rumble != null)
            json["rumble"] = rumble.Prune();
    }
}