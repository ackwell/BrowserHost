using Dalamud.Plugin;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BrowserHost
{
    public class BrowserHost : IDalamudPlugin
    {
        public string Name => "Browser Host";

        private DalamudPluginInterface pluginInterface;
        private Process renderProcess;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            PluginLog.Log("Booting render process.");

            var rendererPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "BrowserRenderer.exe");

            renderProcess = new Process();
            renderProcess.StartInfo = new ProcessStartInfo()
            {
                FileName = rendererPath,
                UseShellExecute = false,
            };

            renderProcess.Start();

            PluginLog.Log("Loaded.");
        }

        public void Dispose()
        {
            renderProcess.Dispose();
            pluginInterface.Dispose();
        }
    }
}
