using CefSharp;
using CefSharp.OffScreen;
using System;
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

        static void Main(string[] args)
        {
            Console.WriteLine("Render process running.");

            AppDomain.CurrentDomain.AssemblyResolve += CustomAssemblyResolver;

            Init();

            Console.WriteLine("Render process shutting down.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Init()
        {
            var settings = new CefSettings()
            {
                BrowserSubprocessPath = Path.Combine(CefAssemblyPath, "CefSharp.BrowserSubprocess.exe"),
            };

            Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
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
