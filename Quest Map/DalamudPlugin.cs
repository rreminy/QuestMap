using Dalamud.Plugin;

namespace QuestMap {
    // ReSharper disable once UnusedType.Global
    public class DalamudPlugin : IDalamudPlugin {
        internal const string PluginName = "Quest Map";

        public string Name => PluginName;

        private Plugin? Plugin { get; set; }

        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.Plugin = new Plugin(pluginInterface);
        }

        public void Dispose() {
            this.Plugin?.Dispose();
        }
    }
}
