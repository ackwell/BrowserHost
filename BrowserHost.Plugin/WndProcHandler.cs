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

		public static void Initialise(IntPtr hWnd)
		{
			WndProcHandler.hWnd = hWnd;

			wndProcDelegate = WndProcDetour;
			var detourPtr = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
			oldWndProcPtr = SetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC, detourPtr);
		}

		public static void Shutdown()
		{
			if (oldWndProcPtr != IntPtr.Zero)
			{
				SetWindowLongPtr(hWnd, WindowLongType.GWL_WNDPROC, oldWndProcPtr);
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


			return CallWindowProc(oldWndProcPtr, hWnd, msg, wParam, lParam);
		}

		// Win API stuff

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
		private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, WindowLongType nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
		private static extern long CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, ulong wParam, long lParam);
	}

	// Enums are not comprehensive for the sake of omitting stuff I won't use.
	enum WindowLongType : int
	{
		GWL_WNDPROC = -4,
	}

	enum WindowsMessage
	{
		WM_KEYDOWN = 0x0100,
		WM_KEYUP = 0x0101,
		WM_CHAR = 0x0102,
		WM_SYSKEYDOWN = 0x0104,
		WM_SYSKEYUP = 0x0105,
		WM_SYSCHAR = 0x0106,
	}
}
