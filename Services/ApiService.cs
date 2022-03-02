using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PlayerService.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace PlayerService.Services
{
	public class ApiService : PlatformService
	{
		private HttpClient HttpClient { get; init; } // Used for making HTTP requests
		private WebClient WebClient { get; init; } // Used for downloading files
		private GenericData DefaultHeaders { get; init; }

		// TODO: Add origin (calling class), and do not honor requests coming from self
		public ApiService()
		{
			HttpClient = new HttpClient(new HttpClientHandler()
			{
				AutomaticDecompression = DecompressionMethods.All
			});
			WebClient = new WebClient();

			AssemblyName exe = Assembly.GetExecutingAssembly().GetName();
			DefaultHeaders = new GenericData()
			{
				{ "User-Agent", $"{exe.Name}/{exe.Version}" },
				{ "Accept", "*/*" },
				{ "Accept-Encoding", "gzip, deflate, br" }
			};
		}

		public ApiRequest Request(string url, int retries = ApiRequest.DEFAULT_RETRIES) => new ApiRequest(this, url, retries);

		internal GenericData Send(HttpRequestMessage message) => null;

		internal ApiResponse Send(ApiRequest request)
		{
			Task<ApiResponse> task = SendAsync(request);
			task.Wait();
			return task.Result;
		}
		internal async Task<ApiResponse> SendAsync(ApiRequest request)
		{
			HttpResponseMessage response = null;
			try
			{
				do
				{
					Log.Local(Owner.Will, $"Sleeping for {request.ExponentialBackoffMS}ms");
					Thread.Sleep(request.ExponentialBackoffMS);
					response = await HttpClient.SendAsync(request);
				} while (!((int)response.StatusCode).ToString().StartsWith("2") && --request.Retries > 0);
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, $"Could not send request to {request.URL}.", data: new
				{
					Request = request,
					Response = response
				}, exception: e);
			}

			ApiResponse output = new ApiResponse(response);
			request.Complete(output);
			return new ApiResponse(response);
		}

		public class ApiResponse
		{
			public bool Success => StatusCode.ToString().StartsWith("2");
			public readonly int StatusCode;
			internal HttpResponseMessage Response;
			internal GenericData OriginalResponse => Await(Response.Content.ReadAsStringAsync());

			public ApiResponse(HttpResponseMessage message)
			{
				Response = message;
				StatusCode = (int)Response.StatusCode;
			}

			// internal ApiResponse Validate()
			// {
			// 	if (Success)
			// 		return this;
			// 	OriginalResponse = AsGenericData;
			// 	Response = null;
			// 	return this;
			// }

			private static T Await<T>(Task<T> asyncCall)
			{
				if (asyncCall == null)
					return default;
				asyncCall.Wait();
				return asyncCall.Result;
			}
			public string AsString => Await(AsStringAsync());
			public async Task<string> AsStringAsync()
			{
				try
				{
					if (!Success)
						return null;
					return await Response.Content.ReadAsStringAsync();
				}
				catch (Exception e)
				{
					Log.Error(Owner.Default, "Could not cast response to string.", data: new
					{
						Response = Response
					}, exception: e);
					return null;
				}
			}
			public GenericData AsGenericData => Await(AsGenericDataAsync());
			public async Task<GenericData> AsGenericDataAsync()
			{
				try
				{
					if (!Success)
						return null;
					return await AsStringAsync();
				}
				catch (Exception e)
				{
					Log.Error(Owner.Default, "Could not cast response to GenericData.", data: new
					{
						Response = Response
					}, exception: e);
					return null;
				}
			}
			public byte[] AsByteArray => Await(AsByteArrayAsync());
			public async Task<byte[]> AsByteArrayAsync()
			{
				try
				{
					if (!Success)
						return null;
					Stream stream = await Response.Content.ReadAsStreamAsync();
					await using MemoryStream ms = new MemoryStream();
					await stream.CopyToAsync(ms);
					return ms.ToArray();
				}
				catch (Exception e)
				{
					Log.Error(Owner.Default, "Could not cast response to byte[].", data: new
					{
						Response = Response
					}, exception: e);
					return null;
				}
			}

			public static implicit operator string(ApiResponse args) => args.AsString;
			public static implicit operator GenericData(ApiResponse args) => args.AsGenericData;
			public static implicit operator byte[](ApiResponse args) => args.AsByteArray;
			public static implicit operator int(ApiResponse args) => args.StatusCode;
		}

		public class ApiRequest
		{
			public const int DEFAULT_RETRIES = 6;
			internal string URL { get; private set; }
			internal GenericData Headers { get; private set; }
			internal GenericData Payload { get; private set; }
			internal GenericData Response { get; private set; }
			internal HttpMethod Method { get; private set; }
			internal GenericData Parameters { get; private set; }
			private readonly ApiService _apiService;
			public int Retries { get; internal set; }
			private int _originalRetries;
			internal int ExponentialBackoffMS => (int)Math.Pow(2, _originalRetries - Retries);
			// internal event EventHandler<ApiResponse> OnComplete;
			private event EventHandler<ApiResponse> _onSuccess;
			private event EventHandler<ApiResponse> _onFailure;
			
			public ApiRequest(ApiService spawner, string url, int retries = DEFAULT_RETRIES)
			{
				_apiService = spawner;
				Headers = spawner.DefaultHeaders;
				Payload = new GenericData();
				Parameters = new GenericData();
				SetRetries(retries);
				_onSuccess += (sender, args) => { };
				_onFailure += (sender, args) => { };
				URL = url;
			}

			public ApiRequest SetUrl(string url)
			{
				URL = url;
				return this;
			}

			public ApiRequest AddAuthorization(string token) => AddHeader("Authorization", $"Bearer {token}");
			public ApiRequest AddHeader(string key, string value) => AddHeaders(new GenericData() { { key, value } });
			public ApiRequest AddHeaders(GenericData headers)
			{
				Headers.Combine(other: headers, prioritizeOther: true);
				return this;
			}
			public ApiRequest AddParameter(string key, string value) => AddParameters(new GenericData() { { key, value } });
			public ApiRequest AddParameters(GenericData parameters)
			{
				Parameters.Combine(other: parameters, prioritizeOther: true);
				return this;
			}
			public ApiRequest SetPayload(GenericData payload)
			{
				Payload.Combine(other: payload, prioritizeOther: true);
				return this;
			}
			public ApiRequest SetRetries(int retries)
			{
				Retries = _originalRetries = retries;
				return this;
			}
			internal void Complete(ApiResponse args)
			{
				if (args.Success)
					_onSuccess?.DynamicInvoke(this, args);
				else
					_onFailure?.DynamicInvoke(this, args);
			}

			public ApiRequest OnSuccess(EventHandler<ApiResponse> action)
			{
				_onSuccess += action;
				return this;
			}
			public ApiRequest OnFailure(EventHandler<ApiResponse> action)
			{
				_onFailure += action;
				return this;
			}

			private ApiRequest SetMethod(HttpMethod method)
			{
				Method = method;
				return this;
			}

			private ApiResponse Send(HttpMethod method, out GenericData result, out int code)
			{
				Task<ApiResponse> task = SendAsync(method);
				task.Wait();
				ApiResponse output = task.Result;
				result = output.AsGenericData;
				code = output.StatusCode;
				return output;
			}
			private async Task<ApiResponse> SendAsync(HttpMethod method) => await SetMethod(method)._apiService.SendAsync(this);
			public ApiResponse Delete() => Delete(out GenericData unused1, out int unused2);
			public ApiResponse Get() => Get(out GenericData unused1, out int unused2);
			public ApiResponse Head() => Head(out GenericData unused1, out int unused2);
			public ApiResponse Options() => Options(out GenericData unused1, out int unused2);
			public ApiResponse Patch() => Patch(out GenericData unused1, out int unused2);
			public ApiResponse Post() => Post(out GenericData unused1, out int unused2);
			public ApiResponse Put() => Put(out GenericData unused1, out int unused2);
			public ApiResponse Trace() => Trace(out GenericData unused1, out int unused2);

			public ApiResponse Delete(out GenericData json) => Delete(out json, out int unused);
			public ApiResponse Get(out GenericData json) => Get(out json, out int unused);
			public ApiResponse Head(out GenericData json) => Head(out json, out int unused);
			public ApiResponse Options(out GenericData json) => Options(out json, out int unused);
			public ApiResponse Patch(out GenericData json) => Patch(out json, out int unused);
			public ApiResponse Post(out GenericData json) => Post(out json, out int unused);
			public ApiResponse Put(out GenericData json) => Put(out json, out int unused);
			public ApiResponse Trace(out GenericData json) => Trace(out json, out int unused);
			
			public ApiResponse Delete(out GenericData json, out int code) => Send(HttpMethod.Delete, out json, out code);
			public ApiResponse Get(out GenericData json, out int code) => Send(HttpMethod.Get, out json, out code);
			public ApiResponse Head(out GenericData json, out int code) => Send(HttpMethod.Head, out json, out code);
			public ApiResponse Options(out GenericData json, out int code) => Send(HttpMethod.Options, out json, out code);
			public ApiResponse Patch(out GenericData json, out int code) => Send(HttpMethod.Patch, out json, out code);
			public ApiResponse Post(out GenericData json, out int code) => Send(HttpMethod.Post, out json, out code);
			public ApiResponse Put(out GenericData json, out int code) => Send(HttpMethod.Put, out json, out code);
			public ApiResponse Trace(out GenericData json, out int code) => Send(HttpMethod.Trace, out json, out code);

			public async Task<ApiResponse> DeleteAsync() => await SendAsync(HttpMethod.Delete);
			public async Task<ApiResponse> GetAsync() => await SendAsync(HttpMethod.Get);
			public async Task<ApiResponse> HeadAsync() => await SendAsync(HttpMethod.Head);
			public async Task<ApiResponse> OptionsAsync() => await SendAsync(HttpMethod.Options);
			public async Task<ApiResponse> PatchAsync() => await SendAsync(HttpMethod.Patch);
			public async Task<ApiResponse> PostAsync() => await SendAsync(HttpMethod.Post);
			public async Task<ApiResponse> PutAsync() => await SendAsync(HttpMethod.Put);
			public async Task<ApiResponse> TraceAsync() => await SendAsync(HttpMethod.Trace);

			public static implicit operator HttpRequestMessage(ApiRequest request)
			{
				try
				{
					HttpRequestMessage output = new HttpRequestMessage();

					output.Method = request.Method;

					string parameters = request.Parameters.Any()
						? "?" + string.Join('&', request.Parameters.SelectMany(pair => $"${pair.Key}={pair.Value}"))
						: "";
					string url = request.URL + parameters;
					output.RequestUri = new Uri(url);

					foreach (KeyValuePair<string, object> pair in request.Headers)
					{
						if (output.Headers.Contains(pair.Key))
							output.Headers.Remove(pair.Key);
						output.Headers.Add(pair.Key, request.Headers.Require<string>(pair.Key));
					}
				
				
					if (NO_BODY.Contains(request.Method))
						return output;
				
					output.Content = new StringContent(request.Payload?.JSON ?? "{}");
					output.Content.Headers.Remove("Content-Type");
					output.Content.Headers.Add("Content-Type", "application/json");

					return output;
				}
				catch (Exception e)
				{
					Log.Error(Owner.Default, "Could not create HttpRequestMessage.", data: new
					{
						APIRequest = request
					}, exception: e);
					throw;
				}
				
			}
			private static readonly HttpMethod[] NO_BODY =
			{
				HttpMethod.Delete, 
				HttpMethod.Get, 
				HttpMethod.Head,
				HttpMethod.Trace
			};
		}
	}
}