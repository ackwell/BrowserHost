using BrowserHost.Common;
using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiNET;
using ImGuiScene;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
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

		private Thread thread;

		private TextureWrap sharedTextureWrap;

		public void Initialize(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;
			pluginInterface.UiBuilder.OnBuildUi += DrawUi;

			var pid = Process.GetCurrentProcess().Id;

			PluginLog.Log("Configuring render process.");

			renderProcess = new RenderProcess(pid);
			renderProcess.Start();

			thread = new Thread(ThreadProc);
			thread.Start();

			PluginLog.Log("Loaded.");
		}

		private void ThreadProc()
		{
			var response = renderProcess.Send<NewInlayResponse>(new NewInlayRequest()
			{
				Url = "https://www.testufo.com/framerates#count=3&background=stars&pps=960",
				Width = 800,
				Height = 800,
			});

			var resPtr = response.TextureHandle;

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
			ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
			if (ImGui.Begin("BrowserHost DXTex"))
			{
				var io = ImGui.GetIO();
				var relativeMousePos = io.MousePos - ImGui.GetWindowPos() - ImGui.GetWindowContentRegionMin();
				MouseMove(relativeMousePos);

				if (sharedTextureWrap != null)
				{
					ImGui.Image(sharedTextureWrap.ImGuiHandle, new Vector2(sharedTextureWrap.Width, sharedTextureWrap.Height));
				}
			}
			ImGui.End();
			ImGui.PopStyleVar();
		}

		// TODO: Proper per-inlay handling, only current focus, etc, etc, etc
		private void MouseMove(Vector2 position)
		{
			// TODO: lmao, yikes
			if (renderProcess == null) { return; }
			// TODO: This should probably be async so we're not blocking the render thread with IPC
			var response = renderProcess.Send<MouseMoveResponse>(new MouseMoveRequest()
			{
				X = position.X,
				Y = position.Y,
			});
		}

		public void Dispose()
		{
			renderProcess.Dispose();

			thread.Join();

			pluginInterface.Dispose();
		}
	}
}
