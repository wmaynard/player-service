using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class NameGeneratorService : PlatformService
	{
		private const string ADJECTIVES_PATH = "adjectives.txt";
		private const string NOUNS_PATH = "nouns.txt";
		private const int MIN_LENGTH = 3;
		private const int MAX_LENGTH = 7;
		public List<string> Adjectives { get; private set; }
		public List<string> Nouns { get; private set; }

		public NameGeneratorService()
		{
			ReloadAdjectives();
			ReloadNouns();
		}

		public string Next => Generate();
		private string Generate()
		{
			if (!Adjectives.Any())
				ReloadAdjectives();
			string adjective = Adjectives.First();

			string noun = Nouns.FirstOrDefault(n => n.StartsWith(adjective[0]));
			if (noun == null)
			{
				ReloadNouns();
				noun = Nouns.FirstOrDefault(n => n.StartsWith(adjective[0]));
			}
			
			Adjectives.RemoveAt(0);
			Nouns.Remove(noun);

			try
			{
				return $"{Capitalize(adjective)} {Capitalize(noun)}";
			}
			catch (Exception e)
			{
				throw;
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
}