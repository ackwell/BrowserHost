using System;

namespace BrowserHost.Common
{
	public class RenderProcessArguments
	{
		public int ParentPid;
		public string SqpackDataDir;
		public string CefAssemblyDir;
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
	public class ResizeInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public int Width;
		public int Height;
	}

	[Serializable]
	public class TextureHandleResponse
	{
		public IntPtr TextureHandle;
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
	public class EventInlayRequest : DownstreamIpcRequest
	{
		public Guid Guid;
		public string Name;
		// TODO: I'm _really_ unhappy about using json for this. Look (further) into arbitrary binary serialization
		//       like AnySerialize and such.
		private string data;
		public object Data
		{
			get { return TinyJson.JSONParser.FromJson<object>(data); }
			set { data = TinyJson.JSONWriter.ToJson(value); }
		}
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
	}

	[Serializable]
	public class SetCursorRequest : UpstreamIpcRequest
	{
		public Guid Guid;
		public Cursor Cursor;
	}

	#endregion
}
