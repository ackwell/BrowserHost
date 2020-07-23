using BrowserHost.Common;
using CefSharp;
using CefSharp.OffScreen;
using System;
using System.Drawing;

namespace BrowserHost.Renderer
{
	class Inlay : IDisposable
	{
		public IntPtr SharedTextureHandle => renderHandler.SharedTextureHandle;

		public event EventHandler<Cursor> CursorChanged
		{
			add { renderHandler.CursorChanged += value; }
			remove { renderHandler.CursorChanged -= value; }
		}

		private string url;
		private Size size;

		private ChromiumWebBrowser browser;
		private TextureRenderHandler renderHandler;

		public Inlay(string url, Size size)
		{
			this.url = url;
			this.size = size;
		}

		public void Initialise()
		{
			browser = new ChromiumWebBrowser(url, automaticallyCreateBrowser: false);

			// Set up the DX texture-based rendering
			renderHandler = new TextureRenderHandler(DxHandler.Device, size);
			browser.RenderHandler = renderHandler;

			// General browser config
			var windowInfo = new WindowInfo()
			{
				Width = size.Width,
				Height = size.Height,
			};
			windowInfo.SetAsWindowless(IntPtr.Zero);

			var browserSettings = new BrowserSettings()
			{
				WindowlessFrameRate = 60,
			};

			// TODO: Proper resize handling
			// WindowInfo gets ignored sometimes, be super sure:
			browser.BrowserInitialized += (sender, args) => { browser.Size = size; };

			// Ready, boot up the browser
			browser.CreateBrowser(windowInfo, browserSettings);

			browserSettings.Dispose();
			windowInfo.Dispose();
		}

		public void Dispose()
		{
			browser.Dispose();
			renderHandler.Dispose();
		}

		public void MouseMove(float x, float y)
		{
			// TODO: nicer way of handling this?
			if (browser == null || !browser.IsBrowserInitialized) { return; }
			// TODO: Modifiers
			var modifiers = CefEventFlags.None;
			browser.GetBrowserHost().SendMouseMoveEvent((int)x, (int)y, false, modifiers);
		}
	}
}
