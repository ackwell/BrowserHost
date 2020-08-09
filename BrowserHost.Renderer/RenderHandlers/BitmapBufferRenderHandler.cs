using CefSharp;
using CefSharp.Structs;
using System;

namespace BrowserHost.Renderer.RenderHandlers
{
	class BitmapBufferRenderHandler : BaseRenderHandler
	{
		public BitmapBufferRenderHandler()
		{
		}

		public override void Dispose()
		{
		}

		public override void Resize(System.Drawing.Size size)
		{
			// TODO
		}

		public override Rect GetViewRect()
		{
			// TODO
			return new Rect();
		}

		public override void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height)
		{
			// TODO
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
