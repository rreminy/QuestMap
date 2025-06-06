using Dalamud.Plugin.Ipc;
using System;

namespace QuestMap
{
    internal sealed class Ipc : IDisposable
    {
        private readonly ICallGateProvider<uint, bool> _showGraphByQuestIdIpc;
        private readonly ICallGateProvider<uint, bool> _showInfoByQuestIdIpc;

        private Plugin Plugin { get; }

        public Ipc(Plugin plugin)
        {
            this.Plugin = plugin;

            this._showGraphByQuestIdIpc = this.Plugin.Interface.GetIpcProvider<uint, bool>("QuestMap.ShowGraphByQuestId");
            this._showGraphByQuestIdIpc.RegisterFunc(this.ShowGraphByQuestId);

            this._showInfoByQuestIdIpc = this.Plugin.Interface.GetIpcProvider<uint, bool>("QuestMap.ShowInfoByQuestId");
            this._showInfoByQuestIdIpc.RegisterFunc(this.ShowInfoByQuestId);
        }

        private bool ShowGraphByQuestId(uint questId) => this.Plugin.Ui.ShowQuest(questId);
        private bool ShowInfoByQuestId(uint questId) => this.Plugin.Ui.ShowInfo(questId);

        public void Dispose()
        {
            this._showGraphByQuestIdIpc.UnregisterFunc();
            this._showInfoByQuestIdIpc.UnregisterFunc();
        }
    }
}
