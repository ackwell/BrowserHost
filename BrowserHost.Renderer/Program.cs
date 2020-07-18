using CefSharp;
using CefSharp.OffScreen;
using SharedMemory;
using System;
using System.Diagnostics;
using System.Drawing.Imaging;
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
            producer = new CircularBuffer("DalamudBrowserHostFrameBuffer");

            AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

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
            });
        }

        private static Assembly CustomAssemblyResolver(object sender, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("CefSharp")) { return null; }

            var assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";
            var assemblyPath = Path.Combine(cefAssemblyPath, assemblyName);

            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine($"Could not find assembly `{assemblyName}` at search path `{assemblyPath}`");
                return null;
            }

            return Assembly.LoadFile(assemblyPath);
        }
    }
}
