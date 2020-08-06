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
		private JsApi jsApi;

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

			// WindowInfo gets ignored sometimes, be super sure:
			browser.BrowserInitialized += (sender, args) => { browser.Size = size; };

			var browserSettings = new BrowserSettings()
			{
				WindowlessFrameRate = 60,
			};

			// Register the JS API
			// TODO: Restrict to inlays opting into the api
			// TODO: Set up a proper client-side api. will need IRenderProcessMessageHandler and a chunk of injected code
			browser.JavascriptObjectRepository.ResolveObject += (sender, args) =>
			{
				if (args.ObjectName != "hostApi") { return; }

				if (jsApi == null) { jsApi = new JsApi(); }
				args.ObjectRepository.Register(args.ObjectName, jsApi, isAsync: true);
			};

			// Ready, boot up the browser
			browser.CreateBrowser(windowInfo, browserSettings);

			browserSettings.Dispose();
			windowInfo.Dispose();
		}

		public void Dispose()
		{
			browser.RenderHandler = null;
			renderHandler.Dispose();
			browser.Dispose();

			jsApi?.Dispose();
		}

		public void Navigate(string newUrl)
		{
			// If navigating to the same url, force a clean reload
			if (browser.Address == newUrl)
			{
				browser.Reload(true);
				return;
			}

			// Otherwise load regularly
			url = newUrl;
			browser.Load(newUrl);
		}

		public void Send(string name, object data)
		{
			jsApi?.Send(name, data);
		}

		public void Debug()
		{
			browser.ShowDevTools();
		}

		public void HandleMouseEvent(MouseEventRequest request)
		{
			// If the browser isn't ready yet, noop
			if (browser == null || !browser.IsBrowserInitialized) { return; }

			// TODO: Handle key modifiers
			var event_ = new MouseEvent((int)request.X, (int)request.Y, DecodeInputModifier(request.Modifier));

			var host = browser.GetBrowserHost();

			// Ensure the mouse position is up to date
			host.SendMouseMoveEvent(event_, request.Leaving);

			// Fire any relevant click events
			var doubleClicks = DecodeMouseButtons(request.Double);
			DecodeMouseButtons(request.Down)
					.ForEach(button => host.SendMouseClickEvent(event_, button, false, doubleClicks.Contains(button) ? 2 : 1));
			DecodeMouseButtons(request.Up).ForEach(button => host.SendMouseClickEvent(event_, button, true, 1));

			// CEF treats the wheel delta as mode 0, pixels. Bump up the numbers to match typical in-browser experience.
			var deltaMult = 100;
			host.SendMouseWheelEvent(event_, (int)request.WheelX * deltaMult, (int)request.WheelY * deltaMult);
		}

		public void HandleKeyEvent(KeyEventRequest request)
		{
			var type = request.Type switch
			{
				Common.KeyEventType.KeyDown => CefSharp.KeyEventType.RawKeyDown,
				Common.KeyEventType.KeyUp => CefSharp.KeyEventType.KeyUp,
				Common.KeyEventType.Character => CefSharp.KeyEventType.Char,
				_ => throw new ArgumentException($"Invalid KeyEventType {request.Type}")
			};

			browser.GetBrowserHost().SendKeyEvent(new KeyEvent()
			{
				Type = type,
				Modifiers = DecodeInputModifier(request.Modifier),
				WindowsKeyCode = request.UserKeyCode,
				NativeKeyCode = request.NativeKeyCode,
				IsSystemKey = request.SystemKey,
			});
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

		private CefEventFlags DecodeInputModifier(InputModifier modifier)
		{
			var result = CefEventFlags.None;
			if ((modifier & InputModifier.Shift) == InputModifier.Shift) { result |= CefEventFlags.ShiftDown; }
			if ((modifier & InputModifier.Control) == InputModifier.Control) { result |= CefEventFlags.ControlDown; }
			if ((modifier & InputModifier.Alt) == InputModifier.Alt) { result |= CefEventFlags.AltDown; }
			return result;
		}
	}
}
