using BrowserHost.Common;
using ImGuiNET;
using ImGuiScene;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Inlay : IDisposable
	{
		public InlayConfiguration Config;

		private Vector2 size;

		private RenderProcess renderProcess;
		private TextureWrap textureWrap;

		private ImGuiMouseCursor cursor;

		public Inlay(RenderProcess renderProcess, InlayConfiguration config)
		{
			this.renderProcess = renderProcess;
			Config = config;
		}

		public void Dispose()
		{
			textureWrap.Dispose();
			renderProcess.Send(new RemoveInlayRequest() { Guid = Config.Guid });
		}

		public void Navigate(string newUrl)
		{
			renderProcess.Send(new NavigateInlayRequest() { Guid = Config.Guid, Url = newUrl });
		}

		public void SetCursor(Cursor cursor)
		{
			this.cursor = DecodeCursor(cursor);
		}

		public void Render()
		{
			ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
			ImGui.Begin($"{Config.Name}###{Config.Guid}", GetWindowFlags());

			HandleWindowSize();

			// TODO: Renderer can take some time to spin up properly, should add a loading state.
			if (textureWrap != null)
			{
				HandleMouseEvent();

				// TODO: Overlapping windows will likely cause nondeterministic cursor handling here.
				// Need to ignore cursor if mouse outside window, and work out how (and if) i deal with overlap.
				ImGui.SetMouseCursor(cursor);

				ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
			}
			ImGui.End();
		}

		private ImGuiWindowFlags GetWindowFlags()
		{
			var flags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoCollapse
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse;

			if (Config.Locked) { flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize; }
			if (Config.ClickThrough) { flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav; }

			return flags;
		}

		// TODO: Dedupe when mouse isn't moving (will need to check change manually, imgui posprev isn't working).
		// TODO: Don't send mouse input if mouse is outside bound of texture. Might need to signal a frame leave?
		private void HandleMouseEvent()
		{
			// Render proc won't be ready on first boot
			if (renderProcess == null) { return; }

			var io = ImGui.GetIO();
			var mousePos = io.MousePos - ImGui.GetWindowPos() - ImGui.GetWindowContentRegionMin();

			var request = new MouseEventRequest()
			{
				Guid = Config.Guid,
				X = mousePos.X,
				Y = mousePos.Y,
				Down = EncodeMouseButtons(io.MouseClicked),
				Double = EncodeMouseButtons(io.MouseDoubleClicked),
				Up = EncodeMouseButtons(io.MouseReleased),
				WheelX = io.MouseWheelH,
				WheelY = io.MouseWheel,
			};

			// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
			renderProcess.Send(request);
		}

		private void HandleWindowSize()
		{
			var currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			if (currentSize == size) { return; }

			// If there isn't a size yet, we haven't rendered at all - boot up an inlay in the render process
			// TODO: Edge case - if a user _somehow_ makes the size zero, this will freak out and generate a new render inlay
			// TODO: Maybe consolidate the request types? dunno.
			var request = size == Vector2.Zero
				? new NewInlayRequest()
				{
					Guid = Config.Guid,
					Url = Config.Url,
					Width = (int)currentSize.X,
					Height = (int)currentSize.Y,
				}
				: new ResizeInlayRequest()
				{
					Guid = Config.Guid,
					Width = (int)currentSize.X,
					Height = (int)currentSize.Y,
				} as DownstreamIpcRequest;

			var response = renderProcess.Send<TextureHandleResponse>(request);

			var oldTextureWrap = textureWrap;
			textureWrap = BuildTextureWrap(response.TextureHandle);
			if (oldTextureWrap != null) { oldTextureWrap.Dispose(); }

			size = currentSize;
		}

		// TODO: This seems like a lot of junk to do every time we resize... is it possible to reuse some of this?
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
