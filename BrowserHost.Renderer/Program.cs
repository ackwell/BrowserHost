using BrowserHost.Common;
using SharedMemory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace BrowserHost.Renderer
{
	class Program
	{
		private static string cefAssemblyDir;
		private static string dalamudAssemblyDir;

		private static Thread parentWatchThread;
		private static EventWaitHandle waitHandle;

		private static RpcBuffer ipcBuffer;

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

			ipcBuffer = new RpcBuffer(args.IpcChannelName, IpcCallback);

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

		private static byte[] IpcCallback(ulong messageId, byte[] requestData)
		{
			var formatter = new BinaryFormatter();
			DownstreamIpcRequest request;
			using (MemoryStream stream = new MemoryStream(requestData))
			{
				request = (DownstreamIpcRequest)formatter.Deserialize(stream);
			}

			var response = HandleIpcRequest(request);

			byte[] rawResponse;
			using (MemoryStream stream = new MemoryStream())
			{
				formatter.Serialize(stream, response);
				rawResponse = stream.ToArray();
			}
			return rawResponse;
		}

		private static object HandleIpcRequest(DownstreamIpcRequest request)
		{
			switch (request)
			{
				case NewInlayRequest newInlayRequest:
				{
					var inlay = new Inlay(newInlayRequest.Url, new Size(newInlayRequest.Width, newInlayRequest.Height));
					inlay.Initialise();
					inlays.Add(newInlayRequest.Guid, inlay);
					inlay.CursorChanged += (sender, cursor) =>
					{
						Console.WriteLine($"Sending: {cursor}");
						// TODO: Clean this up. Move ipc serde to common?
						var formatter = new BinaryFormatter();
						byte[] rawRequest;
						using (MemoryStream stream = new MemoryStream())
						{
							formatter.Serialize(stream, new SetCursorRequest()
							{
								Guid = newInlayRequest.Guid,
								Cursor = cursor
							});
							rawRequest = stream.ToArray();
						}
						ipcBuffer.RemoteRequest(rawRequest);
					};
					return new NewInlayResponse() { TextureHandle = inlay.SharedTextureHandle };
				}

				case MouseMoveRequest mouseMoveRequest:
				{
					var inlay = inlays[mouseMoveRequest.Guid];
					// TODO: also yikes lmao
					if (inlay == null) { return new MouseMoveResponse(); }
					// TODO -> vec2? seems unessecary.
					inlay.MouseMove(mouseMoveRequest.X, mouseMoveRequest.Y);
					return new MouseMoveResponse();
				}

				default:
					throw new Exception("Unknown IPC request type received.");
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
