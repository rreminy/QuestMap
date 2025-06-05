using System.Threading.Channels;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace QuestMap {
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class Plugin : IDalamudPlugin {
        internal static string Name => "Quest Map";

        [PluginService]
        internal IDalamudPluginInterface Interface { get; init; } = null!;

        [PluginService]
        internal IClientState ClientState { get; init; } = null!;

        [PluginService]
        internal ICommandManager CommandManager { get; init; } = null!;

        [PluginService]
        internal IDataManager DataManager { get; init; } = null!;

        [PluginService]
        internal IPluginLog Logger { get; init; } = null!;

        [PluginService]
        internal IGameGui GameGui { get; init; } = null!;

        [PluginService]
        internal ITextureProvider TextureProvider { get; init; } = null!;

        internal Configuration Config { get; }
        internal Quests Quests { get; }
        internal PluginUi Ui { get; }
        private Commands Commands { get; }
        private Ipc Ipc { get; }

        public Plugin(IDalamudPluginInterface pluginInterface) {
            pluginInterface.Inject(this);
            this.Config = this.Interface.GetPluginConfig() as Configuration ?? new Configuration();

            this.Quests = new Quests(this);
            this.Ui = new PluginUi(this);

            this.Commands = new Commands(this);
            this.Ipc = new Ipc(this);
        }

        public void Dispose() {
            this.Ipc.Dispose();
            this.Commands.Dispose();
            this.Ui.Dispose();
        }

        internal void SaveConfig() {
            this.Interface.SavePluginConfig(this.Config);
        }
    }
}
