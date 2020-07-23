using System;

namespace BrowserHost.Common
{
	public class RenderProcessArguments
	{
		public int ParentPid;
		public string CefAssemblyDir;
		public string DalamudAssemblyDir;
		public string KeepAliveHandleName;
		public string IpcChannelName;

		public string Serialise()
		{
			return TinyJson.JSONWriter.ToJson(this);
		}

		public static RenderProcessArguments Deserialise(string serialisedArgs)
		{
			return TinyJson.JSONParser.FromJson<RenderProcessArguments>(serialisedArgs);
		}
	}

	[Serializable]
	public class IpcRequest { }

	[Serializable]
	public class NewInlayRequest : IpcRequest {
		public string Url;
		public int Width;
		public int Height;
	}

	[Serializable]
	public class NewInlayResponse {
		public IntPtr TextureHandle;
	}

	[Serializable]
	public class MouseMoveRequest : IpcRequest
	{
		public float X;
		public float Y;
	}

	// TODO: Probably needs a response with what cursor should be used
	[Serializable]
	public class MouseMoveResponse { }
}
