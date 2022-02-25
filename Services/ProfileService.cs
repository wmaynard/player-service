using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Apis.Auth;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PlayerService.Exceptions;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ProfileService : PlatformMongoService<Profile>
	{
		private const string GAME_CENTER = "gameCenter";
		private const string GOOGLE_PLAY = "googlePlay";
		private const string FACEBOOK = "facebook";
			
		public ProfileService() : base("profiles") { }

		public Profile[] Find(string installId, GenericData ssoData, out SsoData[] ssos)
		{
			ssoData ??= new GenericData();

			List<SsoData> ssoList = new List<SsoData>();
			List<Profile> output = new List<Profile>();
			output.AddRange(base.Find(profile => profile.Type == Profile.TYPE_INSTALL && profile.AccountId == installId));
			foreach (string provider in ssoData.Keys)
			{
				GenericData data = ssoData.Require<GenericData>(provider);

				output.AddRange(provider switch
				{
					GAME_CENTER => FromGameCenter(data),
					GOOGLE_PLAY => FromGooglePlay(data, ref ssoList),
					FACEBOOK => FromFacebook(data),
					_ => throw new ArgumentOutOfRangeException($"Unexpected SSO provider '{provider}'.")
				});
			}

			ssos = ssoList.ToArray();
			return output.ToArray();
		}
		
		// public Profile[] FromSSO(GenericData ssoData)
		// {
		// 	List<Profile> output = new List<Profile>();
		// 	foreach (string provider in ssoData.Keys)
		// 	{
		// 		GenericData data = ssoData.Require<GenericData>(provider);
		// 		output.AddRange(provider switch
		// 		{
		// 			"gameCenter" => FromGameCenter(data),
		// 			"googlePlay" => FromGooglePlay(data),
		// 			"facebook" => FromFacebook(data),
		// 			_ => throw new ArgumentOutOfRangeException($"Unexpected SSO provider '{provider}'.")
		// 		});
		// 	}
		// 	return output.ToArray();
		// }

		private Profile[] FromGooglePlay(GenericData sso, ref List<SsoData> list)
		{
			string token = sso.Require<string>("idToken");
			if (string.IsNullOrWhiteSpace(token))
				return Array.Empty<Profile>();
			try
			{
				Task<GoogleJsonWebSignature.Payload> task = GoogleJsonWebSignature.ValidateAsync(token);
				task.Wait();
				SsoData payload = task.Result;
				payload.Source = GOOGLE_PLAY;
				list.Add(payload);
				return Find(profile => profile.ProfileId == payload.AccountId).ToArray();
			}
			catch (Exception e)
			{
				throw new SsoInvalidException(token, "Google", inner: e);
			}
		}

		// TODO GameCenter is old and deprecated and should be removed.  This is only here for testing purposes
		// and should not be deployed.
		private Profile[] FromGameCenter(GenericData sso)
		{
			string profileId = sso.Optional<string>("playerId");

			return profileId != null
				? Find(profile => profile.ProfileId == profileId).ToArray()
				: null;
		}

		private static Profile[] FromFacebook(GenericData sso)
		{
			return null;
		}
	}
}