using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using PlayerService.Exceptions.Login;
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
		private const string AUTH_KEYS_CACHE_KEY = "AppleAuth";
#pragma warning disable
		private readonly ApiService    _apiService;
		private readonly CacheService  _cache;
		private readonly DynamicConfig _dynamicConfig;
#pragma warning restore
		
		internal static AppleSignatureVerifyService Instance { get; private set; }

		public AppleSignatureVerifyService() => Instance = this;

		private void RefreshAppleAuthKeys(string keyId, out AppleAuthKey authKey, out AppleResponse appleKeys)
		{
			_apiService
				.Request(_dynamicConfig.Require<string>("appleAuthKeysUrl"))
				.OnSuccess(_ => Log.Local(Owner.Will, "New Apple auth keys fetched."))
				.OnFailure(_ => Log.Error(Owner.Will, "Unable to fetch new Apple auth keys."))
				.Get(out AppleResponse response, out _);
				
			appleKeys = response;
				
			_cache.Store(AUTH_KEYS_CACHE_KEY, appleKeys, expirationMS: IntervalMs.TenMinutes);
			
			authKey = appleKeys?.Keys?.Any() ?? false
				? appleKeys.Keys.Find(key => key.Kid == keyId)
				: null;
		}
		
		public AppleAccount Verify(string appleToken, string appleNonce)
		{
			AppleAuthKey authKey = null;
			JwtSecurityTokenHandler handler = new();
			JwtSecurityToken token = handler.ReadJwtToken(appleToken);
			string keyId = token.Header.Kid;
			
			// Cache the Apple public key; if it expired, refresh it.
			if (!_cache.HasValue(AUTH_KEYS_CACHE_KEY, out AppleResponse cacheValue))
				RefreshAppleAuthKeys(keyId, out authKey, out cacheValue);
			else
			{
				authKey = cacheValue.Keys.Find(key => key.Kid == keyId);
				
				// In rare cases it might be possible that the cached public keys don't contain the keyId.  If this happens,
				// it means Apple's side changed, and needs to be refreshed again.
				if (authKey == null)
				{
					Log.Warn(Owner.Will, "No valid Apple auth key was found for Apple SSO attempt. Attempting to fetch new auth keys.", data: new
					{
						AppleToken = appleToken,
						AppleKeys = cacheValue
					});
					RefreshAppleAuthKeys(keyId, out authKey, out cacheValue);

					// Apple SSO is unavailable or otherwise impossible; surface an error.
					if (authKey == null)
						throw new PlatformException("Apple SSO attempt failed due to no matching Apple auth key being found.");
				}
			}
			
			using RSACryptoServiceProvider rsa = new();
			rsa.ImportParameters(new RSAParameters
			{
				Exponent = FromBase64Url(authKey.E),
				Modulus = FromBase64Url(authKey.N)
			});

			TokenValidationParameters validationParameters = new()
			{
				RequireExpirationTime = true,
				RequireSignedTokens = true,
				ValidateAudience = true,
				ValidateIssuer = true,
				ValidateLifetime = true,
				ValidIssuer = "https://appleid.apple.com",
				ValidAudiences = new []
				{
					"com.rumbleentertainment.towersandtitans",
					"com.towersandtitans.eng.dev"
				},
				TryAllIssuerSigningKeys = true,
				IssuerSigningKey = new RsaSecurityKey(rsa),
				IssuerSigningKeys = new [] { new RsaSecurityKey(rsa) }
			};

			SecurityToken validatedSecurityToken = null;
			try
			{
				handler.ValidateToken(appleToken, validationParameters, out validatedSecurityToken);
			}
			catch (Exception e)
			{
				throw new AppleValidationException(appleToken, inner: e);
			}
			JwtSecurityToken validatedJwt = validatedSecurityToken as JwtSecurityToken;

			if (validatedJwt?.Claims.First(claim => claim.Type == "nonce").Value == appleNonce)
				try
				{
					return new AppleAccount(validatedJwt);
				}
				catch (Exception e)
				{
					throw new PlatformException("Error occurred parsing token data into AppleAccount.", inner: e);
				}

			throw new AppleValidationException(appleToken, inner: new PlatformException("Apple nonce did not match token."));
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