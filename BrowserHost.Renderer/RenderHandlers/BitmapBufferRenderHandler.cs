using BrowserHost.Common;
using CefSharp;
using CefSharp.Structs;
using SharedMemory;
using System;

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

		public BitmapBufferRenderHandler(System.Drawing.Size size)
		{
			this.size = size;

			var bitmapBufferName = $"BrowserHostBitmapBuffer{Guid.NewGuid()}";
			bitmapBuffer = new BufferReadWrite(bitmapBufferName, size.Width * size.Height * bytesPerPixel);

			// TODO: Sane size. Do we want to realloc buffers every resize or stone one or...?
			var frameInfoBuffername = $"BrowserHostFrameInfoBuffer{Guid.NewGuid()}";
			frameInfoBuffer = new CircularBuffer(frameInfoBuffername, 5, 1024 /* 1K */);
		}

		public override void Dispose()
		{
			frameInfoBuffer.Dispose();
		}

		public override void Resize(System.Drawing.Size size)
		{
			// TODO
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
		}

		public override void OnPopupShow(bool show)
		{
			// TODO
		}

		public override void OnPopupSize(Rect rect)
		{
			// TODO
		}
	}
}
