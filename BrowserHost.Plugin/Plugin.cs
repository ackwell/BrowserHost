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
		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			// Spin up DX handling from the plugin interface
			DxHandler.Initialise(pluginInterface);

			// Hook up the plugin interface and our UI rendering logic
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += Render;

			// Boot the render process. This has to be done before initialising settings to prevent a
			// race conditionson inlays recieving a null reference.
			var pid = Process.GetCurrentProcess().Id;
			renderProcess = new RenderProcess(pid);
			renderProcess.Recieve += HandleIpcRequest;
			renderProcess.Start();

			// Prep settings
			settings = new Settings(pluginInterface);
			settings.InlayAdded += OnInlayAdded;
			settings.InlayNavigated += OnInlayNavigated;
			settings.InlayRemoved += OnInlayRemoved;
			settings.Initialise();
		}

		private void OnInlayAdded(object sender, InlayConfiguration config)
		{
			// TODO: This is duped with init thread logic above
			// ... Maybe I could just make settings fire inlay added for stuff on init? Hm.
			var inlay = new Inlay(renderProcess, config);
			inlays.Add(inlay.Config.Guid, inlay);
		}

		private void OnInlayNavigated(object sender, InlayConfiguration config)
		{
			var inlay = inlays[config.Guid];
			inlay.Navigate(config.Url);
		}

		private void OnInlayRemoved(object sender, InlayConfiguration config)
		{
			var inlay = inlays[config.Guid];
			inlays.Remove(config.Guid);
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
			settings?.Render();

			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

			foreach (var inlay in inlays.Values) { inlay.Render(); }

			ImGui.PopStyleVar();
		}

		public void Dispose()
		{
			foreach (var inlay in inlays.Values) { inlay.Dispose(); }
			inlays.Clear();

			renderProcess.Dispose();

			settings.Dispose();

			pluginInterface.Dispose();

			DxHandler.Shutdown();
		}
	}
}
