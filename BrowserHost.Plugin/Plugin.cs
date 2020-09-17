using BrowserHost.Common;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace BrowserHost.Plugin
{
	public class Plugin : IDalamudPlugin
	{
		public string Name => "Browser Host";

		private DalamudPluginInterface pluginInterface;
		private string pluginDir;

		private DependencyManager dependencyManager;
		private Settings settings;

		private RenderProcess renderProcess;
		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		// Required for LivePluginLoader support
		private string Location = Assembly.GetExecutingAssembly().Location;
		private void SetLocation(string path) { Location = path; } 

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginDir = Path.GetDirectoryName(Location);

			// Hook up render hook
			pluginInterface.UiBuilder.OnBuildUi += Render;

			dependencyManager = new DependencyManager(pluginDir);
			dependencyManager.DependenciesReady += (sender, args) => StartRendering();
			dependencyManager.Initialise();
		}

		private void StartRendering()
		{
			// Spin up DX handling from the plugin interface
			DxHandler.Initialise(pluginInterface);

			// Spin up WndProc hook
			WndProcHandler.Initialise(DxHandler.WindowHandle);
			WndProcHandler.WndProcMessage += OnWndProc;

			// Boot the render process. This has to be done before initialising settings to prevent a
			// race conditionson inlays recieving a null reference.
			var pid = Process.GetCurrentProcess().Id;
			renderProcess = new RenderProcess(pid, pluginDir, dependencyManager);
			renderProcess.Recieve += HandleIpcRequest;
			renderProcess.Start();

			// Prep settings
			settings = new Settings(pluginInterface);
			settings.InlayAdded += OnInlayAdded;
			settings.InlayNavigated += OnInlayNavigated;
			settings.InlayDebugged += OnInlayDebugged;
			settings.InlayRemoved += OnInlayRemoved;
			settings.Initialise();
		}

		private (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
		{
			// Notify all the inlays of the wndproc, respond with the first capturing response (if any)
			// TODO: Yeah this ain't great but realistically only one will capture at any one time for now. Revisit if shit breaks or something idfk.
			var responses = inlays.Select(pair => pair.Value.WndProcMessage(msg, wParam, lParam));
			return responses.FirstOrDefault(pair => pair.Item1);
		}

		private void OnInlayAdded(object sender, InlayConfiguration config)
		{
			var inlay = new Inlay(renderProcess, config);
			inlays.Add(inlay.Config.Guid, inlay);
		}

		private void OnInlayNavigated(object sender, InlayConfiguration config)
		{
			var inlay = inlays[config.Guid];
			inlay.Navigate(config.Url);
		}

		private void OnInlayDebugged(object sender, InlayConfiguration config)
		{
			var inlay = inlays[config.Guid];
			inlay.Debug();
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
			dependencyManager?.Render();
			settings?.Render();

			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

			foreach (var inlay in inlays.Values) { inlay.Render(); }

			ImGui.PopStyleVar();
		}

		public void Dispose()
		{
			foreach (var inlay in inlays.Values) { inlay.Dispose(); }
			inlays.Clear();

			renderProcess?.Dispose();

			settings?.Dispose();

			WndProcHandler.Shutdown();
			DxHandler.Shutdown();

			pluginInterface.Dispose();

			dependencyManager.Dispose();
		}
	}
}
