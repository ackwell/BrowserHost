using BrowserHost.Common;
using ImGuiNET;
using System;
using System.Numerics;

namespace BrowserHost.Plugin
{
	class Inlay : IDisposable
	{
		public InlayConfiguration Config;

		private Vector2 size;

		private BrowserWidget browserWidget;

		public Inlay(InlayConfiguration config, BrowserWidget browserWidget)
		{
			Config = config;
			this.browserWidget = browserWidget;
		}

		public void Dispose()
		{
			browserWidget.Dispose();
		}

		public void Navigate(string newUrl)
		{
			browserWidget.Navigate(newUrl);
		}

		public void Debug()
		{
			browserWidget.Debug();
		}

		public void Render()
		{
			ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
			ImGui.Begin($"{Config.Name}###{Config.Guid}", GetWindowFlags());

			HandleWindowSize();

			var captureMouse = ImGui.IsWindowHovered() && !Config.ClickThrough;
			browserWidget.Render(captureMouse);

			ImGui.End();
		}

		private ImGuiWindowFlags GetWindowFlags()
		{
			var flags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoTitleBar
				| ImGuiWindowFlags.NoCollapse
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoBringToFrontOnFocus
				| ImGuiWindowFlags.NoFocusOnAppearing;

			if (Config.Locked || Config.ClickThrough)
			{
				flags |= ImGuiWindowFlags.None
					| ImGuiWindowFlags.NoMove
					| ImGuiWindowFlags.NoResize
					| ImGuiWindowFlags.NoBackground;
			}
			if (Config.ClickThrough) { flags |= ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNav; }

			return flags;
		}

		private void HandleWindowSize()
		{
			var currentSize = ImGui.GetWindowContentRegionMax() - ImGui.GetWindowContentRegionMin();
			if (currentSize == size) { return; }
			size = currentSize;

			browserWidget.Resize(size);
		}
	}
}
