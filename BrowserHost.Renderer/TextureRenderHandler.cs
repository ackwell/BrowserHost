using BrowserHost.Common;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Collections.Concurrent;

namespace BrowserHost.Renderer
{
	class TextureRenderHandler : IRenderHandler
	{
		// CEF buffers are 32-bit BGRA
		private const byte bytesPerPixel = 4;

		private D3D11.Texture2D texture;
		private D3D11.Texture2D popupTexture;
		private ConcurrentBag<D3D11.Texture2D> obsoluteTextures = new ConcurrentBag<D3D11.Texture2D>();

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
			if (popupTexture != null) { popupTexture.Dispose(); }
			foreach (var texture in obsoluteTextures) { texture.Dispose(); }
		}

		public void Resize(System.Drawing.Size size)
		{
			if (texture != null) { obsoluteTextures.Add(texture); }
			texture = BuildViewTexture(size);
			// Need to clear the cached handle value
			// TODO: Maybe I should just avoid the lazy cache and do it eagerly on texture build.
			sharedTextureHandle = IntPtr.Zero;
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
			return new ScreenInfo() { DeviceScaleFactor = 1.0F };
		}

		public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
		{
			screenX = viewX;
			screenY = viewY;

			return false;
		}

		public Rect GetViewRect()
		{
			// There's a very small chance that OnPaint's cleanup will delete the current texture midway through this function -
			// Try a few times just in case before failing out with an obviously-wrong value
			// hi adam
			for (var i = 0; i < 5; i++)
			{
				try { return GetViewRectInternal(); }
				catch (NullReferenceException) { }
			}
			return new Rect(0, 0, 1, 1);
		}

		private Rect GetViewRectInternal()
		{
			var texDesc = texture.Description;
			return new Rect(0, 0, texDesc.Width, texDesc.Height);
		}

		public void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			var targetTexture = type switch
			{
				PaintElementType.Popup => popupTexture,
				_ => texture,
			};

			var texDesc = targetTexture.Description;

			var rowPitch = width * bytesPerPixel;
			var depthPitch = rowPitch * height;
			var sourceRegionPtr = buffer + (dirtyRect.X * bytesPerPixel) + (dirtyRect.Y * rowPitch);
			var destinationRegion = new D3D11.ResourceRegion()
			{
				Top = Math.Min(dirtyRect.Y, texDesc.Height),
				Bottom = Math.Min(dirtyRect.Y + dirtyRect.Height, texDesc.Height),
				Left = Math.Min(dirtyRect.X, texDesc.Width),
				Right = Math.Min(dirtyRect.X + dirtyRect.Width, texDesc.Width),
				Front = 0,
				Back = 1,
			};

			var context = targetTexture.Device.ImmediateContext;
			context.UpdateSubresource(targetTexture, 0, destinationRegion, sourceRegionPtr, rowPitch, depthPitch);

			// Only need to do composition + flush on primary texture
			if (type != PaintElementType.View) { return; }

			if (popupVisible)
			{
				context.CopySubresourceRegion(popupTexture, 0, null, targetTexture, 0, popupRect.X, popupRect.Y);
			}

			context.Flush();

			// Rendering is complete, clean up any obsolute textures textures
			var textures = obsoluteTextures;
			obsoluteTextures = new ConcurrentBag<D3D11.Texture2D>();
			foreach (var texture in textures) { texture.Dispose(); }
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
