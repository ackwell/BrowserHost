using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiNET;
using ImGuiScene;
using SharedMemory;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Threading;

namespace BrowserHost.Plugin
{
	public class Plugin : IDalamudPlugin
	{
		public string Name => "Browser Host";

		private DalamudPluginInterface pluginInterface;

		private RenderProcess renderProcess;

		private CircularBuffer consumer;

		private Thread thread;

		private byte[] frameBuffer;

		private TextureWrap sharedTextureWrap;

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += DrawUi;

			consumer = new CircularBuffer("DalamudBrowserHostFrameBuffer", nodeCount: 5, nodeBufferSize: 1024 * 1024 * 10 /* 10M */);

			thread = new Thread(ThreadProc);
			thread.Start();

			PluginLog.Log("Configuring render process.");

			var pid = Process.GetCurrentProcess().Id;

			renderProcess = new RenderProcess(pid);

			PluginLog.Log("Loaded.");
		}

		private void ThreadProc()
		{
			// TODO: Struct this or something
			// First data value will be the size of incoming bitmap
			var sizeData = new int[1];
			consumer.Read(sizeData, timeout: Timeout.Infinite);
			var size = sizeData[0];

			// Second value is the full bitmap, of the previously recorded size
			var buffer = new byte[size];
			consumer.Read(buffer, timeout: Timeout.Infinite);

			PluginLog.Log($"Read bitmap buffer of size {size}");

			frameBuffer = buffer;

			//// Testing stuff: Third value is an IntPtr to a DXGI shared resouce
			var resPtrData = new IntPtr[1];
			consumer.Read(resPtrData, timeout: Timeout.Infinite);
			var resPtr = resPtrData[0];

			PluginLog.Log($"Incoming resource pointer {resPtr}");

			// Yeehaw
			var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
			var im = typeof(UiBuilder).GetField("interfaceManager", bindingFlags).GetValue(pluginInterface.UiBuilder);
			var scene = im.GetType().GetField("scene", bindingFlags).GetValue(im);
			var device = (D3D11.Device)typeof(RawDX11Scene).GetField("device", bindingFlags).GetValue(scene);

			//var factory = new DXGI.Factory1();
			//var adapter = factory.GetAdapter1(0);
			//var device = new D3D11.Device(adapter, D3D11.DeviceCreationFlags.BgraSupport);
			//PluginLog.Log($"Using adapter {adapter.Description.Description}");

			var texture = device.OpenSharedResource<D3D11.Texture2D>(resPtr);

			var view = new D3D11.ShaderResourceView(device, texture, new D3D11.ShaderResourceViewDescription()
			{
				Format = texture.Description.Format,
				Dimension = D3D.ShaderResourceViewDimension.Texture2D,
				Texture2D = { MipLevels = texture.Description.MipLevels },
			});
			PluginLog.Log($"Built view {view}");

			sharedTextureWrap = new D3DTextureWrap(view, texture.Description.Width, texture.Description.Height);
		}

		private void DrawUi()
		{
			if (ImGui.Begin("BrowserHost"))
			{
				var ready = frameBuffer != null;
				ImGui.Text($"ready: {ready}");

				if (ready)
				{
					// THIS IS WHOLLY RELIANT ON A FIX TO IMGUISCENE. IT WILL _NOT_ WORK ON REGULAR BUILDS.
					// (need to fix `MemoryStream` constructor call in `RawDX11Scene.LoadImage(byte[])`)
					// TODO: NUKE.
					var tex = pluginInterface.UiBuilder.LoadImage(frameBuffer);
					ImGui.Image(tex.ImGuiHandle, new Vector2(tex.Width, tex.Height));
				}
			}
			ImGui.End();

			if (ImGui.Begin("BrowserHost DXTex"))
			{
				var ready = sharedTextureWrap != null;
				ImGui.Text($"shared ready: {ready}");

				if (ready)
				{
					ImGui.Image(sharedTextureWrap.ImGuiHandle, new Vector2(sharedTextureWrap.Width, sharedTextureWrap.Height));
				}
			}
			ImGui.End();
		}

		public void Dispose()
		{
			renderProcess.Dispose();

			thread.Join();

			consumer.Dispose();

			pluginInterface.Dispose();
		}
	}
}
