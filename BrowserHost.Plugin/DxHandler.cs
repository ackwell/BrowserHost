using Dalamud.Interface;
using Dalamud.Plugin;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System.Reflection;

namespace BrowserHost.Plugin
{
	static class DxHandler
	{
		public static D3D11.Device Device { get; private set; }
		public static DXGI.SwapChain SwapChain { get; private set; }

		public static void Initialise(DalamudPluginInterface pluginInterface)
		{
			var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
			var interfaceManager = typeof(UiBuilder).GetField("interfaceManager", bindingFlags).GetValue(pluginInterface.UiBuilder);
			var scene = interfaceManager.GetType().GetField("scene", bindingFlags).GetValue(interfaceManager);

			var sceneType = scene.GetType();
			Device = (D3D11.Device)sceneType.GetField("device", bindingFlags).GetValue(scene);
			SwapChain = (DXGI.SwapChain)sceneType.GetField("swapChain", bindingFlags).GetValue(scene);
		}

		public static void Shutdown()
		{
			Device = null;
		}
	}
}
