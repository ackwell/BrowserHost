using CefSharp;
using System;

namespace BrowserHost.Renderer
{
	class SqpackSchemeHandlerFactory : ISchemeHandlerFactory
	{
		// TODO: use `browser.Identifier` to check whitelisted game access
		public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
		{
			var uri = new Uri(request.Url);
			var path = $"{uri.Host}{uri.AbsolutePath}";
			Console.WriteLine($"SCHEME HIT path {path}");
			return null;
		}
	}
}
