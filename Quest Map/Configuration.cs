using System;
using Dalamud.Configuration;

namespace QuestMap {
    [Serializable]
    internal class Configuration : IPluginConfiguration {
        public int Version { get; set; } = 1;

        public bool ShowCompleted;
        public bool ShowSeasonal;
        public bool ShowArrowheads;
        public bool CondenseMsq;

        public Visibility EmoteVis;
        public Visibility ItemVis;
        public Visibility MinionVis;
        public Visibility ActionsVis;
        public Visibility InstanceVis;
        public Visibility TribeVis;
        public Visibility JobVis;
    }
}
