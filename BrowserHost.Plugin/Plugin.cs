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

		private Settings settings;

		private RenderProcess renderProcess;
		private Thread inlayInitThread;
		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			// Spin up DX handling from the plugin interface
			DxHandler.Initialise(pluginInterface);

			// Hook up the plugin interface and our UI rendering logic
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += Render;

			// Prep settings
			// TODO: This may be worth doing in the init thread, it may be IO blocked on config down the road.
			settings = new Settings(pluginInterface);
			settings.InlayAdded += OnInlayAdded;
			settings.InlayRemoved += OnInlayRemoved;

			// Boot the render process
			var pid = Process.GetCurrentProcess().Id;
			renderProcess = new RenderProcess(pid);
			renderProcess.Recieve += HandleIpcRequest;
			renderProcess.Start();

			// Init inlays in a seperate thread so we're not blocking the rest of dalamud
			inlayInitThread = new Thread(InitialiseInlays);
			inlayInitThread.Start();
		}

		private void InitialiseInlays()
		{
			var inlay = new Inlay(renderProcess, new InlayConfiguration()
			{
				Guid = Guid.Parse("84B1C17A-666F-4ECD-ACD0-7C8EA335A362"),
				Name = "Test UFO",
				Url = "https://www.testufo.com/framerates#count=3&background=stars&pps=960",
			});
			inlays.Add(inlay.Config.Guid, inlay);
		}

		private void OnInlayAdded(object sender, InlayConfiguration config)
		{
			// TODO: This is duped with init thread logic above
			// ... Maybe I could just make settings fire inlay added for stuff on init? Hm.
			var inlay = new Inlay(renderProcess, config);
			inlays.Add(inlay.Config.Guid, inlay);
		}

		private void OnInlayRemoved(object sender, Guid guid)
		{
			var inlay = inlays[guid];
			inlays.Remove(guid);
			inlay.Dispose();
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
			settings.Render();

			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

			foreach (var inlay in inlays.Values) { inlay.Render(); }

			ImGui.PopStyleVar();
		}

		public void Dispose()
		{
			inlayInitThread.Join();
			foreach (var inlay in inlays.Values) { inlay.Dispose(); }
			inlays.Clear();

			renderProcess.Dispose();

			settings.Dispose();

			pluginInterface.Dispose();

			DxHandler.Shutdown();
		}
	}
}
