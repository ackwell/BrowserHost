using ImGuiNET;
using System;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		private bool open = true;

		public void Dispose() { }

		public void Render()
		{
			if (!open) { return; }

			ImGui.Begin("Settings##BrowserHost", ref open);

			ImGui.Text("settings pane");

			ImGui.End();
		}
	}
}
