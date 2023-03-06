using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
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
		
		RSAParameters _rsaKeyInfo;

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
					.OnSuccess(action: (sender, apiResponse) =>
					                   {
						                   Log.Local(Owner.Nathan, "New Apple auth keys fetched.");
					                   })
					.OnFailure(action: (sender, apiResponse) =>
					                   {
						                   Log.Error(Owner.Nathan, "Unable to fetch new Apple auth keys.");
					                   })
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
					.Request("url")
					.OnSuccess(action: (sender, apiResponse) =>
					                   {
						                   Log.Local(Owner.Nathan, "New Apple auth keys fetched.");
					                   })
					.OnFailure(action: (sender, apiResponse) =>
					                   {
						                   Log.Error(Owner.Nathan, "Unable to fetch new Apple auth keys.");
					                   })
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
				                                                 RequireExpirationTime = false, // TODO change to true before using
				                                                 RequireSignedTokens = true,
				                                                 ValidateAudience = true,
				                                                 ValidateIssuer = true,
				                                                 ValidateLifetime = false,
				                                                 IssuerSigningKey = new RsaSecurityKey(rsa)
			                                                 };

			SecurityToken validatedSecurityToken = null;
			handler.ValidateToken(appleToken, validationParameters, out validatedSecurityToken);
			JwtSecurityToken validatedJwt = validatedSecurityToken as JwtSecurityToken;

			AppleAccount appleAccount = null;
			try
			{
				appleAccount = new AppleAccount(
												iss: validatedJwt?.Claims.First(claim => claim.Type == "iss").Value,
												aud: validatedJwt?.Claims.First(claim => claim.Type == "aud").Value,
												exp: Int64.Parse(validatedJwt?.Claims.First(claim => claim.Type == "exp").Value),
												iat: Int64.Parse(validatedJwt?.Claims.First(claim => claim.Type == "iat").Value),
												id: validatedJwt?.Claims.First(claim => claim.Type == "sub").Value,
												nonce: validatedJwt?.Claims.First(claim => claim.Type == "nonce").Value,
												cHash: validatedJwt?.Claims.First(claim => claim.Type == "c_hash").Value,
												email: validatedJwt?.Claims.First(claim => claim.Type == "email").Value,
												emailVerified: validatedJwt?.Claims.First(claim => claim.Type == "email_verified").Value,
												isPrivateEmail: validatedJwt?.Claims.First(claim => claim.Type == "is_private_email").Value,
												authTime: Int64.Parse(validatedJwt?.Claims.First(claim => claim.Type == "auth_time").Value),
												nonceSupported: Boolean.Parse( validatedJwt?.Claims.First(claim => claim.Type == "nonce_supported").Value)
	                                            );
			}
			catch (Exception e)
			{
				Log.Error(owner: Owner.Nathan, message: "Error occurred parsing token data into AppleAccount.", data: $"Token data: {validatedJwt}.");
				throw new PlatformException(message: "Error occurred parsing token data into AppleAccount.", inner: e);
			}
			
			return appleAccount;
		}
		
		static byte[] FromBase64Url(string base64Url)
		{
			string padded = base64Url.Length % 4 == 0
				                ? base64Url : base64Url + "====".Substring(base64Url.Length % 4);
			string base64 = padded.Replace("_", "/")
			                      .Replace("-", "+");
			return Convert.FromBase64String(base64);
		}
	}
}