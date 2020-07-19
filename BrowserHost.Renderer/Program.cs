using CefSharp;
using CefSharp.OffScreen;
using SharedMemory;
using SharpDX;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpDX.Direct3D11;

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
			producer = new CircularBuffer("DalamudBrowserHostFrameBuffer");

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

			Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);

			browser = new ChromiumWebBrowser("https://www.google.com/");
			browser.LoadingStateChanged += BrowserLoadingStateChanged;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static void DisposeCef()
		{
			Cef.Shutdown();
		}

		private static void BrowserLoadingStateChanged(object sender, LoadingStateChangedEventArgs args)
		{
			Console.WriteLine($"State change: {args.IsLoading}");

			if (args.IsLoading) { return; }

			//TODO: Use a proper render target thing. custom format on wire to only pass dirty?
			browser.ScreenshotAsync().ContinueWith(task =>
			{
				var bm = task.Result;

				byte[] output;
				using (var stream = new MemoryStream())
				{
					bm.Save(stream, ImageFormat.Bmp); // memorybmp?
					output = stream.ToArray();
				}

				Console.WriteLine($"Writing with size {output.Length}");
				producer.Write(new[] { output.Length });
				producer.Write(output);

				var resPtr = TestBuildTexture(bm);
				producer.Write(new[] { resPtr });
			});
		}

		private static IntPtr TestBuildTexture(Bitmap bitmap)
		{
			var bmRect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			var data = bitmap.LockBits(bmRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

			var texDesc = new D3D11.Texture2DDescription()
			{
				Width = bitmap.Width,
				Height = bitmap.Height,
				MipLevels = 1,
				ArraySize = 1,
				Format = DXGI.Format.B8G8R8A8_UNorm,
				SampleDescription = new DXGI.SampleDescription(1, 0),
				//Usage = D3D11.ResourceUsage.Immutable, // Dynamic?
				Usage = D3D11.ResourceUsage.Default, //?
				BindFlags = D3D11.BindFlags.ShaderResource, // Might need render target?
				CpuAccessFlags = D3D11.CpuAccessFlags.None, // Write?
				OptionFlags = D3D11.ResourceOptionFlags.Shared, //keyedmutex?
			};

			//IntPtr resPtr;
			// ?? would like to know what this actually is returning. hopefully the right device.
			// TODO: Store ref to the device. Creating a new one every time we gen tex is abysmal. Only okay atm because one tex.
			//using (var device = new D3D11.Device(D3D.DriverType.Hardware, D3D11.DeviceCreationFlags.BgraSupport))
			//using (var texture = new D3D11.Texture2D(device, texDesc, new DataRectangle(data.Scan0, data.Stride)))
			//using (var resource = texture.QueryInterface<DXGI.Resource>())
			//{
			//	resPtr = resource.SharedHandle;
			//}

			var factory = new DXGI.Factory1();
			//var adapters = factory.Adapters1;
			//foreach (var adapter in adapters)
			//{
			//	Console.WriteLine($"ADPT: {adapter.Description1.Description}");
			//}
			var adapter = factory.GetAdapter1(0);
			Console.WriteLine($"Using adapter {adapter.Description.Description}");
			var device = new D3D11.Device(adapter, D3D11.DeviceCreationFlags.BgraSupport);

			//var device = new D3D11.Device(D3D.DriverType.Hardware, D3D11.DeviceCreationFlags.BgraSupport);
			var texture = new D3D11.Texture2D(device, texDesc, new DataRectangle(data.Scan0, data.Stride));
			var resource = texture.QueryInterface<DXGI.Resource>();
			var resPtr = resource.SharedHandle;

			bitmap.UnlockBits(data);

			// Test more shit
			//var thing = device.OpenSharedResource<Texture2D>(resPtr);
			//Console.WriteLine($"SR: {thing}");

			Console.WriteLine($"RESPTR: {resPtr}");
			return resPtr;
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
