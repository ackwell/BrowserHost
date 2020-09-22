using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace BrowserHost.Plugin
{
	[Serializable]
	class Configuration : IPluginConfiguration
	{
		public int Version { get; set; } = 0;

		public List<InlayConfiguration> Inlays = new List<InlayConfiguration>();
	}

	[Serializable]
	class InlayConfiguration
	{
		public Guid Guid;
		public string Name;
		public string Url;
		public bool Visible;
		public bool Locked;
		public bool ClickThrough;
	}
}
