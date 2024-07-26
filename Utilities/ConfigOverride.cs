using System;
using System.Collections.Generic;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace PlayerService.Utilities;

public class ConfigOverride : PlatformDataModel
{
    public string Key { get; set; }
    public Version Version { get; set; }
    public object Value { get; set; }
}