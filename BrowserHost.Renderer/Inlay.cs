using BrowserHost.Common;
using BrowserHost.Renderer.RenderHandlers;
using CefSharp;
using CefSharp.OffScreen;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace BrowserHost.Renderer
{
	class Inlay : IDisposable
	{
		private string url;

		private ChromiumWebBrowser browser;
		public BaseRenderHandler RenderHandler;

		public Inlay(string url, BaseRenderHandler renderHandler)
		{
			this.url = url;
			RenderHandler = renderHandler;
		}

		public void Initialise()
		{
			browser = new ChromiumWebBrowser(url, automaticallyCreateBrowser: false);
			browser.RenderHandler = RenderHandler;
			var size = RenderHandler.GetViewRect();

			// General browser config
			var windowInfo = new WindowInfo()
			{
				Width = size.Width,
				Height = size.Height,
			};
			windowInfo.SetAsWindowless(IntPtr.Zero);

			// WindowInfo gets ignored sometimes, be super sure:
			browser.BrowserInitialized += (sender, args) => { browser.Size = new Size(size.Width, size.Height); };

			var browserSettings = new BrowserSettings()
			{
				WindowlessFrameRate = 60,
			};

			// Ready, boot up the browser
			browser.CreateBrowser(windowInfo, browserSettings);

			browserSettings.Dispose();
			windowInfo.Dispose();
		}

		public void Dispose()
		{
			browser.RenderHandler = null;
			RenderHandler.Dispose();
			browser.Dispose();
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

		public void Debug()
		{
			browser.ShowDevTools();
		}

		public void HandleMouseEvent(MouseEventRequest request)
		{
			// If the browser isn't ready yet, noop
			if (browser == null || !browser.IsBrowserInitialized) { return; }

			var cursorX = (int)request.X;
			var cursorY = (int)request.Y;

			// Update the renderer's concept of the mouse cursor
			RenderHandler.SetMousePosition(cursorX, cursorY);

			var event_ = new MouseEvent(cursorX, cursorY, DecodeInputModifier(request.Modifier));

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
			RenderHandler.Resize(size);
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
