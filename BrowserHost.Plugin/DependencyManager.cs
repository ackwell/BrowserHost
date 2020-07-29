using ImGuiNET;
using System.IO;
using System.Linq;

namespace BrowserHost.Plugin
{
	struct Dependency
	{
		public string Url;
		public string Version;
		public string Directory;
	}

	class DependencyManager
	{
		private static Dependency[] dependencies = new[]
		{
			new Dependency()
			{
				Url = "https://github.com/ackwell/BrowserHost/releases/download/cef-binaries/cefsharp-{VERSION}.zip",
				Version = "81.3.10+gb223419+chromium-81.0.4044.138",
				Directory = "cef"
			}
		};

		public bool Valid { get => missingDependencies?.Length == 0; }

		private string pluginDir;
		private Dependency[] missingDependencies;

		public DependencyManager(string pluginDir)
		{
			this.pluginDir = pluginDir;
		}

		public void Initialise()
		{
			missingDependencies = dependencies.Where(DependencyMissing).ToArray();
		}

		private bool DependencyMissing(Dependency dependency)
		{
			var versionFilePath = Path.Combine(pluginDir, dependency.Directory, "VERSION");

			string versionContents;
			try { versionContents = File.ReadAllText(versionFilePath); }
			catch { return true; }

			return !versionContents.Contains(dependency.Version);
		}

		public void Render()
		{
			ImGui.Begin("BrowserHost dependencies");

			ImGui.Text($"Valid: {Valid}");

			ImGui.End();
		}
	}
}
