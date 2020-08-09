using System;

namespace BrowserHost.Plugin.TextureHandlers
{
	interface ITextureHandler : IDisposable
	{
		public void Render();
	}
}
