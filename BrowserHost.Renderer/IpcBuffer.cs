using SharedMemory;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;

namespace BrowserHost.Renderer
{
	public class IpcResponse<TResponse>
	{
		public bool Success;
		public TResponse Data;
	}

	public class IpcBuffer<TIncoming, TOutgoing> : RpcBuffer
	{
		private static BinaryFormatter formatter = new BinaryFormatter();

		// Handle conversion between wire's byte[] and nicer clr types
		private static Func<ulong, byte[], byte[]> CallbackFactory(Func<TIncoming, object> callback)
		{
			return (messageId, rawRequest) =>
			{
				var request = Decode<TIncoming>(rawRequest);

				var response = callback(request);

				return response == null ? null : Encode(response);
			};
		}

		public IpcBuffer(string name, Func<TIncoming, object> callback) : base(name, CallbackFactory(callback)) { }

		public IpcResponse<TResponse> RemoteRequest<TResponse>(TOutgoing request, int timeout = 5000)
		{
			var rawRequest = Encode(request);
			var rawResponse = RemoteRequest(rawRequest, timeout);
			return new IpcResponse<TResponse>
			{
				Success = rawResponse.Success,
				Data = rawResponse.Success ? Decode<TResponse>(rawResponse.Data) : default,
			};
		}

		public async Task<IpcResponse<TResponse>> RemoteRequestAsync<TResponse>(TOutgoing request, int timeout = 5000)
		{
			var rawRequest = Encode(request);
			var rawResponse = await RemoteRequestAsync(rawRequest, timeout);
			return new IpcResponse<TResponse>
			{
				Success = rawResponse.Success,
				Data = rawResponse.Success ? Decode<TResponse>(rawResponse.Data) : default,
			};
		}

		private static byte[] Encode<T>(T value)
		{
			byte[] encoded;
			using (MemoryStream stream = new MemoryStream())
			{
				formatter.Serialize(stream, value);
				encoded = stream.ToArray();
			}
			return encoded;
		}

		private static T Decode<T>(byte[] encoded)
		{
			if (encoded == null) { return default; }

			T value;
			using (MemoryStream stream = new MemoryStream(encoded))
			{
				value = (T)formatter.Deserialize(stream);
			}
			return value;
		}
	}
}
