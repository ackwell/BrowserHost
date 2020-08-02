using System;
using System.Diagnostics;
using System.Threading;

namespace BrowserHost.Bridge
{
	public class Bridge
	{
		public static void OnReady(Action callback)
		{
			// Boot up a new thread to block on waiting for the ready signal
			// TODO: Worth booting the wait handle before thread and doing a check?
			var thread = new Thread(() =>
			{
				var pid = Process.GetCurrentProcess().Id;
				var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostBridgeReady{pid}");
				// TODO: Drop this to like 5-10s, handle error if BH not avail
				waitHandle.WaitOne(Timeout.Infinite);
				callback();
			});
			thread.Start();
		}
	}
}
