using Dalamud.Plugin;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

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
            // TODO: If I go down the wait handle path, generate a guid for the handle name and pass over process args to sync.
            var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, "DalamudBrowserHostTestHandle");
            waitHandle.Set();
            renderProcess.WaitForExit(1000);
            try { renderProcess.Kill(); }
            catch (InvalidOperationException) { }
            renderProcess.Dispose();

            waitHandle.Dispose();

            pluginInterface.Dispose();
        }
    }
}
