using System;
using System.Linq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services;
public class NameGeneratorService : PlatformService
{
	// Will on 2022.04.19
	// While initial feedback on the {adjective} {noun} generation was positive, there were some problems with the implementation:
	//    * The word lists were never curated beyond initial screening and consequently generated innuendo.
	//      This resulted in Mark getting the name "Dry Desire".
	//    * Possible confusion that a new user logged into someone else's account
	//
	// As a result, the decision is to return to a generic name format that players must change on their own.
	private const string NAME_PREFIX = "Player";
	private const int MAX_LENGTH = 13;
	
	private Random rando { get; init; }
	public NameGeneratorService() => rando = new Random();
	public string Next => $"{NAME_PREFIX}{DecimalDigit}{HexDigits}";

	private int DecimalDigit => rando.Next(minValue: 0, maxValue: 9);
	private string HexDigits => GenerateHexNumber(MAX_LENGTH - (NAME_PREFIX.Length + 1)); // 1 for the offset random decimal digit
	
	private string GenerateHexNumber(int length)
	{
		byte[] buffer = new byte[length / 2];
		rando.NextBytes(buffer);
		string output = string.Concat(buffer.Select(x => x.ToString(format: "X2")).ToArray()).ToLower();
		
		return length % 2 == 0
			? output
			: output + rando.Next(maxValue: 16).ToString(format: "X").ToLower();
	}
}