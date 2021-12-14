using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ProfileService : PlatformMongoService<Profile>
	{
		public ProfileService() : base("profiles_temp") { }

		public Profile[] Find(string installId, GenericData ssoData = null)
		{
			ssoData ??= new GenericData();

			List<Profile> output = new List<Profile>();
			output.AddRange(base.Find(profile => profile.Type == "installId" && profile.ProfileId == installId));
			foreach (string provider in ssoData.Keys)
			{
				GenericData data = ssoData.Require<GenericData>(provider);
				output.Add(provider switch
				{
					"gameCenter" => FromGameCenter(data),
					"googlePlay" => FromGooglePlay(data),
					"facebook" => FromFacebook(data),
					_ => throw new ArgumentOutOfRangeException($"Unexpected SSO provider '{provider}'.")
				});
			}

			return output.ToArray();
		}
		
		public Profile[] FromSSO(GenericData ssoData)
		{
			List<Profile> output = new List<Profile>();
			foreach (string provider in ssoData.Keys)
			{
				GenericData data = ssoData.Require<GenericData>(provider);
				output.Add(provider switch
				{
					"gameCenter" => FromGameCenter(data),
					"googlePlay" => FromGooglePlay(data),
					"facebook" => FromFacebook(data),
					_ => throw new ArgumentOutOfRangeException($"Unexpected SSO provider '{provider}'.")
				});
			}
			return output.ToArray();
		}

		private static Profile FromGooglePlay(GenericData sso)
		{
			string googleClientId = PlatformEnvironment.Variable("GOOGLE_CLIENT_ID");

			string token = sso.Require<string>("idToken");
			
			
			
			return null;
		}

		// TODO GameCenter is old and deprecated and should be removed.  This is only here for testing purposes
		// and should not be deployed.
		private Profile FromGameCenter(GenericData sso)
		{
			string profileId = sso.Optional<string>("playerId");

			return profileId != null
				? FindOne(profile => profile.ProfileId == profileId)
				: null;
		}

		private static Profile FromFacebook(GenericData sso)
		{
			return null;
		}
	}
}