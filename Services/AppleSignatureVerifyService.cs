using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using PlayerService.Models.Login;
using PlayerService.Models.Login.AppleAuth;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;

namespace PlayerService.Services
{
	public class AppleSignatureVerifyService : PlatformService
	{
#pragma warning disable
		private readonly ApiService    _apiService;
		private readonly CacheService  _cache;
		private readonly DynamicConfig _dynamicConfig;
#pragma warning restore
		
		internal static AppleSignatureVerifyService Instance { get; private set; }
		
		private RSAParameters _rsaKeyInfo;

		public AppleSignatureVerifyService() //CacheService cache)
		{
			Instance = this;
			//_cache = cache;
		}

		public AppleAccount Verify(string appleToken)
		{
			if (!_cache.HasValue("AppleAuth", out AppleResponse cacheValue))
			{
				string url = _dynamicConfig.Require<string>(key: "appleAuthKeysUrl");
				_apiService
					.Request(url)
					.OnSuccess(_ => Log.Local(Owner.Nathan, "New Apple auth keys fetched."))
					.OnFailure(_ => Log.Error(Owner.Nathan, "Unable to fetch new Apple auth keys."))
					.Get(out AppleResponse response, out int code);
				
				cacheValue = response;
				
				_cache.Store("AppleAuth", cacheValue, expirationMS: 600_000);
			}

			JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
			JwtSecurityToken token = handler.ReadJwtToken(appleToken);
			string kid = token.Header.Kid;

			AppleAuthKey authKey = cacheValue.Keys.Find(key => key.Kid == kid);

			if (authKey == null)
			{
				Log.Warn(owner: Owner.Nathan, message: "No valid Apple auth key was found for Apple SSO attempt. Attempting to fetch new auth keys.", data: $"Token: {appleToken}. Apple Keys: {cacheValue}.");
				_cache.Clear("AppleAuth");
				
				string url = _dynamicConfig.Require<string>(key: "appleAuthKeysUrl");
				_apiService
					.Request(url)
					.OnSuccess(_ => Log.Local(Owner.Nathan, "New Apple auth keys fetched."))
					.OnFailure(_ => Log.Error(Owner.Nathan, "Unable to fetch new Apple auth keys."))
					.Get(out AppleResponse response, out int code);
				
				cacheValue = response;
				
				_cache.Store("AppleAuth", cacheValue, 600_000);
				
				authKey = cacheValue.Keys.Find(key => key.Kid == kid);

				if (authKey == null)
				{
					Log.Error(owner: Owner.Nathan, message: "No valid Apple auth key was found for Apple SSO attempt.", data: $"Token: {appleToken}. Apple Keys: {cacheValue}.");
					throw new PlatformException(message: "Apple SSO attempt failed due to no matching Apple auth key being found.");
				}
			}

			_rsaKeyInfo = new RSAParameters()
			{
				Exponent = FromBase64Url(authKey.E),
				Modulus = FromBase64Url(authKey.N)
			};
			
			using RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
			rsa.ImportParameters(_rsaKeyInfo);

			TokenValidationParameters validationParameters = new TokenValidationParameters
             {
                 RequireExpirationTime = true,
                 RequireSignedTokens = true,
                 ValidateAudience = true,
                 ValidateIssuer = true,
                 ValidateLifetime = true,
                 ValidIssuer = "https://appleid.apple.com",
                 ValidAudience = "com.rumbleentertainment.towersandtitans",
                 TryAllIssuerSigningKeys = true,
                 IssuerSigningKey = new RsaSecurityKey(rsa),
                 IssuerSigningKeys = new List<SecurityKey>() { new RsaSecurityKey(rsa) }
             };

			SecurityToken validatedSecurityToken = null;
			try
			{
				handler.ValidateToken(appleToken, validationParameters, out validatedSecurityToken);
			}
			catch (SecurityTokenSignatureKeyNotFoundException e)
			{
				Log.Error(owner: Owner.Nathan, message: "Apple SSO token validation failed.", data: $"Apple Token: {appleToken}.");

				throw new PlatformException(message: "Apple SSO token validation failed.", inner: e);
			}
			JwtSecurityToken validatedJwt = validatedSecurityToken as JwtSecurityToken;

			try
			{
				return new AppleAccount(validatedJwt);
			}
			catch (Exception e)
			{
				throw new PlatformException(message: "Error occurred parsing token data into AppleAccount.", inner: e);
			}
		}
		
		private static byte[] FromBase64Url(string base64Url)
		{
			string padded = base64Url.Length % 4 == 0
				? base64Url 
				: base64Url + "===="[(base64Url.Length % 4)..];
			string base64 = padded.Replace("_", "/").Replace("-", "+");
			return Convert.FromBase64String(base64);
		}
	}
}