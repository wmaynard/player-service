using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;
public class InvalidUserException : PlatformException
{
	[JsonInclude]
	public string AccountId { get; init; }
	[JsonInclude]
	public string ScreenName { get; init; }
	[JsonInclude]
	public int Discriminator { get; init; }
	
	public InvalidUserException(string accountId, string screenname, int discriminator) : base("Invalid player information.")
	{
		AccountId = accountId;
		ScreenName = screenname;
		Discriminator = discriminator;
	}
}