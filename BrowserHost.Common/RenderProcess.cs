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

	public enum MouseButton
	{
		None = 0,
		Primary = 1 << 0,
		Secondary = 1 << 1,
		Tertiary = 1 << 2,
		Fourth = 1 << 3,
		Fifth = 1 << 4,
	}

	[Serializable]
	public class MouseEventRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public float X;
		public float Y;
		// Down and up represent changes since the previous event, not current state
		public MouseButton Down;
		public MouseButton Up;
	}

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

	#endregion
}
