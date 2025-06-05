using Dalamud.Plugin.Ipc;
using System;

namespace QuestMap
{
    internal sealed class Ipc : IDisposable
    {
        private readonly ICallGateProvider<uint, bool> _showGraphByQuestIdIpc;

        private Plugin Plugin { get; }

        public Ipc(Plugin plugin)
        {
            this.Plugin = plugin;

            this._showGraphByQuestIdIpc = this.Plugin.Interface.GetIpcProvider<uint, bool>("QuestMap.ShowGraphByQuestId");
            this._showGraphByQuestIdIpc.RegisterFunc(this.ShowGraphByQuestId);
        }

        private bool ShowGraphByQuestId(uint questId) => this.Plugin.Ui.ShowQuest(questId);

        public void Dispose()
        {
            this._showGraphByQuestIdIpc.UnregisterFunc();
        }
    }
}
