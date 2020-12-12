using BrowserHost.Common;
using CefSharp;
using CefSharp.Structs;
using SharedMemory;
using System;
using System.Collections.Concurrent;

namespace BrowserHost.Renderer.RenderHandlers
{
	class BitmapBufferRenderHandler : BaseRenderHandler
	{
		// CEF buffers are 32-bit BGRA
		private const byte bytesPerPixel = 4;

		public string BitmapBufferName { get { return bitmapBuffer.Name; } }
		public string FrameInfoBufferName { get { return frameInfoBuffer.Name; } }

		private BufferReadWrite bitmapBuffer;
		private CircularBuffer frameInfoBuffer;
		private System.Drawing.Size size;

		private ConcurrentBag<BufferReadWrite> obsoleteBuffers = new ConcurrentBag<BufferReadWrite>();

		public BitmapBufferRenderHandler(System.Drawing.Size size)
		{
			this.size = size;

			bitmapBuffer = BuildBitmapBuffer(size);

			// TODO: Sane size. Do we want to realloc buffers every resize or stone one or...?
			var frameInfoBuffername = $"BrowserHostFrameInfoBuffer{Guid.NewGuid()}";
			frameInfoBuffer = new CircularBuffer(frameInfoBuffername, 5, 1024 /* 1K */);
		}

		public override void Dispose()
		{
			frameInfoBuffer.Dispose();
		}

		public override void Resize(System.Drawing.Size newSize)
		{
			// If new size is same as current, noop
			if (
				newSize.Width == size.Width &&
				newSize.Height == size.Height
			) {
				return;
			}

			var oldBuffer = bitmapBuffer;

			// Build a new buffer & set up on instance
			var newBuffer = BuildBitmapBuffer(newSize);
			size = newSize;
			bitmapBuffer = newBuffer;

			// Mark the old buffer for disposal
			if (oldBuffer != null) { obsoleteBuffers.Add(oldBuffer); }
		}

		protected override byte GetAlphaAt(int x, int y)
		{
			// TODO
			return 255;
		}

		public override Rect GetViewRect()
		{
			return new Rect(0, 0, size.Width, size.Height);
		}

		public override void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			// TODO: Popups
			if (type != PaintElementType.View) { return; }

			// If the paint size does not match our buffer size, we're likely resizing and paint hasn't caught up. Noop.
			if (width != size.Width && height != size.Height) { return; }

			// TODO: Only write dirty rect
			var length = bytesPerPixel * width * height;
			var frame = new BitmapFrame()
			{
				Width = width,
				Height = height,
				Length = length,
			};

			// Not using read/write locks because I'm a cowboy (and there seems to be a race cond in the locking mechanism)
			bitmapBuffer.Write(buffer, length);
			frameInfoBuffer.Write(ref frame);

			// Render is complete, clean up obsolete buffers
			var obsoleteBuffers = this.obsoleteBuffers;
			this.obsoleteBuffers = new ConcurrentBag<BufferReadWrite>();
			foreach (var toDispose in obsoleteBuffers) { toDispose.Dispose(); }
		}

		public override void OnPopupShow(bool show)
		{
			// TODO
		}

		public override void OnPopupSize(Rect rect)
		{
			// TODO
		}

		private BufferReadWrite BuildBitmapBuffer(System.Drawing.Size size)
		{
			var bitmapBufferName = $"BrowserHostBitmapBuffer{Guid.NewGuid()}";
			return new BufferReadWrite(bitmapBufferName, size.Width * size.Height * bytesPerPixel);
		}
	}
}
