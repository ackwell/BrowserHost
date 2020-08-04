using BrowserHost.Common;
using ImGuiNET;
using ImGuiScene;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class BrowserWidget : IDisposable
	{
		public Guid Guid { get; } = Guid.NewGuid();

		private Vector2 size;
		private string url;

		private TextureWrap textureWrap;
		private Exception textureRenderException;

		private bool captureKeyboard = false;
		private bool captureMouse = false;
		private bool shouldMouseLeave;
		private InputModifier modifier;
		private ImGuiMouseCursor cursor;

		// TODO: URL but not shit like this
		public BrowserWidget(string url)
		{
			this.url = url;
		}

		public void Dispose()
		{
			textureWrap?.Dispose();
			RenderProcess.Send(new RemoveInlayRequest() { Guid = Guid });
		}

		public void Navigate(string newUrl)
		{
			url = newUrl;
			RenderProcess.Send(new NavigateInlayRequest() { Guid = Guid, Url = url });
		}

		public void Debug()
		{
			RenderProcess.Send(new DebugInlayRequest() { Guid = Guid });
		}

		public void SetCursor(Cursor cursor)
		{
			this.cursor = DecodeCursor(cursor);
		}

		public void Resize(Vector2 newSize)
		{
			if (newSize == size) { return; }

			// If there isn't a size yet, we haven't rendered at all - boot up an inlay in the render process
			// TODO: Edge case - if a user _somehow_ makes the size zero, this will freak out and generate a new render inlay
			// TODO: Maybe consolidate the request types? dunno.
			var request = size == Vector2.Zero
				? new NewInlayRequest()
				{
					Guid = Guid,
					Url = url,
					Width = (int)newSize.X,
					Height = (int)newSize.Y,
				}
				: new ResizeInlayRequest()
				{
					Guid = Guid,
					Width = (int)newSize.X,
					Height = (int)newSize.Y,
				} as DownstreamIpcRequest;

			var response = RenderProcess.Send<TextureHandleResponse>(request);

			var oldTextureWrap = textureWrap;
			try { textureWrap = BuildTextureWrap(response.TextureHandle); }
			catch (Exception e) { textureRenderException = e; }
			if (oldTextureWrap != null) { oldTextureWrap.Dispose(); }

			size = newSize;
		}

		private TextureWrap BuildTextureWrap(IntPtr textureHandle)
		{
			var texture = DxHandler.Device.OpenSharedResource<D3D11.Texture2D>(textureHandle);
			var view = new D3D11.ShaderResourceView(DxHandler.Device, texture, new D3D11.ShaderResourceViewDescription()
			{
				Format = texture.Description.Format,
				Dimension = D3D.ShaderResourceViewDimension.Texture2D,
				Texture2D = { MipLevels = texture.Description.MipLevels },
			});

			return new D3DTextureWrap(view, texture.Description.Width, texture.Description.Height);
		}

		public (bool, long) WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
		{
			// On click, set the capture state of the KB to that of the mouse - effectively providing
			// "focus" handling for the widget. We're avoiding ImGui for this, as we want to check for
			// clicks entirely outside ImGui's pervue for defocusing.
			if (msg == WindowsMessage.WM_LBUTTONDOWN) { captureKeyboard = captureMouse; }

			// Bail if we're not focused
			// TODO: Revisit this for UI stuff, might not hold
			if (!captureKeyboard) { return (false, 0); }

			KeyEventType? eventType = msg switch
			{
				WindowsMessage.WM_KEYDOWN => KeyEventType.KeyDown,
				WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
				WindowsMessage.WM_KEYUP => KeyEventType.KeyUp,
				WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
				WindowsMessage.WM_CHAR => KeyEventType.Character,
				WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
				_ => null,
			};

			// If the event isn't something we're tracking, bail early with no capture
			if (eventType == null) { return (false, 0); }

			var isSystemKey = false
				|| msg == WindowsMessage.WM_SYSKEYDOWN
				|| msg == WindowsMessage.WM_SYSKEYUP
				|| msg == WindowsMessage.WM_SYSCHAR;

			// TODO: Technically this is only firing once, because we're checking focused before this point,
			// but having this logic essentially duped per-inlay is a bit eh. Dedupe at higher point?
			var modifierAdjust = InputModifier.None;
			if (wParam == (int)VirtualKey.Shift) { modifierAdjust |= InputModifier.Shift; }
			if (wParam == (int)VirtualKey.Control) { modifierAdjust |= InputModifier.Control; }
			// SYS* messages signal alt is held (really?)
			if (isSystemKey) { modifierAdjust |= InputModifier.Alt; }

			if (eventType == KeyEventType.KeyDown) { modifier |= modifierAdjust; }
			else if (eventType == KeyEventType.KeyUp) { modifier &= ~modifierAdjust; }

			RenderProcess.Send(new KeyEventRequest()
			{
				Guid = Guid,
				Type = eventType.Value,
				SystemKey = isSystemKey,
				UserKeyCode = (int)wParam,
				NativeKeyCode = (int)lParam,
				Modifier = modifier,
			});

			// We've handled the input, signal. For these message types, `0` signals a capture.
			return (true, 0);
		}

		public void Render(bool captureMouse = false)
		{
			if (this.captureMouse && !captureMouse) { shouldMouseLeave = true; }
			this.captureMouse = captureMouse;

			if (textureWrap != null)
			{
				HandleMouseEvent();
				ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
			}
			else if (textureRenderException != null)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				ImGui.Text("An error occured while building the browser inlay texture:");
				ImGui.Text(textureRenderException.ToString());
				ImGui.PopStyleColor();
			}
		}

		private void HandleMouseEvent()
		{
			// Render proc won't be ready on first boot
			if (!RenderProcess.Running) { return; }

			var io = ImGui.GetIO();
			var mousePos = io.MousePos - ImGui.GetCursorScreenPos();

			// If earmarked to mouseleave, do that before we noop
			if (shouldMouseLeave)
			{
				shouldMouseLeave = false;
				RenderProcess.Send(new MouseEventRequest()
				{
					Guid = Guid,
					X = mousePos.X,
					Y = mousePos.Y,
					Leaving = true,
				});
			}

			// Skip the rest of mouse handling if we're not capturing
			if (!captureMouse) { return; }

			ImGui.SetMouseCursor(cursor);

			var down = EncodeMouseButtons(io.MouseClicked);
			var double_ = EncodeMouseButtons(io.MouseDoubleClicked);
			var up = EncodeMouseButtons(io.MouseReleased);
			var wheelX = io.MouseWheelH;
			var wheelY = io.MouseWheel;

			// If the event boils down to no change, bail before sending
			if (io.MouseDelta == Vector2.Zero && down == MouseButton.None && double_ == MouseButton.None && up == MouseButton.None && wheelX == 0 && wheelY == 0)
			{
				return;
			}

			var modifier = InputModifier.None;
			if (io.KeyShift) { modifier |= InputModifier.Shift; }
			if (io.KeyCtrl) { modifier |= InputModifier.Control; }
			if (io.KeyAlt) { modifier |= InputModifier.Alt; }

			// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
			RenderProcess.Send(new MouseEventRequest()
			{
				Guid = Guid,
				X = mousePos.X,
				Y = mousePos.Y,
				Down = down,
				Double = double_,
				Up = up,
				WheelX = wheelX,
				WheelY = wheelY,
				Modifier = modifier,
			});
		}

		#region serde

		private MouseButton EncodeMouseButtons(RangeAccessor<bool> buttons)
		{
			var result = MouseButton.None;
			if (buttons[0]) { result |= MouseButton.Primary; }
			if (buttons[1]) { result |= MouseButton.Secondary; }
			if (buttons[2]) { result |= MouseButton.Tertiary; }
			if (buttons[3]) { result |= MouseButton.Fourth; }
			if (buttons[4]) { result |= MouseButton.Fifth; }
			return result;
		}

		private ImGuiMouseCursor DecodeCursor(Cursor cursor)
		{
			// ngl kinda disappointed at the lack of options here
			switch (cursor)
			{
				case Cursor.Default: return ImGuiMouseCursor.Arrow;
				case Cursor.None: return ImGuiMouseCursor.None;
				case Cursor.Pointer: return ImGuiMouseCursor.Hand;

				case Cursor.Text:
				case Cursor.VerticalText:
					return ImGuiMouseCursor.TextInput;

				case Cursor.NResize:
				case Cursor.SResize:
				case Cursor.NSResize:
					return ImGuiMouseCursor.ResizeNS;

				case Cursor.EResize:
				case Cursor.WResize:
				case Cursor.EWResize:
					return ImGuiMouseCursor.ResizeEW;

				case Cursor.NEResize:
				case Cursor.SWResize:
				case Cursor.NESWResize:
					return ImGuiMouseCursor.ResizeNESW;

				case Cursor.NWResize:
				case Cursor.SEResize:
				case Cursor.NWSEResize:
					return ImGuiMouseCursor.ResizeNWSE;
			}

			return ImGuiMouseCursor.Arrow;
		}

		#endregion
	}
}
