using ImGuiNET;
using System;
using System.Collections.Generic;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		private bool open = true;

		private Dictionary<Guid, Inlay> inlays;

		public Settings(Dictionary<Guid, Inlay> inlays)
		{
			this.inlays = inlays;
		}

		public void Dispose() { }

		public void Render()
		{
			if (!open) { return; }

			ImGui.Begin("Settings##BrowserHost", ref open);

			foreach (var inlay in inlays.Values)
			{
				if (ImGui.CollapsingHeader($"{inlay.Name}###{inlay.Guid}"))
				{
					ImGui.InputText("Name", ref inlay.Name, 100);
					ImGui.Checkbox("Locked", ref inlay.Locked);
					ImGui.Checkbox("Click Through", ref inlay.ClickThrough);
				}
			}

			ImGui.End();
		}
	}
}
