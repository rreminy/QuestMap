using Microsoft.Msagl.Core.Layout;

namespace QuestMap {
    internal class GraphInfo {
        internal GeometryGraph Graph { get; }
        internal Node? Centre { get; }

        internal GraphInfo(GeometryGraph graph, Node? centre) {
            this.Graph = graph;
            this.Centre = centre;
        }
    }
}
