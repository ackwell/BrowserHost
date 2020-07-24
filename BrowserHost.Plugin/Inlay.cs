using BrowserHost.Common;
using ImGuiNET;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using ImGuiScene;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Inlay
	{
		public string Name;
		public string Url;
		public ushort Width;
		public ushort Height;

		public Guid Guid { get; } = Guid.NewGuid();

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
				Width = Width,
				Height = Height,
			});

			// Build up the texture from the shared handle
			var texture = DxHandler.Device.OpenSharedResource<D3D11.Texture2D>(response.TextureHandle);
			var view = new D3D11.ShaderResourceView(DxHandler.Device, texture, new D3D11.ShaderResourceViewDescription()
			{
				Format = texture.Description.Format,
				Dimension = D3D.ShaderResourceViewDimension.Texture2D,
				Texture2D = { MipLevels = texture.Description.MipLevels },
			});

			textureWrap = new D3DTextureWrap(view, texture.Description.Width, texture.Description.Height);
		}

		public void SetCursor(Cursor cursor)
		{
			// TODO: Map properly
			this.cursor = cursor == Cursor.Pointer
				? ImGuiMouseCursor.Hand
				: ImGuiMouseCursor.Arrow;
		}

		public void Render()
		{
			// TODO: Renderer can take some time to spin up properly, should add a loading state.
			if (ImGui.Begin($"{Name}##BrowserHostInlay") && textureWrap != null)
			{
				HandleMouseEvent();

				// TODO: Overlapping windows will likely cause nondeterministic cursor handling here.
				// Need to ignore cursor if mouse outside window, and work out how (and if) i deal with overlap.
				ImGui.SetMouseCursor(cursor);

				ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
			}
			ImGui.End();
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
				Up = EncodeMouseButtons(io.MouseReleased),
			};

			// TODO: Either this or the entire handler function should be asynchronous so we're not blocking the entire draw thread
			renderProcess.Send(request);
		}

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
	}
}
