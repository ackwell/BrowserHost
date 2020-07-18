using Dalamud.Plugin;
using ImGuiNET;
using SharedMemory;
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading;

namespace BrowserHost.Plugin
{
    public class Plugin : IDalamudPlugin
    {
        public string Name => "Browser Host";

        private DalamudPluginInterface pluginInterface;
        private Process renderProcess;

        private CircularBuffer consumer;

        private Thread thread;

        private byte[] frameBuffer;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
            pluginInterface.UiBuilder.OnBuildUi += DrawUi;

            consumer = new CircularBuffer("DalamudBrowserHostFrameBuffer", nodeCount: 5, nodeBufferSize: 1024 * 1024 * 10 /* 10M */);

            thread = new Thread(ThreadProc);
            thread.Start();

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
                Arguments = $"{Process.GetCurrentProcess().Id}",
            };
            renderProcess.OutputDataReceived += (sender, args) => PluginLog.Log($"[Render]: {args.Data}");
            renderProcess.ErrorDataReceived += (sender, args) => PluginLog.LogError($"[Render]: {args.Data}");

            PluginLog.Log("Booting render process.");

            renderProcess.Start();
            renderProcess.BeginOutputReadLine();
            renderProcess.BeginErrorReadLine();

            PluginLog.Log("Loaded.");
        }

        private void ThreadProc()
        {
            // TODO: Struct this or something
            // First data value will be the size of incoming bitmap
            var  data = new int[1];
            consumer.Read(data, timeout: Timeout.Infinite);
            var size = data[0];

            // Second value is the full bitmap, of the previously recorded size
            var buffer = new byte[size];
            consumer.Read(buffer, timeout: Timeout.Infinite);

            PluginLog.Log($"Read bitmap buffer of size {size}");

            frameBuffer = buffer;
        }

        private void DrawUi()
        {
            if (ImGui.Begin("BrowserHost"))
            {
                var ready = frameBuffer != null;
                ImGui.Text($"ready: {ready}");

                if (ready)
                {
                    // THIS IS WHOLLY RELIANT ON A FIX TO IMGUISCENE. IT WILL _NOT_ WORK ON REGULAR BUILDS.
                    // (need to fix `MemoryStream` constructor call in `RawDX11Scene.LoadImage(byte[])`)
                    // TODO: NUKE.
                    var tex = pluginInterface.UiBuilder.LoadImage(frameBuffer);
                    ImGui.Image(tex.ImGuiHandle, new Vector2(tex.Width, tex.Height));
                }
            }
            ImGui.End();
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

            thread.Join();

            consumer.Dispose();

            pluginInterface.Dispose();
        }
    }
}
