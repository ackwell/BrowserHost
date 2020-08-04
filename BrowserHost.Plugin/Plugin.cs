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
using System.Threading;

namespace BrowserHost.Plugin
{
	public class Plugin : IDalamudPlugin
	{
		public string Name => "Browser Host";

		private DalamudPluginInterface pluginInterface;
		private string pluginDir;
		private int pid;

		private DependencyManager dependencyManager;
		private Settings settings;

		private RenderProcess renderProcess;
		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();
		private Dictionary<Guid, WeakReference<BrowserWidget>> widgets = new Dictionary<Guid, WeakReference<BrowserWidget>>();

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			pid = Process.GetCurrentProcess().Id;

			// Hook up render hook
			pluginInterface.UiBuilder.OnBuildUi += Render;

			dependencyManager = new DependencyManager(pluginDir);
			dependencyManager.DependenciesReady += (sender, args) => StartRendering();
			dependencyManager.Initialise();

			// Set up a custom resolver for ourselves so other plugins can reference us via the bridge
			AppDomain.CurrentDomain.AssemblyResolve += BrowserHostAssemblyResolver;

			// Open the ready handle to unblock any plugins that loaded early.
			var readyWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset, $"BrowserHostBridgeReady{pid}");
			readyWaitHandle.Set();
			readyWaitHandle.Dispose();
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
			var responses = widgets.Select(pair =>
			{
				BrowserWidget widget;
				pair.Value.TryGetTarget(out widget);
				return widget != null
					? widget.WndProcMessage(msg, wParam, lParam)
					: (false, 0);
			});
			return responses.FirstOrDefault(pair => pair.Item1);
		}

		private void OnInlayAdded(object sender, InlayConfiguration config)
		{
			var widget = new BrowserWidget(renderProcess, config.Url);
			widgets.Add(widget.Guid, new WeakReference<BrowserWidget>(widget));

			var inlay = new Inlay(config, widget);
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
					BrowserWidget widget = null;
					widgets[setCursorRequest.Guid]?.TryGetTarget(out widget);
					if (widget != null) { widget.SetCursor(setCursorRequest.Cursor); }
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

		private Assembly BrowserHostAssemblyResolver(object sender, ResolveEventArgs args)
		{
			var assemblyName = args.Name.Split(new[] { ',' }, 2)[0] + ".dll";
			return assemblyName.StartsWith("BrowserHost")
				? Assembly.LoadFrom(Path.Combine(pluginDir, assemblyName))
				: null;
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
