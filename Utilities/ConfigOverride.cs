using System.Collections.Generic;
using Rumble.Platform.Data;

namespace PlayerService.Utilities;

public class ConfigOverride : PlatformDataModel
{
    public string Key { get; set; }
    public string Version { get; set; }
    public object Value { get; set; }
}