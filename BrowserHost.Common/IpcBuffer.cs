using SharedMemory;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace BrowserHost.Common
{
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

		public TResponse RemoteRequest<TResponse>(TOutgoing request, int timeout = Timeout.Infinite)
		{
			var rawRequest = Encode(request);

			var rawResponse = RemoteRequest(rawRequest, timeout);
			// TODO: Error check

			if (rawResponse.Data == null) { return default; }

			return Decode<TResponse>(rawResponse.Data);
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
			T value;
			using (MemoryStream stream = new MemoryStream(encoded))
			{
				value = (T)formatter.Deserialize(stream);
			}
			return value;
		}
	}
}
