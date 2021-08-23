using System.Threading.Channels;
using Dalamud.Data;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using XivCommon;

namespace QuestMap {
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class Plugin : IDalamudPlugin {
        public string Name => "Quest Map";

        [PluginService]
        internal DalamudPluginInterface Interface { get; init; } = null!;

        [PluginService]
        internal ClientState ClientState { get; init; } = null!;

        [PluginService]
        internal CommandManager CommandManager { get; init; } = null!;

        [PluginService]
        internal DataManager DataManager { get; init; } = null!;

        [PluginService]
        internal GameGui GameGui { get; init; } = null!;

        [PluginService]
        internal SeStringManager SeStringManager { get; init; } = null!;

        internal XivCommonBase Common { get; }
        internal Configuration Config { get; }
        internal Quests Quests { get; }
        internal PluginUi Ui { get; }
        private Commands Commands { get; }

        public Plugin() {
            this.Common = new XivCommonBase();
            this.Config = this.Interface.GetPluginConfig() as Configuration ?? new Configuration();

            var graphChannel = Channel.CreateUnbounded<GraphInfo>();
            this.Quests = new Quests(this, graphChannel.Writer);
            this.Ui = new PluginUi(this, graphChannel.Reader);

            this.Commands = new Commands(this);
        }

        public void Dispose() {
            this.Commands.Dispose();
            this.Ui.Dispose();
            this.Common.Dispose();
        }

        internal void SaveConfig() {
            this.Interface.SavePluginConfig(this.Config);
        }
    }
}
