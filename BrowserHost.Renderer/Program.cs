﻿using BrowserHost.Common;
using SharedMemory;
using System;
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

		private static CircularBuffer producer;

		private static Thread parentWatchThread;
		private static EventWaitHandle waitHandle;

		static void Main(string[] rawArgs)
		{
			Console.WriteLine("Render process running.");
			AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

			// Argument parsing
			var args = RenderProcessArguments.Deserialise(rawArgs[0]);
			cefAssemblyDir = args.CefAssemblyDir;
			dalamudAssemblyDir = args.DalamudAssemblyDir;

			Run(args.ParentPid);
		}

		// Main process logic. Seperated to ensure assembly resolution is configured.
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void Run(int parentPid)
		{
			waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostRendererKeepAlive{parentPid}");

			// Boot up a thread to make sure we shut down if parent dies
			parentWatchThread = new Thread(WatchParentStatus);
			parentWatchThread.Start(parentPid);

			// We don't specify size, consumer will create the initial buffer.
			producer = new CircularBuffer($"DalamudBrowserHostFrameBuffer{parentPid}");

#if DEBUG
			AppDomain.CurrentDomain.FirstChanceException += (obj, e) => Console.Error.WriteLine(e.Exception.ToString());
#endif

			DxHandler.Initialise();
			CefHandler.Initialise(cefAssemblyDir);

			var ipcBuffer = new RpcBuffer($"BrowserHostRendererIpcChannel{parentPid}", IpcCallback);

			BuildInlay();

			Console.WriteLine("Waiting...");

			waitHandle.WaitOne();
			waitHandle.Dispose();

			Console.WriteLine("Render process shutting down.");

			ipcBuffer.Dispose();

			DxHandler.Shutdown();
			CefHandler.Shutdown();

			producer.Dispose();

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
			IpcRequest request;
			using (MemoryStream stream = new MemoryStream(requestData))
			{
				request = (IpcRequest)formatter.Deserialize(stream);
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

		private static object HandleIpcRequest(IpcRequest request)
		{
			switch (request)
			{
				case NewInlayRequest newInlayRequest:
					Console.WriteLine($"GOT W{newInlayRequest.Width} H{newInlayRequest.Height}");
					return new NewInlayResponse() { TextureHandle = (IntPtr)42069 };
				default:
					throw new Exception("Unknown IPC request type received.");
			}
		}

		private static void BuildInlay()
		{
			var inlay = new Inlay(
				"https://www.testufo.com/framerates#count=3&background=stars&pps=960",
				new Size(800, 800)
			);
			inlay.Initialise();

			// Build the texture & pass over to plugin process
			producer.Write(new[] { inlay.SharedTextureHandle });
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
