using ImGuiNET;

namespace BrowserHost.Plugin.TextureHandlers
{
	class BitmapBufferTextureHandler : ITextureHandler
	{
		public void Dispose() { }

		public void Render()
		{
			ImGui.Text("Bitmap buffer");
		}
	}
}
