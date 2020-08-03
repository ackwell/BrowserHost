using System;
using System.Diagnostics;
using System.Threading;

namespace BrowserHost.Bridge
{
	public class Bridge
	{
		public static void OnReady(Action callback)
		{
			// Spin up a reference to the wait handle the main plugin will use to signal its ready state
			var pid = Process.GetCurrentProcess().Id;
			var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostBridgeReady{pid}");

			// Do an inline check for the handle to see if it's open - if it is, we can skip threading for this.
			var ready = waitHandle.WaitOne(0);
			if (ready)
			{
				waitHandle.Dispose();
				callback();
				return;
			}

			// Boot up a new thread to block on waiting for the ready signal
			var thread = new Thread(() =>
			{
				// TODO: Drop this to like 5-10s, handle error if BH not avail
				waitHandle.WaitOne(Timeout.Infinite);
				waitHandle.Dispose();
				callback();
			});
			thread.Start();
		}
	}
}
