using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Utilities;
public class AppleToken
{
	// private const string PUBLIC_KEY_URL = "https://appleid.apple.com/auth/keys";
	// private static readonly PlatformRequest GetPublicKey = PlatformRequest.Get(PUBLIC_KEY_URL);
	// public RumbleJson Keys { get; init; }
	// // private static readonly RumbleJson PublicKey = GetPublicKey.Send();
	// public string Token { get; set; }
	//
	// public AppleToken(string token)
	// {
	// 	Token = token;
	//
	// 	PlatformRequest req = PlatformRequest.Get(PUBLIC_KEY_URL);
	// 	RumbleJson response = req.Send(out HttpStatusCode code);
	// 	RumbleJson[] data = response.Require<RumbleJson[]>("keys");
	// 	
	// 	if (code != HttpStatusCode.OK)
	// 		Log.Error(Owner.Will, "Could not retrieve auth keys from Apple.", data: new
	// 		{
	// 			URL = PUBLIC_KEY_URL,
	// 			Code = code
	// 		});
	// }
	//
	// public void Decode()
	// {
	// 	List<JsonWebKey> ks = new List<JsonWebKey>();
	// 	foreach (RumbleJson key in Keys.AllValues)
	// 	{
	// 		ks.Add(new JsonWebKey(key.JSON));
	// 	}
	//
	// 	ks = null;
	//
	// }
	//
}