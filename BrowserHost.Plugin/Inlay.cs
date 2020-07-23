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

		public void Render()
		{
			if (ImGui.Begin($"{Name}##BrowserHostInlay"))
			{
				// TODO: Shortcut the entire window if it's not ready? Or should I add a loader?
				if (textureWrap != null)
				{
					// TODO: Better handling for this.
					var io = ImGui.GetIO();
					var relativeMousePos = io.MousePos - ImGui.GetWindowPos() - ImGui.GetWindowContentRegionMin();
					MouseMove(relativeMousePos);

					ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
				}
			}
			ImGui.End();
		}

		// TODO: Proper only current focus, etc, etc, etc
		private void MouseMove(Vector2 position)
		{
			// TODO: lmao, yikes
			if (renderProcess == null) { return; }
			// TODO: This should probably be async so we're not blocking the render thread with IPC
			renderProcess.Send(new MouseMoveRequest()
			{
				Guid = Guid,
				X = position.X,
				Y = position.Y,
			});
		}
	}
}
