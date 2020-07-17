using Dalamud.Plugin;
using System;
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
            renderProcess.OutputDataReceived += (sender, args) => PluginLog.Log($"[Render]: {args.Data}");
            renderProcess.ErrorDataReceived += (sender, args) => PluginLog.LogError($"[Render]: {args.Data}");

            PluginLog.Log("Booting render process.");

            renderProcess.Start();
            renderProcess.BeginOutputReadLine();
            renderProcess.BeginErrorReadLine();

            PluginLog.Log("Loaded.");
        }

        public void Dispose()
        {
            try { renderProcess.Kill(); }
            catch (InvalidOperationException) { }
            renderProcess.Dispose();

            pluginInterface.Dispose();
        }
    }
}
