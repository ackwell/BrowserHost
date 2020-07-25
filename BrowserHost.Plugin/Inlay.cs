using BrowserHost.Common;
using ImGuiNET;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using ImGuiScene;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Inlay : IDisposable
	{
		public string Name;
		public string Url;
		public Vector2 Size;
		public Guid Guid;
		public bool Locked;
		public bool ClickThrough;

		private RenderProcess renderProcess;
		private TextureWrap textureWrap;

		private ImGuiMouseCursor cursor;

		public Inlay(RenderProcess renderProcess)
		{
			this.renderProcess = renderProcess;
		}

		public void Initialise()
		{
			// Build the inlay on the renderer
			var response = renderProcess.Send<NewInlayResponse>(new NewInlayRequest()
			{
				Guid = Guid,
				Url = Url,
				Width = (int)Size.X,
				Height = (int)Size.Y,
			});

			textureWrap = BuildTextureWrap(response.TextureHandle);
		}

		public void Dispose()
		{
			textureWrap.Dispose();
		}

		public void SetCursor(Cursor cursor)
		{
			this.cursor = DecodeCursor(cursor);
		}

		public void Render()
		{
			// TODO: Renderer can take some time to spin up properly, should add a loading state.
			ImGui.Begin($"{Name}###{Guid}", GetWindowFlags());
			if (textureWrap != null)
			{
				HandleMouseEvent();
				HandleResize();

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

			if (Locked) { flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize; }
			if (ClickThrough) { flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav; }

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
				Guid = Guid,
				X = mousePos.X,
				Y = mousePos.Y,
				Down = EncodeMouseButtons(io.MouseClicked),
				Double = EncodeMouseButtons(io.MouseDoubleClicked),
				Up = EncodeMouseButtons(io.MouseReleased),
			};

			// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
			renderProcess.Send(request);
		}

		private void HandleResize()
		{
			var currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			if (currentSize == Size) { return; }

			// TODO: Wonder if I should just use imgui's .ini as the SOT for the size, and wait a frame before rendering to fetch?
			//       Alternatively, might be a _lot_ of junk in the ini doing that way, so json config might be "cleaner".
			Size = currentSize;

			var response = renderProcess.Send<ResizeInlayResponse>(new ResizeInlayRequest()
			{
				Guid = Guid,
				Width = (int)Size.X,
				Height = (int)Size.Y,
			});

			var oldTextureWrap = textureWrap;
			textureWrap = BuildTextureWrap(response.TextureHandle);
			oldTextureWrap.Dispose();
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
