using CefSharp;
using CefSharp.Structs;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace BrowserHost.Renderer.RenderHandlers
{
	class TextureRenderHandler : BaseRenderHandler
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

		// Transparent background click-through state
		private IntPtr bufferPtr;
		private int bufferWidth;
		private int bufferHeight;

		public TextureRenderHandler(System.Drawing.Size size)
		{
			texture = BuildViewTexture(size);
		}

		public override void Dispose()
		{
			texture.Dispose();
			if (popupTexture != null) { popupTexture.Dispose(); }
			foreach (var texture in obsoluteTextures) { texture.Dispose(); }
		}

		public override void Resize(System.Drawing.Size size)
		{
			var oldTexture = texture;
			texture = BuildViewTexture(size);
			if (oldTexture != null) { obsoluteTextures.Add(oldTexture); }
			// Need to clear the cached handle value
			// TODO: Maybe I should just avoid the lazy cache and do it eagerly on texture build.
			sharedTextureHandle = IntPtr.Zero;
		}

		// Nasty shit needs nasty attributes.
		[HandleProcessCorruptedStateExceptions]
		protected override byte GetAlphaAt(int x, int y)
		{
			var rowPitch = bufferWidth * bytesPerPixel;

			// Get the offset for the alpha of the cursor's current position. Bitmap buffer is BGRA, so +3 to get alpha byte
			var cursorAlphaOffset = 0
				 + (Math.Min(Math.Max(x, 0), bufferWidth - 1) * bytesPerPixel)
				 + (Math.Min(Math.Max(y, 0), bufferHeight - 1) * rowPitch)
				 + 3;

			byte alpha;
			try { alpha = Marshal.ReadByte(bufferPtr + cursorAlphaOffset); }
			catch
			{
				Console.Error.WriteLine("Failed to read alpha value from cef buffer.");
				return 255;
			}

			return alpha;
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

		public override Rect GetViewRect()
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

		public override void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			var targetTexture = type switch
			{
				PaintElementType.View => texture,
				PaintElementType.Popup => popupTexture,
				_ => throw new Exception($"Unknown paint type {type}"),
			};

			// Nasty hack; we're keeping a ref to the view buffer for pixel lookups without going through DX
			if (type == PaintElementType.View)
			{
				bufferPtr = buffer;
				bufferWidth = width;
				bufferHeight = height;
			}

			// Calculate offset multipliers for the current buffer
			var rowPitch = width * bytesPerPixel;
			var depthPitch = rowPitch * height;

			// Build the destination region for the dirty rect that we'll draw to
			var texDesc = targetTexture.Description;
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

			// Draw to the target
			var context = targetTexture.Device.ImmediateContext;
			context.UpdateSubresource(targetTexture, 0, destinationRegion, sourceRegionPtr, rowPitch, depthPitch);

			// Only need to do composition + flush on primary texture
			if (type != PaintElementType.View) { return; }

			if (popupVisible)
			{
				context.CopySubresourceRegion(popupTexture, 0, null, targetTexture, 0, popupRect.X, popupRect.Y);
			}

			context.Flush();

			// Rendering is complete, clean up any obsolete textures
			var textures = obsoluteTextures;
			obsoluteTextures = new ConcurrentBag<D3D11.Texture2D>();
			foreach (var texture in textures) { texture.Dispose(); }
		}

		public override void OnPopupShow(bool show)
		{
			popupVisible = show;
		}

		public override void OnPopupSize(Rect rect)
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
	}
}
