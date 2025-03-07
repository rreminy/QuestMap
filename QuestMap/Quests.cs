﻿using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;
using Action = Lumina.Excel.Sheets.Action;

namespace QuestMap {
    internal class Quests {
        private Plugin Plugin { get; }

        private Dictionary<uint, Node<Quest>> AllNodes { get; }
        internal IReadOnlyDictionary<uint, List<Item>> ItemRewards { get; }
        internal IReadOnlyDictionary<uint, Emote> EmoteRewards { get; }
        internal IReadOnlyDictionary<uint, Action> ActionRewards { get; }
        internal IReadOnlyDictionary<uint, HashSet<ContentFinderCondition>> InstanceRewards { get; }
        internal IReadOnlyDictionary<uint, BeastTribe> BeastRewards { get; }
        internal IReadOnlyDictionary<uint, ClassJob> JobRewards { get; }
        private ChannelWriter<GraphInfo> GraphChannel { get; }
        private LayoutAlgorithmSettings LayoutSettings { get; } = new SugiyamaLayoutSettings();

        internal Quests(Plugin plugin, ChannelWriter<GraphInfo> graphChannel) {
            this.Plugin = plugin;
            this.GraphChannel = graphChannel;

            var itemRewards = new Dictionary<uint, List<Item>>();
            var emoteRewards = new Dictionary<uint, Emote>();
            var actionRewards = new Dictionary<uint, Action>();
            var instanceRewards = new Dictionary<uint, HashSet<ContentFinderCondition>>();
            var beastRewards = new Dictionary<uint, BeastTribe>();
            var jobRewards = new Dictionary<uint, ClassJob>();
            var linkedInstances = new HashSet<ContentFinderCondition>();

            var allQuests = new Dictionary<uint, Quest>();
            foreach (var quest in this.Plugin.DataManager.GetExcelSheet<Quest>()) {
                if (quest.Name.ByteLength == 0 || quest.RowId == 65536) {
                    continue;
                }

                allQuests[quest.RowId] = quest;

                if (quest.EmoteReward.RowId != 0) {
                    emoteRewards[quest.RowId] = quest.EmoteReward.Value!;
                }

                // AG TODO: Used to be .ItemReward
                foreach (var row in quest.Reward.Where(item => item.RowId != 0)) {
                    var item = this.Plugin.DataManager.GetExcelSheet<Item>().GetRowOrDefault(row.RowId);
                    if (item == null) {
                        continue;
                    }

                    List<Item> rewards;
                    if (itemRewards.TryGetValue(quest.RowId, out var items)) {
                        rewards = items;
                    } else {
                        rewards = [];
                        itemRewards[quest.RowId] = rewards;
                    }

                    rewards.Add(item.Value);
                }

                foreach (var row in quest.OptionalItemReward.Where(item => item.RowId != 0)) {
                    var item = row.Value;

                    List<Item> rewards;
                    if (itemRewards.TryGetValue(quest.RowId, out var items)) {
                        rewards = items;
                    } else {
                        rewards = [];
                        itemRewards[quest.RowId] = rewards;
                    }

                    rewards.Add(item!);
                }

                if (quest.ActionReward.RowId != 0) {
                    actionRewards[quest.RowId] = quest.ActionReward.Value!;
                }

                var instances = this.InstanceUnlocks(quest, linkedInstances);
                if (instances.Count > 0) {
                    instanceRewards[quest.RowId] = instances;
                    foreach (var instance in instances) {
                        linkedInstances.Add(instance);
                    }
                }

                if (quest.BeastTribe.RowId != 0 && !quest.IsRepeatable && quest.BeastReputationRank.RowId == 0) {
                    beastRewards[quest.RowId] = quest.BeastTribe.Value!;
                }

                var jobReward = this.JobUnlocks(quest);
                if (jobReward != null) {
                    jobRewards[quest.RowId] = jobReward.Value;
                }
            }

            this.ItemRewards = itemRewards;
            this.EmoteRewards = emoteRewards;
            this.ActionRewards = actionRewards;
            this.InstanceRewards = instanceRewards;
            this.BeastRewards = beastRewards;
            this.JobRewards = jobRewards;

            var (_, nodes) = Node<Quest>.BuildTree(allQuests);
            this.AllNodes = nodes;
        }

        private static readonly Vector2 TextOffset = new(5, 2);

        internal CancellationTokenSource StartGraphRecalculation(Quest quest) {
            var cts = new CancellationTokenSource();
            new Thread(async () => {
                var info = this.GetGraphInfo(quest, cts.Token);
                if (info != null) {
                    await this.GraphChannel.WriteAsync(info, cts.Token);
                }
            }).Start();

            return cts;
        }

        private GraphInfo? GetGraphInfo(Quest quest, CancellationToken cancel) {
            if (!this.AllNodes.TryGetValue(quest.RowId, out var first)) {
                return null;
            }

            var msaglNodes = new Dictionary<uint, Node>();
            var links = new List<(uint, uint)>();
            var g = new GeometryGraph();

            void AddNode(Node<Quest> node) {
                if (msaglNodes.ContainsKey(node.Id)) {
                    return;
                }

                var dims = ImGui.CalcTextSize(node.Value.Name.ToString()) + TextOffset * 2;
                var graphNode = new Node(CurveFactory.CreateRectangle(dims.X, dims.Y, new Point()), node.Value);
                g.Nodes.Add(graphNode);
                msaglNodes[node.Id] = graphNode;

                IEnumerable<Node<Quest>> parents;
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
                if (cancel.IsCancellationRequested) {
                    return null;
                }

                AddNode(node);
            }

            /* AG TODO: Reimplement this (MSQ Consolidation)
            foreach (var node in first.Ancestors(this.ConsolidateMsq)) {
                if (cancel.IsCancellationRequested) {
                    return null;
                }

                AddNode(node);
            }
            */

            foreach (var (sourceId, targetId) in links) {
                if (cancel.IsCancellationRequested) {
                    return null;
                }

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

            LayoutHelpers.CalculateLayout(g, this.LayoutSettings, null);

            Node? centre = null;
            if (g.Nodes.Count > 0) {
                centre = g.Nodes[0];
            }

            return cancel.IsCancellationRequested
                ? null
                : new GraphInfo(g, centre);
        }

        private Quest? ConsolidateMsq(Quest quest) {
            if (!this.Plugin.Config.CondenseMsq) {
                return null;
            }

            var name = quest.RowId switch {
                70058 => "A Realm Reborn (2.0)",
                66729 => "A Realm Awoken (2.1)",
                66899 => "Through the Maelstrom (2.2)",
                66996 => "Defenders of Eorzea (2.3)",
                65625 => "Dreams of Ice (2.4)",
                65965 => "Before the Fall - Part 1 (2.5)",
                65964 => "Before the Fall - Part 2 (2.55)",
                67205 => "Heavensward (3.0)",
                67699 => "As Goes Light, So Goes Darkness (3.1)",
                67777 => "The Gears of Change (3.2)",
                67783 => "Revenge of the Horde (3.3)",
                67886 => "Soul Surrender (3.4)",
                67891 => "The Far Edge of Fate - Part 1 (3.5)",
                67895 => "The Far Edge of Fate - Part 2 (3.56)",
                68089 => "Stormblood (4.0)",
                68508 => "The Legend Returns (4.1)",
                68565 => "Rise of a New Sun (4.2)",
                68612 => "Under the Moonlight (4.3)",
                68685 => "Prelude in Violet (4.4)",
                68719 => "A Requiem for Heroes - Part 1 (4.5)",
                68721 => "A Requiem for Heroes - Part 2 (4.56)",
                69190 => "Shadowbringers (5.0)",
                69218 => "Vows of Virtue, Deeds of Cruelty (5.1)",
                69306 => "Echoes of a Fallen Star (5.2)",
                69318 => "Reflections in Crystal (5.3)",
                69552 => "Futures Rewritten (5.4)",
                69599 => "Death Unto Dawn - Part 1 (5.5)",
                69602 => "Death Unto Dawn - Part 2 (5.55)",
                70000 => "Endwalker (6.0)",
                70062 => "Newfound Adventure (6.1)",
                70136 => "Buried Memory (6.2)",
                70214 => "Gods Revel, Lands Tremble (6.3)",
                70279 => "The Dark Throne (6.4)",
                70286 => "Growing Light (6.5)",
                70289 => "The Coming Dawn (6.55)",
                70495 => "Dawntrail (7.0)",
                _ => null,
            };

            if (name == null) {
                return null;
            }

            var newQuest = new Quest();
            foreach (var property in newQuest.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
                property.SetValue(newQuest, property.GetValue(quest));
            }

            //newQuest.Name = new Lumina.Text.SeString($"{name} MSQ"); // AG FIXME: Cannot be done
            return newQuest;
        }

        private HashSet<ContentFinderCondition> InstanceUnlocks(Quest quest, ICollection<ContentFinderCondition> others) {
            if (quest.IsRepeatable) {
                return [];
            }

            var unlocks = new HashSet<ContentFinderCondition>();

            if (quest.InstanceContentUnlock.RowId != 0) {
                var cfcs = this.Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()
                    .Where(cfc => cfc.Content.RowId == quest.InstanceContentUnlock.RowId && cfc.ContentLinkType == 1)
                    .Where(cfc => cfc.UnlockQuest.IsValid && cfc.UnlockQuest.RowId == 0);
                foreach (var cfc in cfcs) {
                    unlocks.Add(cfc);
                }
            }

            /* AG TODO: Reimplement this
            var instanceRefs = quest.ScriptInstruction
                .Zip(quest.ScriptArg, (ins, arg) => (ins, arg))
                .Where(x => x.ins.RawString.StartsWith("INSTANCEDUNGEON"));

            foreach (var reference in instanceRefs) {
                var key = reference.arg;

                // var content = this.Plugin.Interface.Data.GetExcelSheet<InstanceContent>().GetRow(key);

                var cfc = this.Plugin.DataManager.GetExcelSheet<ContentFinderCondition>()!.FirstOrDefault(cfc => cfc.Content == key && cfc.ContentLinkType == 1);
                if (cfc == null || cfc.UnlockQuest.Row != 0 || others.Contains(cfc)) {
                    continue;
                }

                if (!quest.ScriptInstruction.Any(i => i.RawString == "UNLOCK_ADD_NEW_CONTENT_TO_CF" || i.RawString.StartsWith("UNLOCK_DUNGEON"))) {
                    if (quest.ScriptInstruction.Any(i => i.RawString.StartsWith("LOC_ITEM"))) {
                        continue;
                    }
                }

                unlocks.Add(cfc);
            }
            */

            return unlocks;
        }

        private ClassJob? JobUnlocks(Quest quest) {
            if (quest.ClassJobUnlock.RowId > 0) {
                return quest.ClassJobUnlock.Value;
            }

            /* AG TODO: Reimplement this
            if (quest.ScriptInstruction.All(ins => ins.RawString.StartsWith("UNLOCK_IMAGE_CLASS"))) {
                return null;
            }

            var jobId = quest.ScriptInstruction
                .Zip(quest.ScriptArg, (ins, arg) => (ins, arg))
                .FirstOrDefault(entry => entry.ins.RawString.StartsWith("CLASSJOB"))
                .arg;
            return jobId == 0
                ? null
                : this.Plugin.DataManager.GetExcelSheet<ClassJob>()!.GetRow(jobId);
            */
            return null;
        }
    }
}
