using System.Text.Json.Serialization;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Exceptions;

public class ComponentVersionException : PlatformException
{
	[JsonInclude]
	public string ComponentName { get; set; }
	[JsonInclude]
	public int CurrentVersion { get; set; }
	[JsonInclude]
	public int UpdateVersion { get; set; }
	
	[JsonInclude]
	public string Origin { get; set; }
	
	public ComponentVersionException(string name, int currentVersion, int updateVersion, string origin) : base("Component version mismatch", code: ErrorCode.InvalidRequestData)
	{
		ComponentName = name;
		CurrentVersion = currentVersion;
		UpdateVersion = updateVersion;
		Origin = origin;
	}
}