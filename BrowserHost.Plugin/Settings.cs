using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		public event EventHandler<InlayConfiguration> InlayAdded;

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

		public void Render()
		{
			if (!open) { return; }

			ImGui.Begin("Settings##BrowserHost", ref open);

			foreach (var inlay in config.Inlays)
			{
				if (ImGui.CollapsingHeader($"{inlay.Name}###{inlay.Guid}"))
				{
					ImGui.InputText("Name", ref inlay.Name, 100);
					ImGui.InputText("URL", ref inlay.Url, 1000);
					ImGui.Checkbox("Locked", ref inlay.Locked);
					ImGui.Checkbox("Click Through", ref inlay.ClickThrough);
				}
			}

			if (ImGui.Button("Add new inlay")) { AddNewInlay(); }

			ImGui.End();
		}
	}
}
