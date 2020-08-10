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
		public string BufferName { get { return bitmapBuffer.Name; } }

		private CircularBuffer bitmapBuffer;
		private System.Drawing.Size size;

		public BitmapBufferRenderHandler(System.Drawing.Size size)
		{
			this.size = size;

			// TODO: Sane size. Do we want to realloc buffers every resize or stone one or...?
			var name = $"BrowserHostBitmapBuffer{Guid.NewGuid()}";
			bitmapBuffer = new CircularBuffer(name, 5, 1024 * 1024 * 10 /* 10M */);
		}

		public override void Dispose()
		{
			bitmapBuffer.Dispose();
		}

		public override void Resize(System.Drawing.Size size)
		{
			// TODO
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
			// TODO: Seperate buffers for frame info and buffer data?
			var length = bytesPerPixel * width * height;
			var frame = new BitmapFrame()
			{
				Width = width,
				Height = height,
				Length = length,
			};

			bitmapBuffer.Write(ref frame);
			bitmapBuffer.Write(buffer, length);
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
