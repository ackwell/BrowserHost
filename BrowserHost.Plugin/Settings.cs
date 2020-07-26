using Dalamud.Game.Command;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		public event EventHandler<InlayConfiguration> InlayAdded;
		public event EventHandler<InlayConfiguration> InlayNavigated;
		public event EventHandler<InlayConfiguration> InlayRemoved;

		private bool open = true;

		private DalamudPluginInterface pluginInterface;

		private Configuration config;

		private Timer saveDebounceTimer;

		public Settings(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;

			pluginInterface.UiBuilder.OnOpenConfigUi += (sender, args) => open = true;
			pluginInterface.CommandManager.AddHandler("/pbrowser", new CommandInfo((command, arguments) => open = true)
			{
				HelpMessage = "Open BrowserHost configuration pane.",
				ShowInHelp = true,
			});
		}

		public void Initialise()
		{
			// Running this in a thread to avoid blocking the plugin init with potentially expensive stuff
			Task.Run(() =>
			{
				config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

				// Hydrate any inlays in the config
				foreach (var inlayConfig in config.Inlays)
				{
					InlayAdded?.Invoke(this, inlayConfig);
				}
			});
		}

		public void Dispose() { }

		private void AddNewInlay()
		{
			var inlayConfig = new InlayConfiguration()
			{
				Guid = Guid.NewGuid(),
				Name = "New inlay",
				Url = "about:blank",
			};
			config.Inlays.Add(inlayConfig);
			InlayAdded?.Invoke(this, inlayConfig);
			SaveSettings();
		}

		private void NavigateInlay(InlayConfiguration inlayConfig)
		{
			if (inlayConfig.Url == "") { inlayConfig.Url = "about:blank"; }
			InlayNavigated?.Invoke(this, inlayConfig);
		}

		private void RemoveInlay(InlayConfiguration inlayConfig)
		{
			InlayRemoved?.Invoke(this, inlayConfig);
			config.Inlays.Remove(inlayConfig);
			SaveSettings();
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
			pluginInterface.SavePluginConfig(config);
		}

		public void Render()
		{
			if (!open || config == null) { return; }

			var windowFlags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoCollapse;
			ImGui.Begin("Settings##BrowserHost", ref open, windowFlags);

			var contentArea = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			var footerHeight = 30; // I hate this. TODO: Calc from GetStyle() somehow?
			ImGui.BeginChild("inlays", new Vector2(0, contentArea.Y - footerHeight));

			var dirty = false;
			var toRemove = new List<InlayConfiguration>();
			foreach (var inlayConfig in config.Inlays)
			{
				var headerOpen = true;

				if (ImGui.CollapsingHeader($"{inlayConfig.Name}###header-{inlayConfig.Guid}", ref headerOpen))
				{
					ImGui.PushID(inlayConfig.Guid.ToString());

					ImGui.InputText("Name", ref inlayConfig.Name, 100);
					dirty |= ImGui.IsItemEdited();

					ImGui.InputText("URL", ref inlayConfig.Url, 1000);
					dirty |= ImGui.IsItemEdited();
					if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateInlay(inlayConfig); }

					ImGui.Checkbox("Locked", ref inlayConfig.Locked);
					dirty |= ImGui.IsItemEdited();

					ImGui.SameLine();
					ImGui.Checkbox("Click Through", ref inlayConfig.ClickThrough);
					dirty |= ImGui.IsItemEdited();

					ImGui.Spacing();

					ImGui.PopID();
				}

				if (!headerOpen) { toRemove.Add(inlayConfig); }
			}

			foreach (var inlayConfig in toRemove) { RemoveInlay(inlayConfig); }
			if (dirty) { DebouncedSaveSettings();  }

			ImGui.EndChild();
			ImGui.Separator();

			if (ImGui.Button("Add new inlay")) { AddNewInlay(); }

			ImGui.End();
		}
	}
}
