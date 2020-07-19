using Dalamud.Plugin;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace BrowserHost.Plugin
{
	class RenderProcess : IDisposable
	{
		private int key;
		private Process process;

		public RenderProcess(int key)
		{
			this.key = key;

			// Configure the subprocess
			var rendererPath = Path.Combine(
				Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
				"BrowserHost.Renderer.exe");

			process = new Process();
			process.StartInfo = new ProcessStartInfo()
			{
				FileName = rendererPath,
				Arguments = $"{key}",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			process.OutputDataReceived += (sender, args) => PluginLog.Log($"[Render]: {args.Data}");
			process.ErrorDataReceived += (sender, args) => PluginLog.LogError($"[Render]: {args.Data}");

			// Boot it up
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}

		public void Dispose()
		{
			// Grab the handle the process is waiting on and open it up
			var handle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostRendererKeepAlive{key}");
			handle.Set();
			handle.Dispose();

			// Give the process a sec to gracefully shut down, then kill it
			process.WaitForExit(1000);
			try { process.Kill(); }
			catch (InvalidOperationException) { }
			process.Dispose();
		}
	}
}
