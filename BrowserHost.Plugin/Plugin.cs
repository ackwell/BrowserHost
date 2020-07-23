using Dalamud.Plugin;
using ImGuiNET;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace BrowserHost.Plugin
{
	public class Plugin : IDalamudPlugin
	{
		public string Name => "Browser Host";

		private DalamudPluginInterface pluginInterface;

		private RenderProcess renderProcess;

		private Thread thread;

		private List<Inlay> inlays = new List<Inlay>();

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += Render;

			DxHandler.Initialise(pluginInterface);

			var pid = Process.GetCurrentProcess().Id;

			PluginLog.Log("Configuring render process.");

			renderProcess = new RenderProcess(pid);
			renderProcess.Start();

			thread = new Thread(InitialiseInlays);
			thread.Start();

			PluginLog.Log("Loaded.");
		}

		private void InitialiseInlays()
		{
			var inlay = new Inlay(renderProcess)
			{
				Name = "Test UFO",
				Url = "https://www.testufo.com/framerates#count=3&background=stars&pps=960",
				Width = 800,
				Height = 800,
			};
			// TODO: This is essentially a blocking call on IPC to the render process.
			//       When handling >1, look into something a-la promise.all for this.
			inlay.Initialise();
			inlays.Add(inlay);
		}

		private void Render()
		{
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

			inlays.ForEach(inlay => inlay.Render());

			ImGui.PopStyleVar();
		}

		public void Dispose()
		{
			renderProcess.Dispose();

			thread.Join();

			DxHandler.Shutdown();

			pluginInterface.Dispose();
		}
	}
}
