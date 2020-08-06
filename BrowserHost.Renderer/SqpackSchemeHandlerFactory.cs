using CefSharp;
using Lumina.Data.Files;
using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace BrowserHost.Renderer
{
	class SqpackSchemeHandlerFactory : ISchemeHandlerFactory
	{
		private Lumina.Lumina lumina;

		public SqpackSchemeHandlerFactory(Lumina.Lumina lumina)
		{
			this.lumina = lumina;
		}

		// TODO: use `browser.Identifier` to check whitelisted game access
		public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
		{
			var uri = new Uri(request.Url);
			var path = $"{uri.Host}{uri.AbsolutePath}";
			var extension = Path.GetExtension(path);

			return extension switch
			{
				".tex" => ReadTexFile(path),
				_ => throw new Exception($"Unknown or unhandled extension `{extension}`"),
			};
		}

		private IResourceHandler ReadTexFile(string path)
		{
			var file = lumina.GetFile<TexFile>(path);
			var image = file.GetImage();

			var memoryStream = new MemoryStream();
			image.Save(memoryStream, ImageFormat.Png);

			return ResourceHandler.FromStream(
				memoryStream,
				mimeType: Cef.GetMimeType(".png"),
				autoDisposeStream: true);
		}
	}
}
