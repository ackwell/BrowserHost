using BrowserHost.Common;
using Dalamud.Plugin;
using ImGuiNET;
using System;
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

		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += Render;

			DxHandler.Initialise(pluginInterface);

			var pid = Process.GetCurrentProcess().Id;

			PluginLog.Log("Configuring render process.");

			renderProcess = new RenderProcess(pid);
			renderProcess.Recieve += HandleIpcRequest;
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
				Size = new Vector2(800, 800),
			};
			// TODO: This is essentially a blocking call on IPC to the render process.
			//       When handling >1, look into something a-la promise.all for this.
			inlay.Initialise();
			inlays.Add(inlay.Guid, inlay);
		}

		private object HandleIpcRequest(object sender, UpstreamIpcRequest request)
		{
			switch (request)
			{
				case SetCursorRequest setCursorRequest:
				{
					var inlay = inlays[setCursorRequest.Guid];
					inlay.SetCursor(setCursorRequest.Cursor);
					return null;
				}

				default:
					throw new Exception($"Unknown IPC request type {request.GetType().Name} received.");
			}
		}

		private void Render()
		{
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

			foreach (Inlay inlay in inlays.Values)
			{
				inlay.Render();
			}

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
