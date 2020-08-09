using BrowserHost.Common;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using System;

namespace BrowserHost.Renderer.RenderHandlers
{
	abstract class BaseRenderHandler : IRenderHandler
	{
		public event EventHandler<Cursor> CursorChanged;

		public abstract void Dispose();

		public abstract void Resize(System.Drawing.Size size);

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

		public abstract Rect GetViewRect();

		public abstract void OnPaint(PaintElementType type, Rect dirtyRect, IntPtr buffer, int width, int height);

		public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, IntPtr sharedHandle)
		{
			// UNUSED
			// CEF has removed support for DX accelerated paint shared textures, pending re-implementation in
			// chromium's new compositor, Vis. Ref: https://bitbucket.org/chromiumembedded/cef/issues/2575/viz-implementation-for-osr
		}

		public abstract void OnPopupShow(bool show);

		public abstract void OnPopupSize(Rect rect);

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
