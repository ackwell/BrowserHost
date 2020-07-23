using BrowserHost.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BrowserHost.Renderer
{
	class Program
	{
		private static string cefAssemblyDir;
		private static string dalamudAssemblyDir;

		private static Thread parentWatchThread;
		private static EventWaitHandle waitHandle;

		private static IpcBuffer<DownstreamIpcRequest, UpstreamIpcRequest> ipcBuffer;

		private static Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		static void Main(string[] rawArgs)
		{
			Console.WriteLine("Render process running.");
			AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

			Run(RenderProcessArguments.Deserialise(rawArgs[0]));
		}

		// Main process logic. Seperated to ensure assembly resolution is configured.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Run(RenderProcessArguments args)
		{
			cefAssemblyDir = args.CefAssemblyDir;
			dalamudAssemblyDir = args.DalamudAssemblyDir;

			waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, args.KeepAliveHandleName);

			// Boot up a thread to make sure we shut down if parent dies
			parentWatchThread = new Thread(WatchParentStatus);
			parentWatchThread.Start(args.ParentPid);

#if DEBUG
			AppDomain.CurrentDomain.FirstChanceException += (obj, e) => Console.Error.WriteLine(e.Exception.ToString());
#endif

			DxHandler.Initialise();
			CefHandler.Initialise(cefAssemblyDir);

			ipcBuffer = new IpcBuffer<DownstreamIpcRequest, UpstreamIpcRequest>(args.IpcChannelName, HandleIpcRequest);

			Console.WriteLine("Waiting...");

			waitHandle.WaitOne();
			waitHandle.Dispose();

			Console.WriteLine("Render process shutting down.");

			ipcBuffer.Dispose();

			DxHandler.Shutdown();
			CefHandler.Shutdown();

			parentWatchThread.Abort();
		}

		private static void WatchParentStatus(object pid)
		{
			Console.WriteLine($"Watching parent PID {pid}");
			var process = Process.GetProcessById((int)pid);
			process.WaitForExit();
			waitHandle.Set();

			var self = Process.GetCurrentProcess();
			self.WaitForExit(1000);
			try { self.Kill(); }
			catch (InvalidOperationException) { }
		}

		private static object HandleIpcRequest(DownstreamIpcRequest request)
		{
			switch (request)
			{
				case NewInlayRequest newInlayRequest:
				{
					// TODO: Move bulk of this into a method
					var inlay = new Inlay(newInlayRequest.Url, new Size(newInlayRequest.Width, newInlayRequest.Height));
					inlay.Initialise();
					inlays.Add(newInlayRequest.Guid, inlay);
					inlay.CursorChanged += (sender, cursor) =>
					{
						ipcBuffer.RemoteRequest<object>(new SetCursorRequest()
						{
							Guid = newInlayRequest.Guid,
							Cursor = cursor
						});
					};
					return new NewInlayResponse() { TextureHandle = inlay.SharedTextureHandle };
				}

				case MouseMoveRequest mouseMoveRequest:
				{
					var inlay = inlays[mouseMoveRequest.Guid];
					// TODO: also yikes lmao
					if (inlay == null) { return null; }
					// TODO -> vec2? seems unessecary.
					inlay.MouseMove(mouseMoveRequest.X, mouseMoveRequest.Y);
					return null;
				}

				default:
					throw new Exception($"Unknown IPC request type {request.GetType().Name} received.");
			}
		}

		private static Assembly CustomAssemblyResolver(object sender, ResolveEventArgs args)
		{
			var assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";

			string assemblyPath = null;
			if (assemblyName.StartsWith("CefSharp"))
			{
				assemblyPath = Path.Combine(cefAssemblyDir, assemblyName);
			}
			else if (assemblyName.StartsWith("SharpDX"))
			{
				assemblyPath = Path.Combine(dalamudAssemblyDir, assemblyName);
			}

			if (assemblyPath == null) { return null; }

			if (!File.Exists(assemblyPath))
			{
				Console.Error.WriteLine($"Could not find assembly `{assemblyName}` at search path `{assemblyPath}`");
				return null;
			}

			return Assembly.LoadFile(assemblyPath);
		}
	}
}
