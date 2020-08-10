using BrowserHost.Common;
using ImGuiNET;
using ImGuiScene;
using SharedMemory;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System;
using System.Numerics;
using System.Threading;

namespace BrowserHost.Plugin.TextureHandlers
{
	class BitmapBufferTextureHandler : ITextureHandler
	{
		private Thread frameBufferThread;
		private D3D11.Texture2D texture;
		private TextureWrap textureWrap;

		public BitmapBufferTextureHandler(string bufferName)
		{
			frameBufferThread = new Thread(FrameBufferThread);
			frameBufferThread.Start(bufferName);
		}

		public void Dispose()
		{
			// TODO: Nicer handling of this. Maybe wait handle that closes the loop or something?
			frameBufferThread.Abort();
		}

		public void Render()
		{
			if (textureWrap == null) { return; }

			ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
		}

		private void FrameBufferThread(object bufferName)
		{
			var buffer = new CircularBuffer(bufferName as string);
			while (true)
			{
				buffer.Read(out BitmapFrame frame, timeout: Timeout.Infinite);
				// this is possibly thrashing memory more than i'd like? read -> byte[] -> copy ptr
				var data = new byte[frame.Length];
				buffer.Read(data, timeout: Timeout.Infinite);

				// Make sure we have an up-to-date texture to write to
				if (
					texture == null ||
					texture.Description.Width != frame.Width ||
					texture.Description.Height != frame.Height
				) {
					BuildTexture(frame.Width, frame.Height);
				}

				// Do the writing
				var rowPitch = frame.Length / frame.Height;
				var depthPitch = frame.Length;

				var context = DxHandler.Device.ImmediateContext;
				unsafe
				{
					fixed (byte* dataPtr = data)
					{
						context.UpdateSubresource(texture, 0, null, (IntPtr)dataPtr, rowPitch, depthPitch);
					}
				}
			}
		}

		private void BuildTexture(int width, int height)
		{
			texture = new D3D11.Texture2D(DxHandler.Device, new D3D11.Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = DXGI.Format.B8G8R8A8_UNorm,
				SampleDescription = new DXGI.SampleDescription(1, 0),
				Usage = D3D11.ResourceUsage.Default, // dynamic
				BindFlags = D3D11.BindFlags.ShaderResource,
				CpuAccessFlags = D3D11.CpuAccessFlags.None, // write
				OptionFlags = D3D11.ResourceOptionFlags.None,
			});

			var view = new D3D11.ShaderResourceView(DxHandler.Device, texture, new D3D11.ShaderResourceViewDescription()
			{
				Format = texture.Description.Format,
				Dimension = D3D.ShaderResourceViewDimension.Texture2D,
				Texture2D = { MipLevels = texture.Description.MipLevels },
			});

			textureWrap = new D3DTextureWrap(view, texture.Description.Width, texture.Description.Height);
		}
	}
}
