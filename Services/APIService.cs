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
	public class APIService : PlatformService
	{
		private HttpClient HttpClient { get; init; } // Used for making HTTP requests
		private WebClient WebClient { get; init; } // Used for downloading files
		private GenericData DefaultHeaders { get; init; }

		public APIService()
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

		public APIRequest Request(string url, int retries = APIRequest.DEFAULT_RETRIES) => new APIRequest(this, url, retries);

		internal GenericData Send(HttpRequestMessage message) => null;

		internal APIResponseArgs Send(APIRequest request)
		{
			Task<APIResponseArgs> task = SendAsync(request);
			task.Wait();
			return task.Result;
		}
		internal async Task<APIResponseArgs> SendAsync(APIRequest request)
		{
			HttpRequestMessage message = request;
			HttpResponseMessage response = null;
			try
			{
				do
				{
					Thread.Sleep(request.ExponentialBackoffMS);
					response = await HttpClient.SendAsync(message);	
				} while (!response.StatusCode.ToString().StartsWith("2") && --request.Retries > 0);
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, $"Could not send request to {request.URL}.", data: new
				{
					Request = request,
					Response = response
				}, exception: e);
			}

			APIResponseArgs output = new APIResponseArgs(response);
			request.Complete(output);
			return new APIResponseArgs(response);
		}

		public class APIResponseArgs
		{
			public bool Success => StatusCode.ToString().StartsWith("2");
			public int StatusCode;
			internal HttpResponseMessage Response;

			public APIResponseArgs(HttpResponseMessage message)
			{
				Response = message;
				StatusCode = (int)Response.StatusCode;
			}

			public async Task<string> AsStringAsync()
			{
				try
				{
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

			public async Task<GenericData> AsGenericDataAsync()
			{
				try
				{
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

			public string AsString()
			{
				Task<string> output = AsStringAsync();
				output?.Wait();
				return output?.Result;
			}

			public GenericData AsGenericData()
			{
				Task<GenericData> output = AsGenericDataAsync();
				output?.Wait();
				return output?.Result;
			}
			public byte[] AsByteArray()
			{
				Task<byte[]> output = AsByteArrayAsync();
				output?.Wait();
				return output?.Result;
			}
			public async Task<byte[]> AsByteArrayAsync()
			{
				try
				{
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

			public static implicit operator string(APIResponseArgs args) => args.AsString();
			public static implicit operator GenericData(APIResponseArgs args) => args.AsGenericData();
			public static implicit operator byte[](APIResponseArgs args) => args.AsByteArray();
		}

		public class APIRequest
		{
			public const int DEFAULT_RETRIES = 6;
			internal string URL { get; private set; }
			internal GenericData Headers { get; private set; }
			internal GenericData Payload { get; private set; }
			internal GenericData Response { get; private set; }
			internal HttpMethod Method { get; private set; }
			internal GenericData Parameters { get; private set; }
			private readonly APIService _apiService;
			public int Retries { get; internal set; }
			private int _originalRetries;
			internal int ExponentialBackoffMS => (int)Math.Pow(2, _originalRetries - Retries);
			// internal event EventHandler<APIResponseArgs> OnComplete;
			private event EventHandler<APIResponseArgs> _onSuccess;
			private event EventHandler<APIResponseArgs> _onFailure;
			
			public APIRequest(APIService spawner, string url, int retries = DEFAULT_RETRIES)
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

			public APIRequest SetURL(string url)
			{
				URL = url;
				return this;
			}
			public APIRequest AddHeader(string key, string value) => AddHeaders(new GenericData() { { key, value } });
			public APIRequest AddHeaders(GenericData headers)
			{
				Headers.Combine(other: headers, prioritizeOther: true);
				return this;
			}
			public APIRequest AddParameter(string key, string value) => AddParameters(new GenericData() { { key, value } });
			public APIRequest AddParameters(GenericData parameters)
			{
				Parameters.Combine(other: parameters, prioritizeOther: true);
				return this;
			}
			public APIRequest SetPayload(GenericData payload)
			{
				Payload.Combine(other: payload, prioritizeOther: true);
				return this;
			}
			public APIRequest SetRetries(int retries)
			{
				Retries = _originalRetries = retries;
				return this;
			}
			internal void Complete(APIResponseArgs args)
			{
				if (args.Success)
					_onSuccess?.DynamicInvoke(this, args);
				else
					_onFailure?.DynamicInvoke(this, args);
			}

			public APIRequest OnSuccess(EventHandler<APIResponseArgs> action)
			{
				_onSuccess += action;
				return this;
			}
			public APIRequest OnFailure(EventHandler<APIResponseArgs> action)
			{
				_onFailure += action;
				return this;
			}

			private APIRequest SetMethod(HttpMethod method)
			{
				Method = method;
				return this;
			}

			public APIResponseArgs Delete() => Send(HttpMethod.Delete);
			public APIResponseArgs Get() => Send(HttpMethod.Get);
			public APIResponseArgs Head() => Send(HttpMethod.Head);
			public APIResponseArgs Options() => Send(HttpMethod.Options);
			public APIResponseArgs Patch() => Send(HttpMethod.Patch);
			public APIResponseArgs Post() => Send(HttpMethod.Post);
			public APIResponseArgs Put() => Send(HttpMethod.Put);
			public APIResponseArgs Trace() => Send(HttpMethod.Trace);
			
			public async Task<APIResponseArgs> DeleteAsync() => await SendAsync(HttpMethod.Delete);
			public async Task<APIResponseArgs> GetAsync() => await SendAsync(HttpMethod.Get);
			public async Task<APIResponseArgs> HeadAsync() => await SendAsync(HttpMethod.Head);
			public async Task<APIResponseArgs> OptionsAsync() => await SendAsync(HttpMethod.Options);
			public async Task<APIResponseArgs> PatchAsync() => await SendAsync(HttpMethod.Patch);
			public async Task<APIResponseArgs> PostAsync() => await SendAsync(HttpMethod.Post);
			public async Task<APIResponseArgs> PutAsync() => await SendAsync(HttpMethod.Put);
			public async Task<APIResponseArgs> TraceAsync() => await SendAsync(HttpMethod.Trace);

			private APIResponseArgs Send(HttpMethod method)
			{
				Task<APIResponseArgs> task = SendAsync(method);
				task.Wait();
				return task.Result;
			}
			private async Task<APIResponseArgs> SendAsync(HttpMethod method) => await SetMethod(method)._apiService.SendAsync(this);

			public static implicit operator HttpRequestMessage(APIRequest request)
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
			private static readonly HttpMethod[] NO_BODY = new HttpMethod[]
			{
				HttpMethod.Delete, 
				HttpMethod.Get, 
				HttpMethod.Head,
				HttpMethod.Trace
			};
		}
	}
}