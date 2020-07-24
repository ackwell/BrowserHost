using BrowserHost.Common;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;

namespace BrowserHost.Renderer
{
	class TextureRenderHandler : IRenderHandler
	{
		// CEF buffers are 32-bit BGRA
		private const byte bytesPerPixel = 4;

		private D3D11.Texture2D texture;
		private D3D11.Texture2D popupTexture;

		private bool popupVisible;
		private Rect popupRect;

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

		public TextureRenderHandler(System.Drawing.Size size)
		{
			texture = BuildViewTexture(size);
		}

		public void Dispose()
		{
			texture.Dispose();
		}

		public void Resize(System.Drawing.Size size)
		{
			var oldTexture = texture;
			texture = BuildViewTexture(size);
			// Need to clear the cached handle value
			// TODO: Maybe I should just avoid the lazy cache and do it eagerly on texture build.
			sharedTextureHandle = IntPtr.Zero;
			if (oldTexture != null) { oldTexture.Dispose(); }
		}

		private D3D11.Texture2D BuildViewTexture(System.Drawing.Size size)
		{
			// Build texture. Most of these properties are defined to match how CEF exposes the render buffer.
			return new D3D11.Texture2D(DxHandler.Device, new D3D11.Texture2DDescription()
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
			var rowPitch = width * bytesPerPixel;
			var depthPitch = rowPitch * height;
			var sourceRegionPtr = buffer + (dirtyRect.X * bytesPerPixel) + (dirtyRect.Y * rowPitch);
			var destinationRegion = new D3D11.ResourceRegion(dirtyRect.X, dirtyRect.Y, 0, dirtyRect.X + dirtyRect.Width, dirtyRect.Y + dirtyRect.Height, 1);

			switch (type)
			{
				case PaintElementType.View:
					if (width != texture.Description.Width || height != texture.Description.Height)
					{
						// TODO: Render something other than literally nothing while waiting for size to settle.
						break;
					}
					OnPaintView(sourceRegionPtr, rowPitch, depthPitch, destinationRegion);
					break;
				case PaintElementType.Popup:
					OnPaintPopup(sourceRegionPtr, rowPitch, depthPitch, destinationRegion);
					break;
			}
		}

		private void OnPaintView(IntPtr source, int sourceRowPitch, int sourceDepthPitch, D3D11.ResourceRegion destinationRegion)
		{
			var context = texture.Device.ImmediateContext;

			// TODO: This likely has some avenues for optimisation still.
			//   - STAGING texture w/ CopySubresourceRegion
			//   - Maps?
			//   - Texture array for layering popup (would shared permit this?)
			context.UpdateSubresource(texture, 0, destinationRegion, source, sourceRowPitch, sourceDepthPitch);

			if (popupVisible)
			{
				context.CopySubresourceRegion(popupTexture, 0, null, texture, 0, popupRect.X, popupRect.Y);
			}

			context.Flush();
		}

		private void OnPaintPopup(IntPtr source, int sourceRowPitch, int sourceDepthPitch, D3D11.ResourceRegion destinationRegion)
		{
			var context = popupTexture.Device.ImmediateContext;

			// See comment in `OnPaintView` re: optimisation of rendering.
			context.UpdateSubresource(popupTexture, 0, destinationRegion, source, sourceRowPitch, sourceDepthPitch);

			// We're not flushing here, relying on the primary view to flush for us.
		}

		public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, IntPtr sharedHandle)
		{
			// UNUSED
			// CEF has removed support for DX accelerated paint shared textures, pending re-implementation in
			// chromium's new compositor, Vis. Ref: https://bitbucket.org/chromiumembedded/cef/issues/2575/viz-implementation-for-osr
		}

		public void OnPopupShow(bool show)
		{
			popupVisible = show;
			// TODO: May need to set a "clean up" flag when true->false to re-render the popup surface as well as dirty rect,
			//       once I start dealing with the dirty rect.
		}

		public void OnPopupSize(Rect rect)
		{
			popupRect = rect;

			// I'm really not sure if this happens. If it does, frequently - will probably need 2x shared textures and some jazz.
			var texDesc = texture.Description;
			if (rect.Width > texDesc.Width || rect.Height > texDesc.Height)
			{
				Console.Error.WriteLine($"Trying to build popup layer ({rect.Width}x{rect.Height}) larger than primary surface ({texDesc.Width}x{texDesc.Height}).");
			}

			// Get a reference to the old texture, we'll make sure to assign a new texture before disposing the old one.
			var oldTexture = popupTexture;

			// Build a texture for the new sized popup
			popupTexture = new D3D11.Texture2D(texture.Device, new D3D11.Texture2DDescription()
			{
				Width = rect.Width,
				Height = rect.Height,
				MipLevels = 1,
				ArraySize = 1,
				Format = DXGI.Format.B8G8R8A8_UNorm,
				SampleDescription = new DXGI.SampleDescription(1, 0),
				Usage = D3D11.ResourceUsage.Default,
				BindFlags = D3D11.BindFlags.ShaderResource,
				CpuAccessFlags = D3D11.CpuAccessFlags.None,
				OptionFlags = D3D11.ResourceOptionFlags.None,
			});

			if (oldTexture != null) { oldTexture.Dispose(); }
		}

		public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
		{
		}

		public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds)
		{
		}

		public void OnCursorChange(IntPtr cursorPtr, CursorType type, CursorInfo customCursorInfo)
		{
			CursorChanged?.Invoke(this, EncodeCursor(type));
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

		#region Cursor encoding

		private Cursor EncodeCursor(CursorType cursor)
		{
			switch (cursor)
			{
				// CEF calls default "pointer", and pointer "hand". Derp.
				case CursorType.Pointer: return Cursor.Default;
				case CursorType.Cross: return Cursor.Crosshair;
				case CursorType.Hand: return Cursor.Pointer;
				case CursorType.IBeam: return Cursor.Text;
				case CursorType.Wait: return Cursor.Wait;
				case CursorType.Help: return Cursor.Help;
				case CursorType.EastResize: return Cursor.EResize;
				case CursorType.NorthResize: return Cursor.NResize;
				case CursorType.NortheastResize: return Cursor.NEResize;
				case CursorType.NorthwestResize: return Cursor.NWResize;
				case CursorType.SouthResize: return Cursor.SResize;
				case CursorType.SoutheastResize: return Cursor.SEResize;
				case CursorType.SouthwestResize: return Cursor.SWResize;
				case CursorType.WestResize: return Cursor.WResize;
				case CursorType.NorthSouthResize: return Cursor.NSResize;
				case CursorType.EastWestResize: return Cursor.EWResize;
				case CursorType.NortheastSouthwestResize: return Cursor.NESWResize;
				case CursorType.NorthwestSoutheastResize: return Cursor.NWSEResize;
				case CursorType.ColumnResize: return Cursor.ColResize;
				case CursorType.RowResize: return Cursor.RowResize;

				// There isn't really support for panning right now. Default to all-scroll.
				case CursorType.MiddlePanning:
				case CursorType.EastPanning:
				case CursorType.NorthPanning:
				case CursorType.NortheastPanning:
				case CursorType.NorthwestPanning:
				case CursorType.SouthPanning:
				case CursorType.SoutheastPanning:
				case CursorType.SouthwestPanning:
				case CursorType.WestPanning:
					return Cursor.AllScroll;

				case CursorType.Move: return Cursor.Move;
				case CursorType.VerticalText: return Cursor.VerticalText;
				case CursorType.Cell: return Cursor.Cell;
				case CursorType.ContextMenu: return Cursor.ContextMenu;
				case CursorType.Alias: return Cursor.Alias;
				case CursorType.Progress: return Cursor.Progress;
				case CursorType.NoDrop: return Cursor.NoDrop;
				case CursorType.Copy: return Cursor.Copy;
				case CursorType.None: return Cursor.None;
				case CursorType.NotAllowed: return Cursor.NotAllowed;
				case CursorType.ZoomIn: return Cursor.ZoomIn;
				case CursorType.ZoomOut: return Cursor.ZoomOut;
				case CursorType.Grab: return Cursor.Grab;
				case CursorType.Grabbing: return Cursor.Grabbing;

				// Not handling custom for now
				case CursorType.Custom: return Cursor.Default;
			}

			// Unmapped cursor, log and default
			Console.WriteLine($"Switching to unmapped cursor type {cursor}.");
			return Cursor.Default;
		}

		#endregion
	}
}
