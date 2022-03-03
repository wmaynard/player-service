using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;
public class TokenGenerationException : PlatformException
{
	public string Reason { get; set; }
	public TokenGenerationException(string reason) : base($"Token generation failed: {reason}")
	{
		Reason = reason;
	}
}