using System.Collections.Generic;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions;
public class DiscriminatorUnavailableException : PlatformException
{
	[JsonInclude]
	public string AccountId { get; private set; }
	[JsonInclude]
	public IEnumerable<int> Attempts { get; private set; }
	public DiscriminatorUnavailableException(string accountId, IEnumerable<int> attempts) : base("Could not generate a discriminator")
	{
		AccountId = accountId;
		Attempts = attempts;
	}
}