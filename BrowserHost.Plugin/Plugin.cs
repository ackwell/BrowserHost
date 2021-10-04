using BrowserHost.Common;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
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

		private static string COMMAND = "/bh";

		[PluginService] private static DalamudPluginInterface pluginInterface { get; set; }
		[PluginService] private static CommandManager commandManager { get; set; }
		[PluginService] private static ChatGui chat { get; set; }
		private string pluginDir;
		private string pluginConfigDir;

		private DependencyManager dependencyManager;
		private Settings settings;

		private RenderProcess renderProcess;
		private Dictionary<Guid, Inlay> inlays = new Dictionary<Guid, Inlay>();

		// Required for LivePluginLoader support
		public string AssemblyLocation { get; private set; } = Assembly.GetExecutingAssembly().Location;

		public Plugin()
		{
			//this.pluginInterface = pluginInterface;
			pluginDir = Path.GetDirectoryName(AssemblyLocation);
			pluginConfigDir = pluginInterface.GetPluginConfigDirectory();

			// Hook up render hook
			pluginInterface.UiBuilder.Draw += Render;

			dependencyManager = new DependencyManager(pluginDir, pluginConfigDir);
			dependencyManager.DependenciesReady += (sender, args) => DependenciesReady();
			dependencyManager.Initialise();
		}

		private void DependenciesReady()
		{
			// Spin up DX handling from the plugin interface
			DxHandler.Initialise(pluginInterface);

			// Spin up WndProc hook
			WndProcHandler.Initialise(DxHandler.WindowHandle);
			WndProcHandler.WndProcMessage += OnWndProc;

			// Boot the render process. This has to be done before initialising settings to prevent a
			// race conditionson inlays recieving a null reference.
			var pid = Process.GetCurrentProcess().Id;
			renderProcess = new RenderProcess(pid, pluginDir, pluginConfigDir, dependencyManager);
			renderProcess.Recieve += HandleIpcRequest;
			renderProcess.Start();

			// Prep settings
			settings = pluginInterface.Create<Settings>();
			settings.InlayAdded += OnInlayAdded;
			settings.InlayNavigated += OnInlayNavigated;
			settings.InlayDebugged += OnInlayDebugged;
			settings.InlayRemoved += OnInlayRemoved;
			settings.TransportChanged += OnTransportChanged;
			settings.Initialise();

			// Hook up the main BH command
			commandManager.AddHandler(COMMAND, new CommandInfo(HandleCommand)
			{
				HelpMessage = "Control BrowserHost from the chat line! Type '/bh config' or open the settings for more info.",
				ShowInHelp = true,
			});
		}

		private (bool, long) OnWndProc(WindowsMessage msg, ulong wParam, long lParam)
		{
			// Notify all the inlays of the wndproc, respond with the first capturing response (if any)
			// TODO: Yeah this ain't great but realistically only one will capture at any one time for now. Revisit if shit breaks or something idfk.
			var responses = inlays.Select(pair => pair.Value.WndProcMessage(msg, wParam, lParam));
			return responses.FirstOrDefault(pair => pair.Item1);
		}

		private void OnInlayAdded(object sender, InlayConfiguration inlayConfig)
		{
			var inlay = new Inlay(renderProcess, settings.Config, inlayConfig);
			inlays.Add(inlayConfig.Guid, inlay);
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

		private void OnTransportChanged(object sender, EventArgs unused)
		{
			// Transport has changed, need to rebuild all the inlay renderers
			foreach (var inlay in inlays.Values)
			{
				inlay.InvalidateTransport();
			}
		}

		private object HandleIpcRequest(object sender, UpstreamIpcRequest request)
		{
			switch (request)
			{
				case ReadyNotificationRequest readyNotificationRequest:
				{
					settings.SetAvailableTransports(readyNotificationRequest.availableTransports);
					settings.HydrateInlays();
					return null;
				}

				case SetCursorRequest setCursorRequest:
				{
					// TODO: Integrate ideas from Bridge re: SoC between widget and inlay
					var inlay = inlays.Values.Where(inlay => inlay.RenderGuid == setCursorRequest.Guid).FirstOrDefault();
					if (inlay == null) { return null; }
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

		private void HandleCommand(string command, string rawArgs)
		{
			// Docs complain about perf of multiple splits.
			// I'm not convinced this is a sufficiently perf-critical path to care.
			var args = rawArgs.Split(null as char[], 2, StringSplitOptions.RemoveEmptyEntries);

			if (args.Length == 0)
			{
				chat.PrintError(
					"No subcommand specified. Valid subcommands are: config,inlay.");
				return;
			}

			var subcommandArgs = args.Length > 1 ? args[1] : "";

			switch (args[0])
			{
				case "config":
					settings.HandleConfigCommand(subcommandArgs);
					break;
				case "inlay":
					settings.HandleInlayCommand(subcommandArgs);
					break;
				default:
					chat.PrintError(
						$"Unknown subcommand '{args[0]}'. Valid subcommands are: config,inlay.");
					break;
			}
		}

		public void Dispose()
		{
			foreach (var inlay in inlays.Values) { inlay.Dispose(); }
			inlays.Clear();

			renderProcess?.Dispose();

			settings?.Dispose();

			commandManager.RemoveHandler(COMMAND);

			WndProcHandler.Shutdown();
			DxHandler.Shutdown();

			pluginInterface.Dispose();

			dependencyManager.Dispose();
		}
	}
}
