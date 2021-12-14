using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Exceptions;

namespace PlayerService.Exceptions
{
	public class NameGenerationException : PlatformException
	{
		[JsonInclude]
		public string FirstName { get; init; }
		[JsonInclude]
		public string LastName { get; init; }

		public NameGenerationException(string first, string last, Exception inner) : base("Player name generation failed.", inner)
		{
			FirstName = first;
			LastName = last;
		}
	}
}