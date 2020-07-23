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

	#region Downstream IPC

	[Serializable]
	public class DownstreamIpcRequest { }

	[Serializable]
	public class NewInlayRequest : DownstreamIpcRequest {
		public Guid Guid;
		public string Url;
		public int Width;
		public int Height;
	}

	[Serializable]
	public class NewInlayResponse {
		public IntPtr TextureHandle;
	}

	[Serializable]
	public class MouseMoveRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public float X;
		public float Y;
	}

	// TODO: Probably needs a response with what cursor should be used
	[Serializable]
	public class MouseMoveResponse { }

	#endregion

	#region Upstream IPC

	[Serializable]
	public class UpstreamIpcRequest { }

	// TODO: Make this more comprehensive
	public enum Cursor
	{
		Default,
		Pointer,
	}

	[Serializable]
	public class SetCursorRequest : UpstreamIpcRequest
	{
		public Guid Guid;
		public Cursor Cursor;
	}

	[Serializable]
	public class SetCursorResponse { }

	#endregion
}
