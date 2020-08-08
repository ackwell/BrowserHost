using CefSharp;
using System;
using System.Collections.Generic;

namespace BrowserHost.Renderer
{
	class JsApi : IDisposable
	{
		public event EventHandler<string> EventDispatched;

		private Dictionary<string, List<IJavascriptCallback>> callbacks = new Dictionary<string, List<IJavascriptCallback>>();

		public void Dispose()
		{
			foreach (var callbacks in callbacks.Values)
			{
				foreach (var callback in callbacks)
				{
					callback.Dispose();
				}
			}
		}

		internal void Send(string name, object data)
		{
			var eventCallbacks = callbacks[name];
			if (eventCallbacks == null) { return; }
			eventCallbacks.ForEach(callback => callback.ExecuteAsync(data));
		}

		public void AddEventListener(string name, IJavascriptCallback callback)
		{
			var found = callbacks.TryGetValue(name, out List<IJavascriptCallback> eventCallbacks);
			if (!found)
			{
				eventCallbacks = new List<IJavascriptCallback>();
				callbacks.Add(name, eventCallbacks);
			}

			eventCallbacks.Add(callback);
		}

		// TODO: Data stuff
		public void DispatchEvent(string name)
		{
			EventDispatched?.Invoke(this, name);
		}
	}
}
