using CefSharp;
using CefSharp.OffScreen;
using System.IO;

namespace BrowserHost.Renderer
{
	static class CefHandler
	{
		public static void Initialise(string cefAssemblyPath, string cefCacheDir)
		{
			var settings = new CefSettings()
			{
				BrowserSubprocessPath = Path.Combine(cefAssemblyPath, "CefSharp.BrowserSubprocess.exe"),
				CachePath = cefCacheDir,
			};
			settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";
			settings.EnableAudio();
			settings.SetOffScreenRenderingBestPerformanceArgs();

			Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
		}

		public static void Shutdown()
		{
			Cef.Shutdown();
		}
	}
}
