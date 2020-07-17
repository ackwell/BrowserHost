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

            PluginLog.Log("Configuring render process.");

            var rendererPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "BrowserRenderer.exe");

            renderProcess = new Process();
            renderProcess.StartInfo = new ProcessStartInfo()
            {
                FileName = rendererPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            renderProcess.OutputDataReceived += (object sender, DataReceivedEventArgs args) => PluginLog.Log(args.Data);
            renderProcess.ErrorDataReceived += (object sender, DataReceivedEventArgs args) => PluginLog.LogError(args.Data);

            PluginLog.Log("Booting render process.");

            renderProcess.Start();
            renderProcess.BeginOutputReadLine();
            renderProcess.BeginErrorReadLine();

            PluginLog.Log("Loaded.");
        }

        public void Dispose()
        {
            renderProcess.Kill();
            renderProcess.Dispose();

            pluginInterface.Dispose();
        }
    }
}
