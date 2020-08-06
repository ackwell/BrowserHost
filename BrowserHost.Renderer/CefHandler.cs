using CefSharp;
using CefSharp.OffScreen;
using System.IO;

namespace BrowserHost.Renderer
{
	static class CefHandler
	{
		public static void Initialise(string cefAssemblyPath, Lumina.Lumina lumina)
		{
			// Base CEF settings
			var settings = new CefSettings()
			{
				BrowserSubprocessPath = Path.Combine(cefAssemblyPath, "CefSharp.BrowserSubprocess.exe"),
			};
			settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
			settings.EnableAudio();
			settings.SetOffScreenRenderingBestPerformanceArgs();

			// Configure our xiv-specific scheme
			settings.RegisterScheme(new CefCustomScheme()
			{
				SchemeName = "sqpack",
				IsSecure = true,
				SchemeHandlerFactory = new SqpackSchemeHandlerFactory(lumina),
			});

			Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
		}

		public static void Shutdown()
		{
			Cef.Shutdown();
		}
	}
}
