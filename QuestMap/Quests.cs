using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Msagl.Core;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Action = Lumina.Excel.Sheets.Action;

namespace QuestMap {
    internal class Quests {
        private Plugin Plugin { get; }

        internal FrozenDictionary<uint, QuestNode> AllNodes { get; }
        internal FrozenDictionary<uint, QuestNode> ConsolidationNodes { get; }
        internal FrozenDictionary<uint, ImmutableArray<Item>> ItemRewards { get; }
        internal FrozenDictionary<uint, Emote> EmoteRewards { get; }
        internal FrozenDictionary<uint, Action> ActionRewards { get; }
        internal FrozenDictionary<uint, FrozenSet<ContentFinderCondition>> InstanceRewards { get; }
        internal FrozenDictionary<uint, BeastTribe> BeastRewards { get; }
        internal FrozenDictionary<uint, ClassJob> JobRewards { get; }
        private LayoutAlgorithmSettings LayoutSettings { get; } = new SugiyamaLayoutSettings();

        internal Quests(Plugin plugin) {
            this.Plugin = plugin;

            var excelItems = this.Plugin.DataManager.GetExcelSheet<Item>();

            var itemRewards = new Dictionary<uint, List<Item>>();
            var emoteRewards = new Dictionary<uint, Emote>();
            var actionRewards = new Dictionary<uint, Action>();
            var instanceRewards = new Dictionary<uint, HashSet<ContentFinderCondition>>();
            var beastRewards = new Dictionary<uint, BeastTribe>();
            var jobRewards = new Dictionary<uint, ClassJob>();
            var linkedInstances = new HashSet<ContentFinderCondition>();

            var allQuests = new Dictionary<uint, Quest>();
            foreach (var quest in this.Plugin.DataManager.GetExcelSheet<Quest>()) {
                if (quest.Name.ByteLength == 0 || quest.RowId == 65536) continue;
                allQuests[quest.RowId] = quest;

                if (quest.EmoteReward.RowId != 0) emoteRewards[quest.RowId] = quest.EmoteReward.Value!;

                ref var items = ref CollectionsMarshal.GetValueRefOrAddDefault(itemRewards, quest.RowId, out var _);
                items ??= [];

                // AG NOTE: .Reward used to be .ItemReward
                items.AddRange(quest.Reward.Where(row => row.RowId != 0).Select(row => excelItems.GetRowOrDefault(row.RowId)).Where(item => item is not null).Select(item => item!.Value));
                items.AddRange(quest.OptionalItemReward.Where(item => item.IsValid && item.RowId != 0).Select(item => item.Value));

                if (quest.ActionReward.IsValid && quest.ActionReward.RowId != 0) {
                    actionRewards[quest.RowId] = quest.ActionReward.Value;
                }

                var instances = this.InstanceUnlocks(quest, linkedInstances);
                if (instances.Count > 0) {
                    instanceRewards[quest.RowId] = instances;
                    foreach (var instance in instances) {
                        linkedInstances.Add(instance);
                    }
                }

                if (!quest.IsRepeatable && quest.BeastReputationRank.RowId == 0 && quest.BeastTribe.IsValid && quest.BeastTribe.RowId != 0) {
                    beastRewards[quest.RowId] = quest.BeastTribe.Value;
                }

                var jobReward = this.JobUnlocks(quest);
                if (jobReward != null) {
                    jobRewards[quest.RowId] = jobReward.Value;
                }
            }

            this.ItemRewards = itemRewards.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
            this.EmoteRewards = emoteRewards.ToFrozenDictionary();
            this.ActionRewards = actionRewards.ToFrozenDictionary();
            this.InstanceRewards = instanceRewards.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToFrozenSet());
            this.BeastRewards = beastRewards.ToFrozenDictionary();
            this.JobRewards = jobRewards.ToFrozenDictionary();

            var (_, nodes) = QuestNode.BuildTree(allQuests);
            this.AllNodes = nodes.ToFrozenDictionary();
            this.ConsolidationNodes = CreateMsqConsolidationNodes();
        }

        internal GraphWorker StartGraphRecalculation(Quest quest) {

            var cts = new CancellationTokenSource();
            var task = Task.Factory.StartNew(() => this.GetGraphInfo(quest, cts.Token), TaskCreationOptions.LongRunning);
            return new(task, cts);
        }

        private GraphInfo? GetGraphInfo(Quest quest, CancellationToken cancel) {
            var first = this.AllNodes[quest.RowId];
            var msaglNodes = new Dictionary<uint, Node>();
            var links = new List<(uint, uint)>();
            var g = new GeometryGraph();

            void AddNode(QuestNode node) {
                if (msaglNodes.ContainsKey(node.Id)) return;

                var dims = node.Dimensions;
                var graphNode = new Node(CurveFactory.CreateRectangle(dims.X, dims.Y, new Point()), node);
                g.Nodes.Add(graphNode);
                msaglNodes[node.Id] = graphNode;

                IEnumerable<QuestNode> parents;
                if (this.Plugin.Config.ShowRedundantArrows) {
                    parents = node.Parents;
                } else {
                    // only add if no *other* parent also shares
                    parents = node.Parents
                        .Where(q => {
                            return !node.Parents
                                .Where(other => other != q)
                                .Any(other => other.Parents.Contains(q));
                        });
                }

                foreach (var parent in parents) {
                    links.Add((parent.Id, node.Id));
                }
            }

            foreach (var node in first.Traverse()) {
                cancel.ThrowIfCancellationRequested();
                AddNode(node);
            }

            foreach (var node in first.Ancestors(this.ConsolidateMsq)) {
                cancel.ThrowIfCancellationRequested();
                AddNode(node);
            }

            foreach (var (sourceId, targetId) in links) {
                cancel.ThrowIfCancellationRequested();

                if (!msaglNodes.TryGetValue(sourceId, out var source) || !msaglNodes.TryGetValue(targetId, out var target)) {
                    continue;
                }

                var edge = new Edge(source, target);
                if (this.Plugin.Config.ShowArrowheads) {
                    edge.EdgeGeometry = new EdgeGeometry {
                        TargetArrowhead = new Arrowhead(),
                    };
                }

                g.Edges.Add(edge);
            }

            var msAglCancelToken = new CancelToken();
            using (var registration = cancel.Register(() => msAglCancelToken.Canceled = true))
            {
                LayoutHelpers.CalculateLayout(g, this.LayoutSettings, msAglCancelToken);
            }

            Node? centre = g.Nodes.FirstOrDefault();
            return new GraphInfo(g, centre);
        }

        private static FrozenDictionary<uint, QuestNode> CreateMsqConsolidationNodes()
        {
            var nodes = new List<QuestNode>()
            {
                new(70058, "A Realm Reborn (2.0)"),
                new(66729, "A Realm Awoken (2.1)"),
                new(66899, "Through the Maelstrom (2.2)"),
                new(66996, "Defenders of Eorzea (2.3)"),
                new(65625, "Dreams of Ice (2.4)"),
                new(65965, "Before the Fall - Part 1 (2.5)"),
                new(65964, "Before the Fall - Part 2 (2.55)"),
                new(67205, "Heavensward (3.0)"),
                new(67699, "As Goes Light, So Goes Darkness (3.1)"),
                new(67777, "The Gears of Change (3.2)"),
                new(67783, "Revenge of the Horde (3.3)"),
                new(67886, "Soul Surrender (3.4)"),
                new(67891, "The Far Edge of Fate - Part 1 (3.5)"),
                new(67895, "The Far Edge of Fate - Part 2 (3.56)"),
                new(68089, "Stormblood (4.0)"),
                new(68508, "The Legend Returns (4.1)"),
                new(68565, "Rise of a New Sun (4.2)"),
                new(68612, "Under the Moonlight (4.3)"),
                new(68685, "Prelude in Violet (4.4)"),
                new(68719, "A Requiem for Heroes - Part 1 (4.5)"),
                new(68721, "A Requiem for Heroes - Part 2 (4.56)"),
                new(69190, "Shadowbringers (5.0)"),
                new(69218, "Vows of Virtue, Deeds of Cruelty (5.1)"),
                new(69306, "Echoes of a Fallen Star (5.2)"),
                new(69318, "Reflections in Crystal (5.3)"),
                new(69552, "Futures Rewritten (5.4)"),
                new(69599, "Death Unto Dawn - Part 1 (5.5)"),
                new(69602, "Death Unto Dawn - Part 2 (5.55)"),
                new(70000, "Endwalker (6.0)"),
                new(70062, "Newfound Adventure (6.1)"),
                new(70136, "Buried Memory (6.2)"),
                new(70214, "Gods Revel, Lands Tremble (6.3)"),
                new(70279, "The Dark Throne (6.4)"),
                new(70286, "Growing Light (6.5)"),
                new(70289, "The Coming Dawn (6.55)"),
                new(70495, "Dawntrail (7.0)"),
                new(70786, "Crossroads (7.1)"),
                new(70842, "Seekers of Eternity (7.2)"),
            };
            return nodes.ToFrozenDictionary(node => node.Id);
        }

        private QuestNode? ConsolidateMsq(QuestNode quest) {
            if (!this.Plugin.Config.CondenseMsq) return null;
            return this.ConsolidationNodes.GetValueOrDefault(quest.Id);
        }

        private HashSet<ContentFinderCondition> InstanceUnlocks(Quest quest, HashSet<ContentFinderCondition> others) {
            if (quest.IsRepeatable) return [];
            var unlocks = new HashSet<ContentFinderCondition>();

            if (quest.InstanceContentUnlock.IsValid && quest.InstanceContentUnlock.RowId != 0) {
                var cfcs = this.Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()
                    .Where(cfc => cfc.Content.RowId == quest.InstanceContentUnlock.RowId && cfc.ContentLinkType == 1)
                    .Where(cfc => cfc.UnlockQuest.IsValid && cfc.UnlockQuest.RowId == 0);
                foreach (var cfc in cfcs) {
                    unlocks.Add(cfc);
                }
            }

            var questParams = quest.QuestParams.Select(param => (param.ScriptInstruction.ExtractText(), param.ScriptArg)).ToList();
            foreach (var (ins, arg) in questParams)
            {
                if (ins.StartsWith("INSTANCEDUNGEON"))
                {
                    var cfc = this.Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()!.FirstOrDefault(cfc => cfc.Content.RowId == arg && cfc.ContentLinkType == 1);
                    if (cfc.RowId == 0 || cfc.UnlockQuest.RowId != 0 || others.Contains(cfc)) continue;
                    if (questParams.Any(param => param.Item1 == "UNLOCK_ADD_NEW_CONTENT_TO_CF" || param.Item1.StartsWith("UNLOCK_DUNGEON")) && questParams.Any(param => param.Item1.StartsWith("LOC_ITEM")))
                        continue;
                    unlocks.Add(cfc);
                }
            }
            return unlocks;
        }

        private ClassJob? JobUnlocks(Quest quest) {
            if (quest.ClassJobUnlock.IsValid && quest.ClassJobUnlock.RowId != 0) {
                return quest.ClassJobUnlock.Value;
            }

            var questParams = quest.QuestParams.Select(param => (param.ScriptInstruction.ExtractText(), param.ScriptArg)).ToList();
            if (questParams.All(param => param.Item1.StartsWith("UNLOCK_IMAGE_CLASS"))) return null;
            var jobId = questParams.FirstOrDefault(param => param.Item1.StartsWith("CLASSJOB")).ScriptArg;
            return jobId == 0
                ? null
                : this.Plugin.DataManager.GetExcelSheet<ClassJob>()!.GetRow(jobId);
        }
    }
}
