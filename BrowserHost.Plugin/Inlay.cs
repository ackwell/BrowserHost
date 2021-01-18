using BrowserHost.Common;
using BrowserHost.Plugin.TextureHandlers;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Inlay : IDisposable
	{
		private Configuration config;
		private InlayConfiguration inlayConfig;
		public Guid RenderGuid { get; private set; } = Guid.NewGuid();

		private bool resizing = false;
		private Vector2 size;

		private RenderProcess renderProcess;
		private ITextureHandler textureHandler;
		private Exception textureRenderException;

		private bool mouseInWindow;
		private bool windowFocused;
		private InputModifier modifier;
		private ImGuiMouseCursor cursor;
		private bool captureCursor;

		public Inlay(RenderProcess renderProcess, Configuration config, InlayConfiguration inlayConfig)
		{
			this.renderProcess = renderProcess;
			this.config = config;
			this.inlayConfig = inlayConfig;
		}

		public void Dispose()
		{
			textureHandler?.Dispose();
			renderProcess.Send(new RemoveInlayRequest() { Guid = RenderGuid });
		}

		public void Navigate(string newUrl)
		{
			renderProcess.Send(new NavigateInlayRequest() { Guid = RenderGuid, Url = newUrl });
		}

		public void Debug()
		{
			renderProcess.Send(new DebugInlayRequest() { Guid = RenderGuid });
		}

		public void InvalidateTransport()
		{
			// Get old refs so we can clean up later
			var oldTextureHandler = textureHandler;
			var oldRenderGuid = RenderGuid;

			// Invalidate the handler, and reset the size to trigger a rebuild
			// Also need to generate a new renderer guid so we don't have a collision during the hand over
			// TODO: Might be able to tweak the logic in resize alongside this to shore up (re)builds
			textureHandler = null;
			size = Vector2.Zero;
			RenderGuid = Guid.NewGuid();

			// Clean up
			oldTextureHandler.Dispose();
			renderProcess.Send(new RemoveInlayRequest() { Guid = oldRenderGuid });
		}

		public void SetCursor(Cursor cursor)
		{
			captureCursor = cursor != Cursor.BrowserHostNoCapture;
			this.cursor = DecodeCursor(cursor);
		}

		public (bool, long) WndProcMessage(WindowsMessage msg, ulong wParam, long lParam)
		{
			// Check if there was a click, and use it to set the window focused state
			// We're avoiding ImGui for this, as we want to check for clicks entirely outside
			// ImGui's pervue to defocus inlays
			if (msg == WindowsMessage.WM_LBUTTONDOWN) { windowFocused = mouseInWindow && captureCursor; }

			// Bail if we're not focused
			// TODO: Revisit this for UI stuff, might not hold
			if (!windowFocused) { return (false, 0); }

			KeyEventType? eventType = msg switch
			{
				WindowsMessage.WM_KEYDOWN => KeyEventType.KeyDown,
				WindowsMessage.WM_SYSKEYDOWN => KeyEventType.KeyDown,
				WindowsMessage.WM_KEYUP => KeyEventType.KeyUp,
				WindowsMessage.WM_SYSKEYUP => KeyEventType.KeyUp,
				WindowsMessage.WM_CHAR => KeyEventType.Character,
				WindowsMessage.WM_SYSCHAR => KeyEventType.Character,
				_ => (KeyEventType?) null,
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

			renderProcess.Send(new KeyEventRequest()
			{
				Guid = RenderGuid,
				Type = eventType.Value,
				SystemKey = isSystemKey,
				UserKeyCode = (int)wParam,
				NativeKeyCode = (int)lParam,
				Modifier = modifier,
			});

			// We've handled the input, signal. For these message types, `0` signals a capture.
			return (true, 0);
		}

		public void Render()
		{
			if (!inlayConfig.Visible)
			{
				return;
			}

			ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
			ImGui.Begin($"{inlayConfig.Name}###{inlayConfig.Guid}", GetWindowFlags());

			HandleWindowSize();

			// TODO: Renderer can take some time to spin up properly, should add a loading state.
			if (textureHandler != null)
			{
				HandleMouseEvent();

				textureHandler.Render();
			}
			else if (textureRenderException != null)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
				ImGui.Text("An error occured while building the browser inlay texture:");
				ImGui.Text(textureRenderException.ToString());
				ImGui.PopStyleColor();
			}
			ImGui.End();
		}

		private ImGuiWindowFlags GetWindowFlags()
		{
			var flags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoTitleBar
				| ImGuiWindowFlags.NoCollapse
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoBringToFrontOnFocus
				| ImGuiWindowFlags.NoFocusOnAppearing;

			// ClickThrough is implicitly locked
			var locked = inlayConfig.Locked || inlayConfig.ClickThrough;

			if (locked)
			{
				flags |= ImGuiWindowFlags.None
					| ImGuiWindowFlags.NoMove
					| ImGuiWindowFlags.NoResize
					| ImGuiWindowFlags.NoBackground;
			}

			if (inlayConfig.ClickThrough || (!captureCursor && locked))
			{
				flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav;
			}

			return flags;
		}

		private void HandleMouseEvent()
		{
			// Render proc won't be ready on first boot
			// Totally skip mouse handling for click through inlays, as well
			if (renderProcess == null || inlayConfig.ClickThrough) { return; }

			var io = ImGui.GetIO();
			var windowPos = ImGui.GetWindowPos();
			var mousePos = io.MousePos - windowPos - ImGui.GetWindowContentRegionMin();

			// Generally we want to use IsWindowHovered for hit checking, as it takes z-stacking into account -
			// but when cursor isn't being actively captured, imgui will always return false - so fall back
			// so a slightly more naive hover check, just to maintain a bit of flood prevention.
			// TODO: Need to test how this will handle overlaps... fully transparent _shouldn't_ be accepting
			//       clicks so shouuulllddd beee fineee???
			var hovered = captureCursor
				? ImGui.IsWindowHovered()
				: ImGui.IsMouseHoveringRect(windowPos, windowPos + ImGui.GetWindowSize());

			// If the cursor is outside the window, send a final mouse leave then noop
			if (!hovered)
			{
				if (mouseInWindow)
				{
					mouseInWindow = false;
					renderProcess.Send(new MouseEventRequest()
					{
						Guid = RenderGuid,
						X = mousePos.X,
						Y = mousePos.Y,
						Leaving = true,
					});
				}
				return;
			}
			mouseInWindow = true;

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
			renderProcess.Send(new MouseEventRequest()
			{
				Guid = RenderGuid,
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

		private async void HandleWindowSize()
		{
			var currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			if (currentSize == size || resizing) { return; }

			// If there isn't a size yet, we haven't rendered at all - boot up an inlay in the render process
			// TODO: Edge case - if a user _somehow_ makes the size zero, this will freak out and generate a new render inlay
			// TODO: Maybe consolidate the request types? dunno.
			var request = size == Vector2.Zero
				? new NewInlayRequest()
				{
					Guid = RenderGuid,
					FrameTransportMode = config.FrameTransportMode,
					Url = inlayConfig.Url,
					Width = (int)currentSize.X,
					Height = (int)currentSize.Y,
				}
				: new ResizeInlayRequest()
				{
					Guid = RenderGuid,
					Width = (int)currentSize.X,
					Height = (int)currentSize.Y,
				} as DownstreamIpcRequest;

			resizing = true;

			var response = await renderProcess.Send<FrameTransportResponse>(request);
			if (!response.Success)
			{
				PluginLog.LogError("Texture build failure, retrying...");
				resizing = false;
				return;
			}

			size = currentSize;
			resizing = false;

			var oldTextureHandler = textureHandler;
			try
			{
				textureHandler = response.Data switch
				{
					TextureHandleResponse textureHandleResponse => new SharedTextureHandler(textureHandleResponse),
					BitmapBufferResponse bitmapBufferResponse => new BitmapBufferTextureHandler(bitmapBufferResponse),
					_ => throw new Exception($"Unhandled frame transport {response.GetType().Name}"),
				};
			}
			catch (Exception e) { textureRenderException = e; }
			if (oldTextureHandler != null) { oldTextureHandler.Dispose(); }
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
