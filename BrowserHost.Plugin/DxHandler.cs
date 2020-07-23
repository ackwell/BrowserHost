using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiScene;
using D3D11 = SharpDX.Direct3D11;
using System.Reflection;

namespace BrowserHost.Plugin
{
	static class DxHandler
	{
		public static D3D11.Device Device { get; private set; }

		public static void Initialise(DalamudPluginInterface pluginInterface)
		{
			var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
			var interfaceManager = typeof(UiBuilder).GetField("interfaceManager", bindingFlags).GetValue(pluginInterface.UiBuilder);
			var scene = interfaceManager.GetType().GetField("scene", bindingFlags).GetValue(interfaceManager);

			Device = (D3D11.Device)typeof(RawDX11Scene).GetField("device", bindingFlags).GetValue(scene);
		}

		public static void Shutdown()
		{
			Device = null;
		}
	}
}
