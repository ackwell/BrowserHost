using BrowserHost.Common;
using ImGuiNET;
using ImGuiScene;
using SharedMemory;
using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;
using DXGI = SharpDX.DXGI;
using System.Numerics;
using System.Threading;
using System.Collections.Concurrent;

namespace BrowserHost.Plugin.TextureHandlers
{
	class BitmapBufferTextureHandler : ITextureHandler
	{
		private Thread frameBufferThread;
		private D3D11.Texture2D texture;
		private TextureWrap textureWrap;

		private BufferReadWrite bitmapBuffer;
		private ConcurrentQueue<BitmapFrame> frameQueue = new ConcurrentQueue<BitmapFrame>();

		public BitmapBufferTextureHandler(BitmapBufferResponse response)
		{
			frameBufferThread = new Thread(FrameBufferThread);
			frameBufferThread.Start(response.FrameInfoBufferName);

			bitmapBuffer = new BufferReadWrite(response.BitmapBufferName);
		}

		public void Dispose()
		{
			// TODO: Nicer handling of this. Maybe wait handle that closes the loop or something?
			frameBufferThread.Abort();
		}

		public void Render()
		{
			// Render incoming frame info on the queue. Doing a queue swap to prevent edge cases where a slow game
			// paired with a fast renderer will loop dequeue infinitely.
			var currentFrameQueue = frameQueue;
			frameQueue = new ConcurrentQueue<BitmapFrame>();
			while (currentFrameQueue.TryDequeue(out BitmapFrame frame))
			{
				RenderFrame(frame);
			}

			if (textureWrap == null) { return; }

			ImGui.Image(textureWrap.ImGuiHandle, new Vector2(textureWrap.Width, textureWrap.Height));
		}

		private void RenderFrame(BitmapFrame frame)
		{
			// Make sure there's a texture to render to
			// TODO: Can probably afford to remove width/height from frame, and just add the buffer name. this client code can then check that the buffer name matches what it expects, and noop if it doesn't
			if (texture == null)
			{
				BuildTexture(frame.Width, frame.Height);
			}

			// If the details don't match our expected sizes, noop the frame to avoid a CTD.
			// This may "stick" and cause no rendering at all, but can be fixed by jiggling the size a bit, or reloading.
			if (
				texture.Description.Width != frame.Width
				|| texture.Description.Height != frame.Height
				|| bitmapBuffer.BufferSize != frame.Length
			) {
				return;
			}

			// Write data from the buffer
			var rowPitch = frame.Length / frame.Height;
			var depthPitch = frame.Length;

			var context = DxHandler.Device.ImmediateContext;
			bitmapBuffer.Read(ptr =>
			{
				context.UpdateSubresource(texture, 0, null, ptr, rowPitch, depthPitch);
			});
		}

		private void BuildTexture(int width, int height)
		{
			// TODO: This should probably be a dynamic texture, with updates performed via mapping. Work it out.
			texture = new D3D11.Texture2D(DxHandler.Device, new D3D11.Texture2DDescription()
			{
				Width = width,
				Height = height,
				MipLevels = 1,
				ArraySize = 1,
				Format = DXGI.Format.B8G8R8A8_UNorm,
				SampleDescription = new DXGI.SampleDescription(1, 0),
				Usage = D3D11.ResourceUsage.Default,
				//Usage = D3D11.ResourceUsage.Dynamic,
				BindFlags = D3D11.BindFlags.ShaderResource,
				CpuAccessFlags = D3D11.CpuAccessFlags.None,
				//CpuAccessFlags = D3D11.CpuAccessFlags.Write,
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

		private void FrameBufferThread(object bufferName)
		{
			// Open up a reference to the frame info buffer
			using var frameInfoBuffer = new CircularBuffer((string)bufferName);

			// We're just looping the blocking read operation forever. Parent will abort the to shut down.
			while (true)
			{
				frameInfoBuffer.Read(out BitmapFrame frame, timeout: Timeout.Infinite);
				frameQueue.Enqueue(frame);
			}
		}
	}
}
