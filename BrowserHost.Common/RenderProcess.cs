using System;

namespace BrowserHost.Common
{
	// TODO: I should probably split this file up it's getting a bit silly

	public class RenderProcessArguments
	{
		public int ParentPid;
		public string CefAssemblyDir;
		public string CefCacheDir;
		public string DalamudAssemblyDir;
		public long DxgiAdapterLuid;
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

	// TODO: Perhaps look into seperate buffers for bitmap data and frame info?
	public struct BitmapFrame
	{
		public int Width;
		public int Height;

		// Length of the incoming data in next node
		public int Length;
	}

	public enum FrameTransportMode
	{
		None = 0,
		SharedTexture = 1 << 0,
		BitmapBuffer = 1 << 1,
	}

	#region Downstream IPC

	[Serializable]
	public class DownstreamIpcRequest { }

	[Serializable]
	public class NewInlayRequest : DownstreamIpcRequest {
		public Guid Guid;
		public FrameTransportMode FrameTransportMode;
		public string Url;
		public int Width;
		public int Height;
	}

	[Serializable]
	public class ResizeInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public int Width;
		public int Height;
	}

	[Serializable]
	public class FrameTransportResponse { }

	[Serializable]
	public class TextureHandleResponse : FrameTransportResponse
	{
		public IntPtr TextureHandle;
	}

	[Serializable]
	public class BitmapBufferResponse : FrameTransportResponse
	{
		public string BitmapBufferName;
		public string FrameInfoBufferName;
	}

	[Serializable]
	public class NavigateInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public string Url;
	}

	[Serializable]
	public class DebugInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
	}

	[Serializable]
	public class RemoveInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
	}

	public enum InputModifier
	{
		None = 0,
		Shift = 1 << 0,
		Control = 1 << 1,
		Alt = 1 << 2,
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
		public bool Leaving;
		// The following button fields represent changes since the previous event, not current state
		// TODO: May be approaching being advantageous for button->fields map
		public MouseButton Down;
		public MouseButton Double;
		public MouseButton Up;
		public float WheelX;
		public float WheelY;
		public InputModifier Modifier;
	}

	public enum KeyEventType
	{
		KeyDown,
		KeyUp,
		Character,
	}

	[Serializable]
	public class KeyEventRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public KeyEventType Type;
		public bool SystemKey;
		public int UserKeyCode;
		public int NativeKeyCode;
		public InputModifier Modifier;
	}

	#endregion

	#region Upstream IPC

	[Serializable]
	public class UpstreamIpcRequest { }

	[Serializable]
	public class ReadyNotificationRequest : UpstreamIpcRequest
	{
		public FrameTransportMode availableTransports;
	}

	// Akk, did you really write out every supported value of the cursor property despite both sides of the IPC not supporting the full set?
	// Yes. Yes I did.
	public enum Cursor
	{
		Default,
		None,
		ContextMenu,
		Help,
		Pointer,
		Progress,
		Wait,
		Cell,
		Crosshair,
		Text,
		VerticalText,
		Alias,
		Copy,
		Move,
		NoDrop,
		NotAllowed,
		Grab,
		Grabbing,
		AllScroll,
		ColResize,
		RowResize,
		NResize,
		EResize,
		SResize,
		WResize,
		NEResize,
		NWResize,
		SEResize,
		SWResize,
		EWResize,
		NSResize,
		NESWResize,
		NWSEResize,
		ZoomIn,
		ZoomOut,

		// Special case value - cursor is on a fully-transparent section of the page, and should not capture
		BrowserHostNoCapture,
	}

	[Serializable]
	public class SetCursorRequest : UpstreamIpcRequest
	{
		public Guid Guid;
		public Cursor Cursor;
	}

	#endregion
}
