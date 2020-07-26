using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		public event EventHandler<InlayConfiguration> InlayAdded;
		public event EventHandler<Guid> InlayRemoved;

		private bool open = true;

		private DalamudPluginInterface pluginInterface;

		private Configuration config;

		public Settings(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;

			config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
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
		}

		private void RemoveInlay(InlayConfiguration inlayConfig)
		{
			InlayRemoved?.Invoke(this, inlayConfig.Guid);
			config.Inlays.Remove(inlayConfig);
		}

		public void Render()
		{
			if (!open) { return; }

			var windowFlags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoCollapse;
			ImGui.Begin("Settings##BrowserHost", ref open, windowFlags);

			var contentArea = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			var footerHeight = 30; // I hate this. TODO: Calc from GetStyle() somehow?
			ImGui.BeginChild("inlays", new Vector2(0, contentArea.Y - footerHeight));

			var toRemove = new List<InlayConfiguration>();
			foreach (var inlay in config.Inlays)
			{
				var headerOpen = true;

				if (ImGui.CollapsingHeader($"{inlay.Name}###header-{inlay.Guid}", ref headerOpen))
				{
					ImGui.PushID(inlay.Guid.ToString());

					ImGui.InputText("Name", ref inlay.Name, 100);
					ImGui.InputText("URL", ref inlay.Url, 1000);

					ImGui.Checkbox("Locked", ref inlay.Locked);
					ImGui.SameLine();
					ImGui.Checkbox("Click Through", ref inlay.ClickThrough);
					ImGui.Spacing();

					ImGui.PopID();
				}

				if (!headerOpen) { toRemove.Add(inlay); }
			}

			foreach (var inlay in toRemove) { RemoveInlay(inlay); }

			ImGui.EndChild();
			ImGui.Separator();

			if (ImGui.Button("Add new inlay")) { AddNewInlay(); }

			ImGui.End();
		}
	}
}
