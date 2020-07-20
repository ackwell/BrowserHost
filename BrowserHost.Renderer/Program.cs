using CefSharp;
using CefSharp.OffScreen;
using SharedMemory;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
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
		private static string cefAssemblyPath => Path.Combine(
			AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
			Environment.Is64BitProcess ? "x64" : "x86");

		private static ChromiumWebBrowser browser;

		private static CircularBuffer producer;

		private static Thread parentWatchThread;
		private static EventWaitHandle waitHandle;

		static void Main(string[] args)
		{
			Console.WriteLine("Render process running.");

			var parentPid = int.Parse(args[0]);

			waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostRendererKeepAlive{parentPid}");

			// Boot up a thread to make sure we shut down if parent dies
			parentWatchThread = new Thread(WatchParentStatus);
			parentWatchThread.Start(int.Parse(args[0]));

			// We don't specify size, consumer will create the initial buffer.
			producer = new CircularBuffer($"DalamudBrowserHostFrameBuffer{parentPid}");

			AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

#if DEBUG
			AppDomain.CurrentDomain.FirstChanceException += (obj, e) => Console.Error.WriteLine(e.Exception.ToString());
#endif

			InitialiseCef();

			Console.WriteLine("Waiting...");

			waitHandle.WaitOne();
			waitHandle.Dispose();

			Console.WriteLine("Render process shutting down.");

			DisposeCef();

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

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void InitialiseCef()
		{
			var settings = new CefSettings()
			{
				BrowserSubprocessPath = Path.Combine(cefAssemblyPath, "CefSharp.BrowserSubprocess.exe"),
			};
			settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";

			Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);

			var width = 800;
			var height = 800;

			// Build the texture
			// TODO: Need to ensure that our render device is on the same adapter as the primary game process.
			// TODO: Store ref to the device. Creating a new one every time we gen tex is abysmal. Only okay atm because one tex.
			var device = new D3D11.Device(D3D.DriverType.Hardware, D3D11.DeviceCreationFlags.BgraSupport | D3D11.DeviceCreationFlags.Debug);

			var renderHandler = new TextureRenderHandler(device, width, height);

			// Pass texture over to plugin proc
			Console.WriteLine($"Sending resource pointer {renderHandler.SharedTextureHandle}");
			producer.Write(new[] { renderHandler.SharedTextureHandle });

			// Browser config
			var windowInfo = new WindowInfo()
			{
				Width = width,
				Height = height,
			};
			windowInfo.SetAsWindowless(IntPtr.Zero);

			var browserSettings = new BrowserSettings()
			{
				WindowlessFrameRate = 60,
			};

			// Boot up the browser itself
			// TODO: Proper resize handling, this is all hardcoded size shit
			browser = new ChromiumWebBrowser("https://www.testufo.com/framerates#count=3&background=stars&pps=960", automaticallyCreateBrowser: false);
			browser.RenderHandler = renderHandler;
			// WindowInfo gets ignored sometimes, be super sure:
			browser.BrowserInitialized += (sender, args) => { browser.Size = new Size(width, height); };
			browser.CreateBrowser(windowInfo, browserSettings);

			windowInfo.Dispose();
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void DisposeCef()
		{
			Cef.Shutdown();
		}

		private static Assembly CustomAssemblyResolver(object sender, ResolveEventArgs args)
		{
			var assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";

			string assemblyPath = null;
			if (assemblyName.StartsWith("CefSharp"))
			{
				assemblyPath = Path.Combine(cefAssemblyPath, assemblyName);
			}
			else if (assemblyName.StartsWith("SharpDX"))
			{
				// TODO: Obtain this path sanely, probably pass down dalamud dir from parent proc
				assemblyPath = Path.Combine(
					AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
					"..", "..", "addon", "Hooks",
					assemblyName);
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
