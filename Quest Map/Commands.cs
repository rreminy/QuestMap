using System;
using Dalamud.Game.Command;

namespace QuestMap {
    internal class Commands : IDisposable {
        private Plugin Plugin { get; }

        internal Commands(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.CommandManager.AddHandler("/quests", new CommandInfo(this.OnCommand) {
                HelpMessage = "Show Quest Map",
            });
        }

        public void Dispose() {
            this.Plugin.CommandManager.RemoveHandler("/quests");
        }

        private void OnCommand(string command, string args) {
            this.Plugin.Ui.Show ^= true;
        }
    }
}
