using BrowserHost.Common;
using CefSharp;
using CefSharp.OffScreen;
using System;
using System.Collections.Generic;
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
			renderHandler = new TextureRenderHandler(size);
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

		public void Navigate(string newUrl)
		{
			browser.Load(newUrl);
		}

		public void HandleMouseEvent(MouseEventRequest request)
		{
			// If the browser isn't ready yet, noop
			if (browser == null || !browser.IsBrowserInitialized) { return; }

			// TODO: Handle key modifiers
			var event_ = new MouseEvent((int)request.X, (int)request.Y, CefEventFlags.None);

			var host = browser.GetBrowserHost();

			// Ensure the mouse position is up to date
			// TODO: the `false` is mouseLeave, which may be what we want for moving off-window? Research.
			host.SendMouseMoveEvent(event_, false);

			// Fire any relevant click events
			var doubleClicks = DecodeMouseButtons(request.Double);
			DecodeMouseButtons(request.Down)
					.ForEach(button => host.SendMouseClickEvent(event_, button, false, doubleClicks.Contains(button) ? 2 : 1));
			DecodeMouseButtons(request.Up).ForEach(button => host.SendMouseClickEvent(event_, button, true, 1));
		}

		public void Resize(Size size)
		{
			// Need to resize renderer first, the browser will check it (and hence the texture) when browser.Size is set.
			renderHandler.Resize(size);
			browser.Size = size;
		}

		private List<MouseButtonType> DecodeMouseButtons(MouseButton buttons)
		{
			var result = new List<MouseButtonType>();
			if ((buttons & MouseButton.Primary) == MouseButton.Primary) { result.Add(MouseButtonType.Left); }
			if ((buttons & MouseButton.Secondary) == MouseButton.Secondary) { result.Add(MouseButtonType.Right); }
			if ((buttons & MouseButton.Tertiary) == MouseButton.Tertiary) { result.Add(MouseButtonType.Middle); }
			return result;
		}
	}
}
