using ImGuiScene;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using System.Numerics;
using ImGuiNET;

namespace BrowserHost.Plugin.TextureHandlers
{
	class SharedTextureHandler : ITextureHandler
	{
		private TextureWrap textureWrap;

		public SharedTextureHandler(IntPtr textureHandle)
		{
			var texture = DxHandler.Device.OpenSharedResource<D3D11.Texture2D>(textureHandle);
			var view = new D3D11.ShaderResourceView(DxHandler.Device, texture, new D3D11.ShaderResourceViewDescription()
			{
				Format = texture.Description.Format,
				Dimension = D3D.ShaderResourceViewDimension.Texture2D,
				Texture2D = { MipLevels = texture.Description.MipLevels },
			});

			textureWrap = new D3DTextureWrap(view, texture.Description.Width, texture.Description.Height);
		}

		public void Dispose()
		{
			textureWrap.Dispose();
		}

		public void Render()
		{
			if (textureWrap == null) { return; }

			ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
		}
	}
}
