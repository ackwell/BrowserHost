using Dalamud.Interface;
using Dalamud.Plugin;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Reflection;

namespace BrowserHost.Plugin
{
	static class DxHandler
	{
		public static D3D11.Device Device { get; private set; }
		public static IntPtr WindowHandle { get; private set; }
		public static long AdapterLuid { get; private set; }

		public static void Initialise(DalamudPluginInterface pluginInterface)
		{
			// Drill into the UI builder to grab its reference to the dx device
			var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
			var interfaceManager = typeof(UiBuilder).GetField("interfaceManager", bindingFlags).GetValue(pluginInterface.UiBuilder);
			var scene = interfaceManager.GetType().GetField("scene", bindingFlags).GetValue(interfaceManager);

			var sceneType = scene.GetType();
			Device = (D3D11.Device)sceneType.GetField("device", bindingFlags).GetValue(scene);

			// Grab the window handle, we'll use this for setting up our wndproc hook
			var SwapChain = (DXGI.SwapChain)sceneType.GetField("swapChain", bindingFlags).GetValue(scene);
			WindowHandle = SwapChain.Description.OutputHandle;

			// Get the game's device adapter, we'll need that as a reference for the render process.
			var dxgiDevice = Device.QueryInterface<DXGI.Device>();
			AdapterLuid = dxgiDevice.Adapter.Description.Luid;
		}

		public static void Shutdown()
		{
			Device = null;
		}
	}
}
