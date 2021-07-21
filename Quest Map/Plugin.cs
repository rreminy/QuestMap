using System;
using System.Threading.Channels;
using Dalamud.Plugin;
using Microsoft.Msagl.Core.Layout;
using XivCommon;

namespace QuestMap {
    internal class Plugin : IDisposable {
        internal DalamudPluginInterface Interface { get; }
        internal XivCommonBase Common { get; }
        internal Configuration Config { get; }
        internal Quests Quests { get; }
        internal PluginUi Ui { get; }
        private Commands Commands { get; }

        internal Plugin(DalamudPluginInterface pluginInterface) {
            this.Interface = pluginInterface;
            this.Common = new XivCommonBase(pluginInterface);
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
