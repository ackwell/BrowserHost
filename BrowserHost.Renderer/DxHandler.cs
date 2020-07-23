using D3D = SharpDX.Direct3D;
using D3D11 = SharpDX.Direct3D11;

namespace BrowserHost.Renderer
{
	static class DxHandler
	{
		public static D3D11.Device Device { get; private set; }

		public static void Initialise()
		{
			// TODO: Need to ensure that our render device is on the same adapter as
			//       the primary game process to ensure shared textures will work.
			var flags = D3D11.DeviceCreationFlags.BgraSupport;
#if DEBUG
			flags |= D3D11.DeviceCreationFlags.Debug;
#endif

			Device = new D3D11.Device(D3D.DriverType.Hardware, flags);
		}

		public static void Shutdown()
		{
			Device.Dispose();
		}
	}
}
