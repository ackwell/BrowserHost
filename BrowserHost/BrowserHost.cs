using Dalamud.Plugin;

namespace BrowserHost
{
    public class BrowserHost : IDalamudPlugin
    {
        public string Name => "Browser Host";

        private DalamudPluginInterface pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            PluginLog.Log("BrowserHost loaded.");
        }

        public void Dispose()
        {
            pluginInterface.Dispose();
        }
    }
}
