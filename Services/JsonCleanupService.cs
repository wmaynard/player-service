using System;
using System.Reflection;
using System.Text.Json;
using Rumble.Platform.Common.Services;

namespace PlayerService.Services;

public class JsonCleanupService : PlatformTimerService
{
    public JsonCleanupService() : base(Rumble.Platform.Common.Utilities.IntervalMs.TenMinutes) { }

    protected override void OnElapsed()
    {
        // Excerpt from FlurinBruehwiler: https://github.com/dotnet/runtime/issues/65323
        Assembly assembly = typeof(JsonSerializerOptions).Assembly;
        Type updateHandlerType = assembly.GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler");
        MethodInfo clearCacheMethod = updateHandlerType?.GetMethod("ClearCache", BindingFlags.Static | BindingFlags.Public);
        clearCacheMethod?.Invoke(null, new object[] { null }); 
    }
}