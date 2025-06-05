using System;
using System.Linq;
using Dalamud.Game.Command;
using Lumina.Excel.Sheets;

namespace QuestMap {
    internal class Commands : IDisposable {
        private Plugin Plugin { get; }

        internal Commands(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.CommandManager.AddHandler("/quests", new CommandInfo(this.OnCommand) { HelpMessage = "Show Quest Map" });
        }

        public void Dispose() {
            this.Plugin.CommandManager.RemoveHandler("/quests");
        }

        private void OnCommand(string command, string args) {
            if (string.IsNullOrWhiteSpace(args))
            {
                this.Plugin.Ui.Show ^= true;
            }
            else
            {
                if (uint.TryParse(args, out var questId))
                {
                    this.Plugin.Ui.ShowQuest(questId);
                }
                else
                {
                    var node = this.Plugin.Quests.AllNodes.Values.FirstOrDefault(node => node.Name.Equals(args, StringComparison.InvariantCultureIgnoreCase));
                    if (node is not null) this.Plugin.Ui.ShowQuest(node.Quest);
                }
            }
        }
    }
}
