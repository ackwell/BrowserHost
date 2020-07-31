using System;
using System.Runtime.InteropServices;

namespace BrowserHost.Plugin
{
	class WndProcHandler
	{
		public delegate (bool, long) WndProcMessageDelegate(WindowsMessage msg, ulong wParam, long lParam);
		public static event WndProcMessageDelegate WndProcMessage;

		public delegate long WndProcDelegate(IntPtr hWnd, uint msg, ulong wParam, long lParam);
		private static WndProcDelegate wndProcDelegate;

		private static IntPtr hWnd;
		private static IntPtr oldWndProcPtr;
		private static IntPtr detourPtr;

		public static void Initialise(IntPtr hWnd)
		{
			WndProcHandler.hWnd = hWnd;

			wndProcDelegate = WndProcDetour;
			detourPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
			oldWndProcPtr = NativeMethods.SetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC, detourPtr);
		}

		public static void Shutdown()
		{
			// If the current pointer doesn't match our detour, something swapped the pointer out from under us -
			// likely the InterfaceManager doing its own cleanup. Don't reset in that case, we'll trust the cleanup
			// is accurate.
			var curWndProcPtr = NativeMethods.GetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC);
			if (oldWndProcPtr != IntPtr.Zero && curWndProcPtr == detourPtr)
			{
				NativeMethods.SetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC, oldWndProcPtr);
				oldWndProcPtr = IntPtr.Zero;
			}
		}

		private static long WndProcDetour(IntPtr hWnd, uint msg, ulong wParam, long lParam)
		{
			// Ignore things not targeting the current window handle
			if (hWnd == WndProcHandler.hWnd)
			{
				var resp = WndProcMessage?.Invoke((WindowsMessage)msg, wParam, lParam);

				// Item1 is a bool, where true == capture event. If false, we're falling through default handling.
				if (resp.HasValue && resp.Value.Item1)
				{
					return resp.Value.Item2;
				}
			}

			return NativeMethods.CallWindowProc(oldWndProcPtr, hWnd, msg, wParam, lParam);
		}
	}
}
