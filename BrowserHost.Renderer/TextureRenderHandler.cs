using BrowserHost.Common;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using SharpDX;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;

namespace BrowserHost.Renderer
{
	class TextureRenderHandler : IRenderHandler
	{
		private D3D11.Texture2D texture;

		private IntPtr sharedTextureHandle = IntPtr.Zero;
		public IntPtr SharedTextureHandle {
			get
			{
				if (sharedTextureHandle == IntPtr.Zero)
				{
					using (var resource = texture.QueryInterface<DXGI.Resource>())
					{
						sharedTextureHandle = resource.SharedHandle;
					}
				}
				return sharedTextureHandle;
			}
		}

		public event EventHandler<Cursor> CursorChanged;

		public TextureRenderHandler(D3D11.Device device, System.Drawing.Size size)
		{
			// Build texture. Most of these properties are defined to match how CEF exposes the render buffer.
			texture = new D3D11.Texture2D(device, new D3D11.Texture2DDescription()
			{
				Width = size.Width,
				Height = size.Height,
				MipLevels = 1,
				ArraySize = 1,
				Format = DXGI.Format.B8G8R8A8_UNorm,
				SampleDescription = new DXGI.SampleDescription(1, 0),
				Usage = D3D11.ResourceUsage.Default,
				BindFlags = D3D11.BindFlags.ShaderResource,
				CpuAccessFlags = D3D11.CpuAccessFlags.None,
				// TODO: Look into getting SharedKeyedmutex working without a CTD from the plugin side.
				OptionFlags = D3D11.ResourceOptionFlags.Shared,
			});
		}

		public void Dispose()
		{
			texture.Dispose();
		}

		# region IRenderHandler implementation

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
			// TODO: How is resizing going to work?
			var texDesc = texture.Description;
			return new Rect(0, 0, texDesc.Width, texDesc.Height);
		}

		public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			// TODO: Handle popups
			if (type == PaintElementType.Popup) { return; }

			// TODO: Define this better. The incoming buffer is a ptr to a 32-bit BGRA buffer, so 4 bytes per pixel.
			var bytesPerPixel = 4;

			var context = texture.Device.ImmediateContext;

			// TODO: This is very much a cruddy MVP impl. Few things to look into:
			//   - STAGING texture w/ CopySubresourceRegion
			//   - Only updating the dirty rect
			//   - Maps?
			context.UpdateSubresource(new DataBox(buffer, width * bytesPerPixel, width * height * bytesPerPixel), texture);
			context.Flush();
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

		public void OnCursorChange(IntPtr cursorPtr, CursorType type, CursorInfo customCursorInfo)
		{
			Console.WriteLine($"CefCursor: {type}");

			// TODO: Map properly
			// CEF calls default "pointer", and pointer "hand". Derp.
			var cursor = type == CursorType.Hand ? Cursor.Pointer : Cursor.Default;

			CursorChanged?.Invoke(this, cursor);
		}

		public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y)
		{
			// Returning false to abort drag operations.
			return false;
		}

		public void UpdateDragCursor(DragOperationsMask operation)
		{
		}

		#endregion
	}
}
