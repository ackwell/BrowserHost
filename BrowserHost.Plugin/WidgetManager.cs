using BrowserHost.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BrowserHost.Plugin
{
	public static class WidgetManager
	{
		private static Dictionary<Guid, WeakReference<BrowserWidget>> widgets = new Dictionary<Guid, WeakReference<BrowserWidget>>();

		public static BrowserWidget CreateWidget(string url)
		{
			var widget = new BrowserWidget(url);
			widgets.Add(widget.Guid, new WeakReference<BrowserWidget>(widget));

			return widget;
		}

		internal static (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
		{
			// Notify all the inlays of the wndproc, respond with the first capturing response (if any)
			// TODO: Yeah this ain't great but realistically only one will capture at any one time for now. Revisit if shit breaks or something idfk.
			var responses = widgets.Select(pair =>
			{
				BrowserWidget widget;
				pair.Value.TryGetTarget(out widget);
				return widget != null
					? widget.WndProcMessage(msg, wParam, lParam)
					: (false, 0);
			});
			return responses.FirstOrDefault(pair => pair.Item1);
		}

		internal static object HandleIpcRequest(object sender, UpstreamIpcRequest request)
		{
			switch (request)
			{
				case SetCursorRequest setCursorRequest:
					{
						BrowserWidget widget = null;
						widgets[setCursorRequest.Guid]?.TryGetTarget(out widget);
						if (widget != null) { widget.SetCursor(setCursorRequest.Cursor); }
						return null;
					}

				default:
					throw new Exception($"Unknown IPC request type {request.GetType().Name} received.");
			}
		}
	}
}
