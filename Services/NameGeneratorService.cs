using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlayerService.Exceptions;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services;
public class NameGeneratorService : PlatformService
{
	private const string ADJECTIVES_PATH = "adjectives.txt";
	private const string NOUNS_PATH = "nouns.txt";
	private const int MIN_LENGTH = 3;
	private const int MAX_LENGTH = 7;
	private const int MAX_RETRIES = 10;
	public List<string> Adjectives { get; private set; }
	public List<string> Nouns { get; private set; }

	private bool Initialized { get; init; }
	public NameGeneratorService()
	{
		try
		{
			ReloadAdjectives();
			ReloadNouns();
			Initialized = true;
		}
		catch (Exception e)
		{
			Log.Error(Owner.Default, "Unable to initialize NameGeneratorService.", exception: e);
			Initialized = false;
		}
	}

	public string Next => Initialized 
		? Generate() 
		: null;
	
	private string Generate(int retries = MAX_RETRIES)
	{
		string adjective = null;
		string noun = null;
		try
		{
			if (!Adjectives.Any())
				ReloadAdjectives();
			adjective = Adjectives.First();
			Adjectives.RemoveAt(0);

			noun = Nouns.FirstOrDefault(n => n.StartsWith(adjective[0]));
			if (noun == null)
			{
				ReloadNouns();
				noun = Nouns.FirstOrDefault(n => n.StartsWith(adjective[0]));
			}
		
			Nouns.Remove(noun);
			
			// Log.Local(Owner.Will, $"Nouns: {Nouns.Count.ToString(),4} | Adjectives: {Adjectives.Count.ToString(),4}");

			return $"{Capitalize(adjective)} {Capitalize(noun)}";
		}
		catch (Exception e)
		{
			if (retries > 0)
				return Generate(retries - 1);
			Log.Error(Owner.Default, $"Name generation failed. {adjective} {noun}", exception: new NameGenerationException(adjective, noun, e));
			Log.Local(Owner.Will, $"Null nouns: {Nouns.Count(n => n == null)} | Null adjectives: {Adjectives.Count(n => n == null)}");
			ReloadAdjectives();
			ReloadNouns();
			// throw new NameGenerationException(adjective, noun, e);
			// Fallback to old default; bad name generation can't be allowed to break the login
			return string.Concat(Enumerable.Range(0, 8).Select(_ => new Random().Next(16).ToString("X"))).ToLower();
		}
	}

	private static string Capitalize(string input) => string.Concat(input[0].ToString().ToUpper(), input.AsSpan(1));

	private void ReloadAdjectives() => Adjectives = Shuffle(ADJECTIVES_PATH);
	private void ReloadNouns() => Nouns = Shuffle(NOUNS_PATH);

	private static List<string> Shuffle(string path)
	{
		Random rando = new Random();
		List<string> output = File.ReadAllLines(path)
			.Distinct()
			// .Where(str => str != null) // Somehow we're getting null values in our lists
			.Where(str => str.Length >= MIN_LENGTH && str.Length <= MAX_LENGTH)
			.Where(str => !str.StartsWith("//")) // Allow commented lines in case we want to revisit words
			.Select(str => str.ToLower().Trim()) // Account for proper nouns and bad whitespace input
			.OrderBy(str => str)
			.ToList();

		int step = output.Count;
		while (--step > 0)
		{
			int a = rando.Next(step + 1);
			string temp = output[a];
			output[a] = output[step];
			output[step] = temp;
		}

		return output;
	}
}