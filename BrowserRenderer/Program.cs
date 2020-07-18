using CefSharp;
using CefSharp.OffScreen;
using SharedMemory;
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BrowserRenderer
{
    class Program
    {
        private static string CefAssemblyPath => Path.Combine(
            AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
            Environment.Is64BitProcess ? "x64" : "x86");

        private static ChromiumWebBrowser browser;

        // Maybe circular buffer if we start moving dirty states around?
        private static CircularBuffer producer;

        static void Main(string[] args)
        {
            Console.WriteLine("Render process running.");

            // We don't specify size, consumer will create the initial buffer.
            producer = new CircularBuffer("DalamudBrowserHostFrameBuffer");

            AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

            InitialiseCef();

            Console.WriteLine("Waiting...");

            var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, "DalamudBrowserHostTestHandle");
            waitHandle.WaitOne();
            waitHandle.Dispose();

            Console.WriteLine("Render process shutting down.");

            DisposeCef();

            producer.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitialiseCef()
        {
            var settings = new CefSettings()
            {
                BrowserSubprocessPath = Path.Combine(CefAssemblyPath, "CefSharp.BrowserSubprocess.exe"),
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

                bm.Save(Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "BEFORE.png"), ImageFormat.Png);

                Console.WriteLine($"Writing with size {output.Length}");
                producer.Write(new[] { output.Length });
                producer.Write(output);
            });
        }

        private static Assembly CustomAssemblyResolver(object sender, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("CefSharp")) { return null; }

            var assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";
            var assemblyPath = Path.Combine(CefAssemblyPath, assemblyName);

            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine($"Could not find assembly `{assemblyName}` at search path `{assemblyPath}`");
                return null;
            }

            return Assembly.LoadFile(assemblyPath);
        }
    }
}
