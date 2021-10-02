using BrowserHost.Common;
using Dalamud.Game.Gui;
using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		public event EventHandler<InlayConfiguration> InlayAdded;
		public event EventHandler<InlayConfiguration> InlayNavigated;
		public event EventHandler<InlayConfiguration> InlayDebugged;
		public event EventHandler<InlayConfiguration> InlayRemoved;
		public event EventHandler TransportChanged;

		public Configuration Config;

		[PluginService] private static DalamudPluginInterface pluginInterface { get; set; }
		[PluginService] private static ChatGui chat { get; set; }

#if DEBUG
		private bool open = true;
#else
		private bool open = false;
#endif

		private List<FrameTransportMode> availableTransports = new List<FrameTransportMode>();

		InlayConfiguration selectedInlay = null;
		private Timer saveDebounceTimer;

		public Settings()
		{
			pluginInterface.UiBuilder.OpenConfigUi += () => open = true;
		}

		public void Initialise()
		{
			Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		}

		public void Dispose() { }

		public void HandleConfigCommand(string rawArgs)
		{
			open = true;

			// TODO: Add further config handling if required here.
		}

		public void HandleInlayCommand(string rawArgs)
		{
			var args = rawArgs.Split(null as char[], 3, StringSplitOptions.RemoveEmptyEntries);

			// Ensure there's enough arguments
			if (args.Length < 3)
			{
				chat.PrintError(
					"Invalid inlay command. Supported syntax: '[inlayCommandName] [setting] [value]'");
				return;
			}

			// Find the matching inlay config
			var targetConfig = Config.Inlays.Find(inlay => GetInlayCommandName(inlay) == args[0]);
			if (targetConfig == null)
			{
				chat.PrintError(
					$"Unknown inlay '{args[0]}'.");
				return;
			}

			switch (args[1])
			{
				case "url":
					CommandSettingString(args[2], ref targetConfig.Url);
					// TODO: This call is duped with imgui handling. DRY.
					NavigateInlay(targetConfig);
					break;
				case "locked":
					CommandSettingBoolean(args[2], ref targetConfig.Locked);
					break;
				case "hidden":
					CommandSettingBoolean(args[2], ref targetConfig.Hidden);
					break;
				case "typethrough":
					CommandSettingBoolean(args[2], ref targetConfig.TypeThrough);
					break;
				case "clickthrough":
					CommandSettingBoolean(args[2], ref targetConfig.ClickThrough);
					break;
				default:
					chat.PrintError(
						$"Unknown setting '{args[1]}. Valid settings are: url,hidden,locked,clickthrough.");
					return;
			}

			SaveSettings();
		}

		private void CommandSettingString(string value, ref string target)
		{
			target = value;
		}

		private void CommandSettingBoolean(string value, ref bool target)
		{
			switch (value)
			{
				case "on":
					target = true;
					break;
				case "off":
					target = false;
					break;
				case "toggle":
					target = !target;
					break;
				default:
					chat.PrintError(
						$"Unknown boolean value '{value}. Valid values are: on,off,toggle.");
					break;
			}
		}

		public void SetAvailableTransports(FrameTransportMode transports)
		{
			// Decode bit flags to array for easier ui crap
			availableTransports = Enum.GetValues(typeof(FrameTransportMode))
				.Cast<FrameTransportMode>()
				.Where(transport => transport != FrameTransportMode.None && transports.HasFlag(transport))
				.ToList();

			// If the configured transport isn't available, pick the first so we don't end up in a weird spot.
			// NOTE: Might be nice to avoid saving this to disc - a one-off failure may cause a save of full fallback mode.
			if (availableTransports.Count > 0 && !availableTransports.Contains(Config.FrameTransportMode))
			{
				SetActiveTransport(availableTransports[0]);
			}
		}

		public void HydrateInlays()
		{
			// Hydrate any inlays in the config
			foreach (var inlayConfig in Config.Inlays)
			{
				InlayAdded?.Invoke(this, inlayConfig);
			}
		}

		private InlayConfiguration AddNewInlay()
		{
			var inlayConfig = new InlayConfiguration()
			{
				Guid = Guid.NewGuid(),
				Name = "New inlay",
				Url = "about:blank",
			};
			Config.Inlays.Add(inlayConfig);
			InlayAdded?.Invoke(this, inlayConfig);
			SaveSettings();

			return inlayConfig;
		}

		private void NavigateInlay(InlayConfiguration inlayConfig)
		{
			if (inlayConfig.Url == "") { inlayConfig.Url = "about:blank"; }
			InlayNavigated?.Invoke(this, inlayConfig);
		}

		private void ReloadInlay(InlayConfiguration inlayConfig) { NavigateInlay(inlayConfig); }

		private void DebugInlay(InlayConfiguration inlayConfig)
		{
			InlayDebugged?.Invoke(this, inlayConfig);
		}

		private void RemoveInlay(InlayConfiguration inlayConfig)
		{
			InlayRemoved?.Invoke(this, inlayConfig);
			Config.Inlays.Remove(inlayConfig);
			SaveSettings();
		}

		private void SetActiveTransport(FrameTransportMode transport)
		{
			Config.FrameTransportMode = transport;
			TransportChanged?.Invoke(this, null);
		}

		private void DebouncedSaveSettings()
		{
			saveDebounceTimer?.Dispose();
			saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
		}

		private void SaveSettings()
		{
			saveDebounceTimer?.Dispose();
			saveDebounceTimer = null;
			pluginInterface.SavePluginConfig(Config);
		}

		private string GetInlayCommandName(InlayConfiguration inlayConfig)
		{
			return Regex.Replace(inlayConfig.Name, @"\s+", "").ToLower();
		}

		public void Render()
		{
			if (!open || Config == null) { return; }

			// Primary window container
			ImGui.SetNextWindowSizeConstraints(new Vector2(400, 300), new Vector2(9001, 9001));
			var windowFlags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoCollapse;
			ImGui.Begin("BrowserHost Settings", ref open, windowFlags);

			RenderPaneSelector();

			// Pane details
			var dirty = false;
			ImGui.SameLine();
			ImGui.BeginChild("details");
			if (selectedInlay == null)
			{
				dirty |= RenderGeneralSettings();
			}
			else
			{
				dirty |= RenderInlaySettings(selectedInlay);
			}
			ImGui.EndChild();

			if (dirty) { DebouncedSaveSettings(); }

			ImGui.End();
		}

		private void RenderPaneSelector()
		{
			// Selector pane
			ImGui.BeginGroup();
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

			var selectorWidth = 100;
			ImGui.BeginChild("panes", new Vector2(selectorWidth, -ImGui.GetFrameHeightWithSpacing()), true);

			// General settings
			if (ImGui.Selectable($"General", selectedInlay == null))
			{
				selectedInlay = null;
			}

			// Inlay selector list
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			ImGui.Text("- Inlays -");
			ImGui.PopStyleVar();
			foreach (var inlayConfig in Config.Inlays)
			{
				if (ImGui.Selectable($"{inlayConfig.Name}##{inlayConfig.Guid}", selectedInlay == inlayConfig))
				{
					selectedInlay = inlayConfig;
				}
			}
			ImGui.EndChild();

			// Selector controls
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
			ImGui.PushFont(UiBuilder.IconFont);

			var buttonWidth = selectorWidth / 2;
			if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0)))
			{
				selectedInlay = AddNewInlay();
			}

			ImGui.SameLine();
			if (selectedInlay != null)
			{
				if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0)))
				{
					var toRemove = selectedInlay;
					selectedInlay = null;
					RemoveInlay(toRemove);
				}
			}
			else
			{
				ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
				ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0));
				ImGui.PopStyleVar();
			}

			ImGui.PopFont();
			ImGui.PopStyleVar(2);

			ImGui.EndGroup();
		}

		private bool RenderGeneralSettings()
		{
			var dirty = false;

			ImGui.Text("Select an inlay on the left to edit its settings.");

			if (ImGui.CollapsingHeader("Command Help", ImGuiTreeNodeFlags.DefaultOpen))
			{
				// TODO: If this ever gets more than a few options, should probably colocate help with the defintion. Attributes?
				ImGui.Text("/bh config");
				ImGui.Text("Open this configuration window.");
				ImGui.Dummy(new Vector2(0, 5));
				ImGui.Text("/bh inlay [inlayCommandName] [setting] [value]");
				ImGui.TextWrapped(
					"Change a setting for an inlay.\n" +
					"\tinlayCommandName: The inlay to edit. Use the 'Command Name' shown in its config.\n" +
					"\tsetting: Value to change. Accepted settings are:\n" +
					"\t\turl: string\n" +
					"\t\tlocked: boolean\n" +
					"\t\thidden: boolean\n" +
					"\t\ttypethrough: boolean\n" +
					"\t\tclickthrough: boolean\n" +
					"\tvalue: Value to set for the setting. Accepted values are:\n" +
					"\t\tstring: any string value\n\t\tboolean: on, off, toggle");
			}

			if (ImGui.CollapsingHeader("Advanced Settings"))
			{
				var options = availableTransports.Select(transport => transport.ToString());
				var currentIndex = availableTransports.IndexOf(Config.FrameTransportMode);

				if (availableTransports.Count == 0)
				{
					options = options.Append("Initialising...");
					currentIndex = 0;
				}

				if (options.Count() <= 1) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }
				var transportChanged = ImGui.Combo("Frame transport", ref currentIndex, options.ToArray(), options.Count());
				if (options.Count() <= 1) { ImGui.PopStyleVar(); }

				// TODO: Flipping this should probably try to rebuild existing inlays
				dirty |= transportChanged;
				if (transportChanged)
				{
					SetActiveTransport(availableTransports[currentIndex]);
				}

				if (Config.FrameTransportMode == FrameTransportMode.BitmapBuffer)
				{
					ImGui.PushStyleColor(ImGuiCol.Text, 0xFF0000FF);
					ImGui.TextWrapped("The bitmap buffer frame transport is a fallback, and should only be used if no other options work for you. It is not as stable as the shared texture option.");
					ImGui.PopStyleColor();
				}
			}

			return dirty;
		}

		private bool RenderInlaySettings(InlayConfiguration inlayConfig)
		{
			var dirty = false;

			ImGui.PushID(inlayConfig.Guid.ToString());

			dirty |= ImGui.InputText("Name", ref inlayConfig.Name, 100);

			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			var commandName = GetInlayCommandName(inlayConfig);
			ImGui.InputText("Command Name", ref commandName, 100);
			ImGui.PopStyleVar();

			dirty |= ImGui.InputText("URL", ref inlayConfig.Url, 1000);
			if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateInlay(inlayConfig); }

			ImGui.SetNextItemWidth(100);
			ImGui.Columns(2, "boolInlayOptions", false);

			var true_ = true;
			if (inlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }
			dirty |= ImGui.Checkbox("Locked", ref inlayConfig.ClickThrough ? ref true_ : ref inlayConfig.Locked);
			if (inlayConfig.ClickThrough) { ImGui.PopStyleVar(); }
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from being resized or moved. This is implicitly set by Click Through."); }
			ImGui.NextColumn();

			dirty |= ImGui.Checkbox("Hidden", ref inlayConfig.Hidden);
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Hide the inlay. This does not stop the inlay from executing, only from being displayed."); }
			ImGui.NextColumn();


			if (inlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }
			dirty |= ImGui.Checkbox("Type Through", ref inlayConfig.ClickThrough ? ref true_ : ref inlayConfig.TypeThrough);
			if (inlayConfig.ClickThrough) { ImGui.PopStyleVar(); }
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from intercepting any keyboard events. Implicitly set by Click Through."); }
			ImGui.NextColumn();

			dirty |= ImGui.Checkbox("Click Through", ref inlayConfig.ClickThrough);
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from intercepting any mouse events. Implicitly sets Locked and Type Through."); }
			ImGui.NextColumn();

			ImGui.Columns(1);

			if (ImGui.Button("Reload")) { ReloadInlay(inlayConfig); }

			ImGui.SameLine();
			if (ImGui.Button("Open Dev Tools")) { DebugInlay(inlayConfig); }

			ImGui.PopID();

			return dirty;
		}
	}
}
