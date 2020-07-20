using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using D3D11 = SharpDX.Direct3D11;
using System;
using SharpDX;

namespace BrowserHost.Renderer
{
	class TextureRenderHandler : IRenderHandler
	{
		private ChromiumWebBrowser browser;
		private D3D11.Texture2D texture;

		public TextureRenderHandler(ChromiumWebBrowser browser, D3D11.Texture2D texture)
		{
			this.browser = browser;
			this.texture = texture;
		}

		public void Dispose()
		{
			browser = null;
		}

		public ScreenInfo? GetScreenInfo()
		{
			// TODO: Cache?
			return new ScreenInfo() { DeviceScaleFactor = 1.0F };
		}

		public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
		{
			// TODO: Should we bother with this?
			screenX = viewX;
			screenY = viewY;

			return false;
		}

		public Rect GetViewRect()
		{
			// TODO: Get rect from texture? How is resizing going to work?
			var size = browser.Size;
			return new Rect(0, 0, size.Width, size.Height);
		}

		public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			// TODO: Handle popups
			if (type == PaintElementType.Popup) { return; }

			// TODO: Reuse this, should only need one.

			// TODO: Define this better. The incoming buffer is a ptr to a 32-bit BGRA buffer, so 4 bytes per pixel.
			var bytesPerPixel = 4;
			//texture.Device.ImmediateContext.

			// TODO: Map?
			//texture.Device.ImmediateContext.CopySubresourceRegion();
			// TODO: copy subresource region?
			texture.Device.ImmediateContext.UpdateSubresource(new DataBox(buffer, width * bytesPerPixel, width * height * bytesPerPixel), texture);
			texture.Device.ImmediateContext.Flush();

			// TODO: render to a shared texture
			//throw new NotImplementedException();
		}

		public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, IntPtr sharedHandle)
		{
			// UNUSED
			// CEF has removed support for DX accelerated paint shared textures, pending re-implementation in
			// chromium's new compositor, Vis. Ref: https://bitbucket.org/chromiumembedded/cef/issues/2575/viz-implementation-for-osr
		}

		public void OnPopupShow(bool show)
		{
			throw new NotImplementedException();
		}

		public void OnPopupSize(Rect rect)
		{
			throw new NotImplementedException();
		}

		public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
		{
		}

		public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds)
		{
		}

		public void OnCursorChange(IntPtr cursor, CursorType type, CursorInfo customCursorInfo)
		{
			// TODO: Might need to implement cursor stuff for QoL down the track.
		}

		public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y)
		{
			// Returning false to abort drag operations.
			return false;
		}

		public void UpdateDragCursor(DragOperationsMask operation)
		{
		}
	}
}
