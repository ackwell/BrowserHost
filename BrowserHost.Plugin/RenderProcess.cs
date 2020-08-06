using BrowserHost.Common;
using Dalamud.Plugin;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BrowserHost.Plugin
{
	static class RenderProcess
	{
		public delegate object RecieveEventHandler(object sender, UpstreamIpcRequest request);
		public static event RecieveEventHandler Recieve;

		public static bool Running;
		private static Process process;
		private static IpcBuffer<UpstreamIpcRequest, DownstreamIpcRequest> ipc;

		private static string keepAliveHandleName;
		private static string ipcChannelName;

		public static void Initialise(int pid, string pluginDir, string cefAssemblyDir)
		{
			keepAliveHandleName = $"BrowserHostRendererKeepAlive{pid}";
			ipcChannelName = $"BrowserHostRendererIpcChannel{pid}";

			ipc = new IpcBuffer<UpstreamIpcRequest, DownstreamIpcRequest>(ipcChannelName, request => Recieve?.Invoke(null, request));

			var processArgs = new RenderProcessArguments()
			{
				ParentPid = pid,
				// TODO: This has to be kept in sync with reality. Probably a safe bet, but would be nice to use dalamud's lumina's DataPath.
				SqpackDataDir = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "sqpack"),
				DalamudAssemblyDir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
				CefAssemblyDir = cefAssemblyDir,
				DxgiAdapterLuid = DxHandler.AdapterLuid,
				KeepAliveHandleName = keepAliveHandleName,
				IpcChannelName = ipcChannelName,
			};

			process = new Process();
			process.StartInfo = new ProcessStartInfo()
			{
				FileName = Path.Combine(pluginDir, "BrowserHost.Renderer.exe"),
				Arguments = processArgs.Serialise().Replace("\"", "\"\"\""),
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			process.OutputDataReceived += (sender, args) => PluginLog.Log($"[Render]: {args.Data}");
			process.ErrorDataReceived += (sender, args) => PluginLog.LogError($"[Render]: {args.Data}");
		}

		public static void Start()
		{
			if (Running) { return; }

			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			Running = true;
		}

		public static void Send(DownstreamIpcRequest request) { Send<object>(request); }

		// TODO: Option to wrap this func in an async version?
		public static TResponse Send<TResponse>(DownstreamIpcRequest request)
		{
			return ipc.RemoteRequest<TResponse>(request);
		}

		public static void Stop()
		{
			if (!Running) { return; }
			Running = false;

			// Grab the handle the process is waiting on and open it up
			var handle = new EventWaitHandle(false, EventResetMode.ManualReset, keepAliveHandleName);
			handle.Set();
			handle.Dispose();

			// Give the process a sec to gracefully shut down, then kill it
			process.WaitForExit(1000);
			try { process.Kill(); }
			catch (InvalidOperationException) { }
		}

		public static void Shutdown()
		{
			Stop();

			process.Dispose();
			ipc.Dispose();
		}
	}
}
