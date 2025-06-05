using System;
using System.Linq;
using Dalamud.Game.Command;
using Lumina.Excel.Sheets;

namespace QuestMap {
    internal class Commands : IDisposable {
        private Plugin Plugin { get; }

        internal Commands(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.CommandManager.AddHandler("/quests", new CommandInfo(this.ShowGraphCommand) { HelpMessage = "Show Quest Map" });
            this.Plugin.CommandManager.AddHandler("/questinfo", new CommandInfo(this.ShowInfoCommand) { HelpMessage = "Show information for a specified quest by name or id" });
        }

        public void Dispose() {
            this.Plugin.CommandManager.RemoveHandler("/quests");
        }

        private void ShowGraphCommand(string command, string args) {
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

        private void ShowInfoCommand(string command, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                this.Plugin.Chat.PrintError("/questinfo needs a quest name or id");
            }
            else
            {
                if (uint.TryParse(args, out var questId))
                {
                    this.Plugin.Ui.ShowInfo(questId);
                }
                else
                {
                    var node = this.Plugin.Quests.AllNodes.Values.FirstOrDefault(node => node.Name.Equals(args, StringComparison.InvariantCultureIgnoreCase));
                    if (node is not null) this.Plugin.Ui.ShowInfo(node.Quest);
                }
            }
        }
    }
}
