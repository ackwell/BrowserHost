using BrowserHost.Common;
using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiNET;
using ImGuiScene;
using SharedMemory;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace BrowserHost.Plugin
{
	public class Plugin : IDalamudPlugin
	{
		public string Name => "Browser Host";

		private DalamudPluginInterface pluginInterface;

		private RenderProcess renderProcess;

		private CircularBuffer consumer;
		private RpcBuffer rendererIpc;

		private Thread thread;

		private TextureWrap sharedTextureWrap;

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += DrawUi;

			var pid = Process.GetCurrentProcess().Id;

			consumer = new CircularBuffer($"DalamudBrowserHostFrameBuffer{pid}", nodeCount: 5, nodeBufferSize: 1024 * 1024 * 10 /* 10M */);
			rendererIpc = new RpcBuffer($"BrowserHostRendererIpcChannel{pid}");

			thread = new Thread(ThreadProc);
			thread.Start();

			PluginLog.Log("Configuring render process.");

			renderProcess = new RenderProcess(pid);
			renderProcess.Start();

			PluginLog.Log("Loaded.");
		}

		private void ThreadProc()
		{
			// TESTING STUFF
			byte[] request;
			var formatter = new BinaryFormatter();
			using (MemoryStream stream = new MemoryStream())
			{
				formatter.Serialize(stream, new NewInlayRequest()
				{
					Width = 420,
					Height = 69,
				});
				request = stream.ToArray();
			}

			var rawResponse = rendererIpc.RemoteRequest(request, timeoutMs: Timeout.Infinite);
			PluginLog.Log($"success: rawResponse.Success");

			NewInlayResponse response;
			using (MemoryStream stream = new MemoryStream(rawResponse.Data))
			{
				response = (NewInlayResponse)formatter.Deserialize(stream);
			}

			PluginLog.Log($"Got response {response.TextureHandle}");

			// First value is IntPtr to a DXGI shared resource
			var resPtrData = new IntPtr[1];
			consumer.Read(resPtrData, timeout: Timeout.Infinite);
			var resPtr = resPtrData[0];

			PluginLog.Log($"Incoming resource pointer {resPtr}");

			// Yeehaw
			var bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;
			var im = typeof(UiBuilder).GetField("interfaceManager", bindingFlags).GetValue(pluginInterface.UiBuilder);
			var scene = im.GetType().GetField("scene", bindingFlags).GetValue(im);
			var device = (D3D11.Device)typeof(RawDX11Scene).GetField("device", bindingFlags).GetValue(scene);

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
