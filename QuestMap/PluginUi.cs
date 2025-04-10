using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using static Dalamud.Interface.Utility.Raii.ImRaii;

namespace QuestMap {
    internal class PluginUi : IDisposable {
        private static class Colours {
            internal static readonly uint Bg = ImGui.GetColorU32(new Vector4(0.13f, 0.13f, 0.13f, 1));
            internal static readonly uint Bg2 = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1));
            internal static readonly uint Text = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1));
            internal static readonly uint Line = ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1));
            internal static readonly uint Grid = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1));

            internal static readonly Vector4 NormalQuest = new(0.54f, 0.45f, 0.36f, 1);
            internal static readonly Vector4 MsqQuest = new(0.29f, 0.35f, 0.44f, 1);
            internal static readonly Vector4 BlueQuest = new(0.024F, 0.016f, 0.72f, 1);
        }

        private Plugin Plugin { get; }

        private string _filter = string.Empty;
        private Quest? Quest { get; set; }
        private GraphWorker? Worker { get; set; }
        private HashSet<uint> InfoWindows { get; } = [];
        private List<(QuestNode, bool, string)> FilteredQuests { get; } = [];

        internal bool Show;

        private bool _relayout;
        private bool _recenter;
        private Vector2 _offset = Vector2.Zero;
        private static readonly Vector2 TextOffset = new(5, 2);
        private const int GridSmall = 10;
        private const int GridLarge = 50;
        private bool _viewDrag;
        private Vector2 _lastDragPos;

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            this.Refilter();

            this.Plugin.Interface.UiBuilder.Draw += this.Draw;
            this.Plugin.Interface.UiBuilder.OpenConfigUi += this.OpenConfig;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.OpenConfigUi -= this.OpenConfig;
            this.Plugin.Interface.UiBuilder.Draw -= this.Draw;
        }

        private void OpenConfig() {
            this.Show = true;
        }

        private unsafe void Refilter() {
            this.FilteredQuests.Clear();

            var filter = this._filter;
            var filtered = this.Plugin.Quests.AllNodes.Values
                .Where(questNode => {
                    var quest = questNode.Quest;

                    if (string.IsNullOrEmpty(questNode.Name)) {
                        return false;
                    }

                    if (!this.Plugin.Config.ShowSeasonal && quest.Festival.RowId != 0) {
                        return false;
                    }

                    var completed = QuestManager.IsQuestComplete(quest.RowId);
                    if (!this.Plugin.Config.ShowCompleted && completed) {
                        return false;
                    }

                    if (this.Plugin.Config.EmoteVis == Visibility.Only && !this.Plugin.Quests.EmoteRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this.Plugin.Config.ItemVis == Visibility.Only && !this.Plugin.Quests.ItemRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this.Plugin.Config.MinionVis == Visibility.Only) {
                        if (!this.Plugin.Quests.ItemRewards.TryGetValue(quest.RowId, out var items)) {
                            return false;
                        }

                        if (items.All(item => item.ItemUICategory.RowId != 81)) {
                            return false;
                        }
                    }

                    if (this.Plugin.Config.ActionsVis == Visibility.Only && !this.Plugin.Quests.ActionRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this.Plugin.Config.InstanceVis == Visibility.Only && !this.Plugin.Quests.InstanceRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this.Plugin.Config.TribeVis == Visibility.Only && !this.Plugin.Quests.BeastRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this.Plugin.Config.JobVis == Visibility.Only && !this.Plugin.Quests.JobRewards.ContainsKey(quest.RowId)) {
                        return false;
                    }

                    if (this._filter.Length == 0) {
                        return true;
                    }

                    return questNode.Name.ToLowerInvariant().Contains(filter, StringComparison.InvariantCultureIgnoreCase)
                           || this.Plugin.Quests.ItemRewards.TryGetValue(quest.RowId, out var items1) && items1.Any(item => item.Name.ToString().Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                           || this.Plugin.Quests.EmoteRewards.TryGetValue(quest.RowId, out var emote) && emote.Name.ExtractText().Contains(filter, StringComparison.InvariantCultureIgnoreCase)
                           || this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action) && action.Name.ExtractText().Contains(filter, StringComparison.InvariantCultureIgnoreCase)
                           || this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances) && instances.Any(instance => instance.Name.ExtractText().Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                           || this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe) && tribe.Name.ExtractText().Contains(filter, StringComparison.InvariantCultureIgnoreCase)
                           || this.Plugin.Quests.JobRewards.TryGetValue(quest.RowId, out var job) && job.Name.ExtractText().Contains(filter, StringComparison.InvariantCultureIgnoreCase);
                })
                .SelectMany(questNode => {
                    var quest = questNode.Quest;

                    var drawItems = new List<(QuestNode, bool, string)> {
                        (questNode, false, $"{questNode.Name}##{quest.Id}"),
                    };

                    var allItems = this.Plugin.Config.ItemVis != Visibility.Hidden;
                    var anyItemVisible = allItems || this.Plugin.Config.MinionVis != Visibility.Hidden;
                    if (anyItemVisible && this.Plugin.Quests.ItemRewards.TryGetValue(questNode.Id, out var items)) {
                        var toShow = items.Where(item => allItems || item.ItemUICategory.RowId == 81);
                        drawItems.AddRange(toShow.Select(item => (questNode, true, $"{this.Convert(item.Name)}##item-{questNode.Id}-{item.RowId}")));
                    }

                    if (this.Plugin.Config.EmoteVis != Visibility.Hidden && this.Plugin.Quests.EmoteRewards.TryGetValue(questNode.Id, out var emote)) {
                        drawItems.Add((questNode, true, $"{this.Convert(emote.Name)}##emote-{quest.RowId}-{emote.RowId}"));
                    }

                    if (this.Plugin.Config.ActionsVis != Visibility.Hidden && this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action)) {
                        drawItems.Add((questNode, true, $"{this.Convert(action.Name)}##action-{quest.RowId}-{action.RowId}"));
                    }

                    if (this.Plugin.Config.InstanceVis != Visibility.Hidden && this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances)) {
                        drawItems.AddRange(instances.Select(instance => (questNode, true, $"{this.Convert(instance.Name)}##instance-{quest.RowId}-{instance.RowId}")));
                    }

                    if (this.Plugin.Config.TribeVis != Visibility.Hidden && this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe)) {
                        drawItems.Add((questNode, true, $"{this.Convert(tribe.Name)}##tribe-{quest.RowId}-{tribe.RowId}"));
                    }

                    if (this.Plugin.Config.JobVis != Visibility.Hidden && this.Plugin.Quests.JobRewards.TryGetValue(quest.RowId, out var job)) {
                        drawItems.Add((questNode, true, $"{this.Convert(job.Name)}##job-{quest.RowId}-{job.RowId}"));
                    }

                    return drawItems;
                });
            this.FilteredQuests.AddRange(filtered);
        }

        private void Draw() {
            this.CalculateAllTextDimensions();
            this.DrawInfoWindows();
            this.DrawMainWindow();
        }

        private void CalculateAllTextDimensions()
        {
            foreach (var node in this.Plugin.Quests.AllNodes.Values)
            {
                if (node.Dimensions != default) return;
                node.Dimensions = ImGui.CalcTextSize(node.Name) + TextOffset * 2;
            }

            foreach (var node in this.Plugin.Quests.ConsolidationNodes.Values)
            {
                if (node.Dimensions != default) return;
                node.Dimensions = ImGui.CalcTextSize(node.Name) + TextOffset * 2;
            }
        }

        private unsafe void DrawMainWindow() {
            if (!this.Show) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(675, 600), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(Plugin.Name, ref this.Show, ImGuiWindowFlags.MenuBar)) {
                ImGui.End();
                return;
            }

            if (ImGui.BeginMenuBar()) {
                if (ImGui.BeginMenu("Options")) {
                    var anyChanged = false;

                    if (ImGui.BeginMenu("Quest list")) {
                        anyChanged |= ImGui.MenuItem("Show completed quests", null, ref this.Plugin.Config.ShowCompleted);
                        anyChanged |= ImGui.MenuItem("Show seasonal quests", null, ref this.Plugin.Config.ShowSeasonal);

                        ImGui.EndMenu();
                    }

                    if (ImGui.BeginMenu("Quest map")) {
                        if (ImGui.MenuItem("Show arrowheads", null, ref this.Plugin.Config.ShowArrowheads)) {
                            this._relayout = true;
                            anyChanged = true;
                        }

                        if (ImGui.MenuItem("Condense final MSQ quests", null, ref this.Plugin.Config.CondenseMsq)) {
                            this._relayout = true;
                            anyChanged = true;
                        }

                        if (ImGui.MenuItem("Show redundant arrows", null, ref this.Plugin.Config.ShowRedundantArrows)) {
                            this._relayout = true;
                            anyChanged = true;
                        }

                        ImGui.EndMenu();
                    }

                    void VisibilityItem(string name, string id, ref Visibility visibility) {
                        if (!ImGui.BeginMenu(name)) {
                            return;
                        }

                        foreach (var vis in (Visibility[]) Enum.GetValues(typeof(Visibility))) {
                            if (!ImGui.MenuItem($"{vis}##{id}", null, visibility == vis)) {
                                continue;
                            }

                            visibility = vis;
                            anyChanged = true;
                        }

                        ImGui.EndMenu();
                    }

                    if (ImGui.BeginMenu("Reward/unlock visibility")) {
                        VisibilityItem("Emotes", "emote-vis", ref this.Plugin.Config.EmoteVis);
                        VisibilityItem("Items", "item-vis", ref this.Plugin.Config.ItemVis);
                        VisibilityItem("Minions", "minion-vis", ref this.Plugin.Config.MinionVis);
                        VisibilityItem("Actions", "action-vis", ref this.Plugin.Config.ActionsVis);
                        VisibilityItem("Instances", "instance-vis", ref this.Plugin.Config.InstanceVis);
                        VisibilityItem("Beast tribes", "tribe-vis", ref this.Plugin.Config.TribeVis);
                        VisibilityItem("Jobs", "job-vis", ref this.Plugin.Config.JobVis);

                        ImGui.EndMenu();
                    }

                    if (anyChanged) {
                        this.Plugin.SaveConfig();
                        this.Refilter();
                    }

                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            if (ImGui.InputText("Filter", ref this._filter, 100)) {
                this.Refilter();
            }

            if (ImGui.BeginChild("quest-list", new Vector2(ImGui.GetContentRegionAvail().X * .25f, -1), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                ImGuiListClipperPtr clipper;
                unsafe {
                    clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                }

                clipper.Begin(this.FilteredQuests.Count);
                while (clipper.Step()) {
                    for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++) {
                        var (questNode, indent, drawItem) = this.FilteredQuests[row];
                        var quest = questNode.Quest;

                        void DrawSelectable(string name, Quest quest) {
                            var completed = QuestManager.IsQuestComplete(quest.RowId);
                            if (completed) {
                                var disabled = *ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled);
                                ImGui.PushStyleColor(ImGuiCol.Text, disabled);
                            }

                            var ret = ImGui.Selectable(name, this.Quest is not null && this.Quest.Value.RowId == quest.RowId);

                            if (completed) {
                                ImGui.PopStyleColor();
                            }

                            if (!ret) {
                                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                                    this.InfoWindows.Add(quest.RowId);
                                }

                                return;
                            }

                            this.Quest = quest;
                            this._relayout = true;
                        }

                        if (indent) {
                            ImGui.TreePush();
                        }

                        DrawSelectable(drawItem, quest);

                        if (indent) {
                            ImGui.TreePop();
                        }
                    }
                }

                clipper.Destroy();

                ImGui.EndChild();
            }

            ImGui.SameLine();
            if (ImGui.BeginChild("quest-map", new Vector2(-1, -1))) {
                if (this.Quest is null) ImGui.TextUnformatted("No quest selected");
                else if (this.Worker is null || !this.Worker.Task.IsCompleted) ImGui.TextUnformatted("Generating map...");
                else if (this.Worker.Task.IsCompleted)
                {
                    var task = this.Worker.Task;
                    if (!task.IsCompletedSuccessfully)
                    {
                        var exception = task.Exception?.Flatten().ToString();
                        if (exception is null) ImGui.TextUnformatted("Task did not complete successfully");
                        else ImGui.TextUnformatted(exception.ToString());
                    }
                    else
                    {
                        var graph = task.Result;
                        if (graph is null) ImGui.TextUnformatted("Task did not complete successfully (null)");
                        else this.DrawGraph(graph);
                    }
                }
                ImGui.EndChild();
            }

            if (this._relayout && this.Quest != null) {
                var oldWorker = this.Worker;
                this.Worker = this.Plugin.Quests.StartGraphRecalculation(this.Quest.Value);
                oldWorker?.Dispose();
                this._relayout = false;
                this._recenter = true;
            }

            ImGui.End();
        }

        private void DrawInfoWindows() {
            var remove = 0u;

            foreach (var id in this.InfoWindows) {
                var quest = this.Plugin.DataManager.GetExcelSheet<Quest>().GetRowOrDefault(id);
                if (quest == null) {
                    continue;
                }

                if (this.DrawInfoWindow(quest.Value)) {
                    remove = id;
                }
            }

            if (remove > 0) {
                this.InfoWindows.Remove(remove);
            }
        }

        /// <returns>true if closing</returns>
        private bool DrawInfoWindow(Quest quest) {
            var open = true;
            if (!ImGui.Begin($"{this.Convert(quest.Name)}##{quest.RowId}", ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.End();
                return !open;
            }

            var completed = QuestManager.IsQuestComplete(quest.RowId);

            ImGui.TextUnformatted($"Level: {quest.ClassJobLevel[0]}");

            if (completed) {
                ImGui.PushFont(UiBuilder.IconFont);
                var check = FontAwesomeIcon.Check.ToIconString();
                var width = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(check).X;
                ImGui.SameLine(width);
                ImGui.TextUnformatted(check);
                ImGui.PopFont();
            }

            IDalamudTextureWrap? GetIcon(uint id) {
                return this.Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrDefault();
            }

            var textWrap = ImGui.GetFontSize() * 20f;

            if (quest.Icon != 0) {
                var header = GetIcon(quest.Icon);
                if (header != null) {
                    textWrap = header.Width /2f;
                    ImGui.Image(header.ImGuiHandle, new Vector2(header.Width / 2f, header.Height / 2f));
                }
            }

            var rewards = new List<string>();
            var paramGrow = this.Plugin.DataManager.GetExcelSheet<ParamGrow>().GetRowOrDefault(quest.ClassJobLevel[0]);
            var xp = 0;
            if (paramGrow != null) {
                xp = quest.ExpFactor * paramGrow.Value.ScaledQuestXP * paramGrow.Value.QuestExpModifier / 100;
            }

            if (xp > 0) {
                rewards.Add($"Exp: {xp:N0}");
            }

            if (quest.GilReward > 0) {
                rewards.Add($"Gil: {quest.GilReward:N0}");
            }

            if (rewards.Count > 0) {
                ImGui.TextUnformatted(string.Join(" / ", rewards));
            }

            ImGui.Separator();

            void DrawItemRewards(string label, IEnumerable<(SeString name, uint icon, byte qty)> enumerable) {
                var items = enumerable.ToArray();
                if (items.Length == 0) {
                    return;
                }

                ImGui.TextUnformatted(label);

                var maxHeight = items
                    .Select(entry => GetIcon(entry.icon))
                    .Select(image => image?.Height ?? 0)
                    .Max(height => height / 2f);

                var originalY = ImGui.GetCursorPosY();
                foreach (var (name, icon, qty) in items) {
                    var image = GetIcon(icon);
                    if (image != null) {
                        if (image.Height < maxHeight) {
                            ImGui.SetCursorPosY(originalY + (maxHeight - image.Height) / 2f);
                        }

                        ImGui.Image(image.ImGuiHandle, new Vector2(image.Width / 2f, image.Height / 2f));
                        Util.Tooltip(name.ToString());
                    }

                    if (qty > 1) {
                        var oldSpacing = ImGui.GetStyle().ItemSpacing;
                        ImGui.GetStyle().ItemSpacing = new Vector2(2, 0);
                        ImGui.SameLine();
                        var qtyLabel = $"x{qty}";
                        var labelSize = ImGui.CalcTextSize(qtyLabel);
                        ImGui.SetCursorPosY(originalY + (maxHeight - labelSize.Y) / 2f);
                        ImGui.TextUnformatted(qtyLabel);
                        ImGui.GetStyle().ItemSpacing = oldSpacing;
                    }

                    ImGui.SameLine();
                    ImGui.SetCursorPosY(originalY);
                }

                ImGui.Dummy(Vector2.Zero);
                ImGui.Separator();
            }

            var additionalRewards = new List<(SeString name, uint icon, byte qty)>();
            if (this.Plugin.Quests.JobRewards.TryGetValue(quest.RowId, out var job)) {
                // FIXME: figure out better way to find icon
                additionalRewards.Add((this.Convert(job.Name).ToString(), 62000 + job.RowId, 1));
            }

            for (var i = 0; i < quest.ItemCatalyst.Count; i++) {
                var catalyst = quest.ItemCatalyst[i];
                var amount = quest.ItemCountCatalyst[i];

                if (catalyst.RowId != 0) {
                    additionalRewards.Add((this.Convert(catalyst.Value!.Name), catalyst.Value.Icon, amount));
                }
            }

            foreach (var generalAction in quest.GeneralActionReward.Where(row => row.RowId != 0)) {
                additionalRewards.Add((this.Convert(generalAction.Value!.Name), (uint) generalAction.Value.Icon, 1));
            }

            if (this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action)) {
                additionalRewards.Add((this.Convert(action.Name), action.Icon, 1));
            }

            if (this.Plugin.Quests.EmoteRewards.TryGetValue(quest.RowId, out var emote)) {
                additionalRewards.Add((this.Convert(emote.Name), emote.Icon, 1));
            }

            if (quest.OtherReward.RowId != 0) {
                additionalRewards.Add((this.Convert(quest.OtherReward.Value!.Name), quest.OtherReward.Value.Icon, 1));
            }

            if (quest.ReputationReward > 0) {
                var beastTribe = quest.BeastTribe.ValueNullable;
                if (beastTribe != null) {
                    additionalRewards.Add((this.Convert(beastTribe.Value.NameRelation), beastTribe.Value.Icon, quest.ReputationReward));
                }
            }

            if (quest.TomestoneReward > 0) {
                var tomestone = this.Plugin.DataManager.GetExcelSheet<TomestonesItem>().FirstOrDefault(row => row.Tomestones.RowId == quest.TomestoneReward);
                if (tomestone.Item.IsValid) {
                    var item = tomestone.Item.Value;
                    additionalRewards.Add((this.Convert(item.Name), item.Icon, quest.TomestoneCountReward));
                }
            }

            if (quest.ItemRewardType is 0 or 1 or 3 or 5) {
                DrawItemRewards(
                    "Rewards",
                    quest.Reward // AG TODO: Originally .ItemReward. There are multiple "Reward" properties in quest and therefore figuring out may be needed
                        .Zip(quest.ItemCountReward, (id, qty) => (id, qty))
                        .Where(entry => entry.id.RowId != 0)
                        .Select(entry => (item: this.Plugin.DataManager.GetExcelSheet<Item>().GetRowOrDefault(entry.id.RowId), entry.qty))
                        .Where(entry => entry.item != null)
                        .Select(entry => (this.Convert(entry.item!.Value.Name), (uint) entry.item!.Value.Icon, entry.qty))
                        .Concat(additionalRewards)
                );

                DrawItemRewards(
                    "Optional rewards",
                    quest.OptionalItemReward
                        .Zip(quest.OptionalItemCountReward, (row, qty) => (row, qty))
                        .Where(entry => entry.row.RowId != 0)
                        .Select(entry => (item: entry.row.ValueNullable, entry.qty))
                        .Where(entry => entry.item != null)
                        .Select(entry => (this.Convert(entry.item!.Value.Name), (uint) entry.item!.Value.Icon, entry.qty))
                );
            }

            if (this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances)) {
                ImGui.TextUnformatted("Instances");

                foreach (var instance in instances.Where(instance => instance.ContentType.IsValid)) {
                    var icon = instance.ContentType.Value.Icon;
                    if (icon > 0) {
                        var image = GetIcon(icon);
                        if (image != null) {
                            ImGui.Image(image.ImGuiHandle, new Vector2(image.Width / 2f, image.Height / 2f));
                            Util.Tooltip(this.Convert(instance.Name).ToString());
                        }
                    } else {
                        ImGui.TextUnformatted(this.Convert(instance.Name).ToString());
                    }

                    ImGui.SameLine();
                }

                ImGui.Dummy(Vector2.Zero);

                ImGui.Separator();
            }

            if (this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe)) {
                ImGui.TextUnformatted("Beast tribe");

                var image = GetIcon(tribe.Icon);
                if (image != null) {
                    ImGui.Image(image.ImGuiHandle, new Vector2(image.Width / 2f, image.Height / 2f));
                    Util.Tooltip(this.Convert(tribe.Name).ToString());
                }
                ImGui.Separator();
            }

            var id = quest.RowId & 0xFFFF;
            var path = $"quest/{id.ToString("00000")[..3]}/{quest.Id.ExtractText().ToLowerInvariant()}";
            var sheet = this.Plugin.DataManager.GetExcelSheet<RawRow>(null, path);
            if (sheet is not null)
            {
                var text = sheet.GetRow(0).ReadStringColumn(1).ExtractText();
                ImGui.PushTextWrapPos(textWrap);
                ImGui.TextUnformatted(text);
                ImGui.PopTextWrapPos();
            }

            ImGui.Separator();

            void OpenMap(Level? level) {
                if (level == null) {
                    return;
                }

                var mapLink = new MapLinkPayload(
                    level.Value.Territory.RowId,
                    level.Value.Map.RowId,
                    (int) (level.Value.X * 1_000f),
                    (int) (level.Value.Z * 1_000f)
                );

                this.Plugin.GameGui.OpenMapWithMapLink(mapLink);
            }

            var issuer = this.Plugin.DataManager.GetExcelSheet<ENpcResident>().GetRowOrDefault(quest.IssuerStart.RowId)?.Singular.ExtractText() ?? "Unknown";
            var target = this.Plugin.DataManager.GetExcelSheet<ENpcResident>().GetRowOrDefault(quest.TargetEnd.RowId)?.Singular.ExtractText() ?? "Unknown";
            ImGui.TextUnformatted(issuer);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SameLine();
            ImGui.TextUnformatted(FontAwesomeIcon.ArrowRight.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextUnformatted(target);

            ImGui.Separator();

            if (Util.IconButton(FontAwesomeIcon.MapMarkerAlt)) {
                OpenMap(quest.IssuerLocation.Value);
            }

            Util.Tooltip("Mark issuer on map");

            ImGui.SameLine();
            if (Util.IconButton(FontAwesomeIcon.Book)) {
                unsafe {
                    AgentQuestJournal.Instance()->OpenForQuest(quest.RowId & 0xFFFF, 1);
                }
            }

            Util.Tooltip("Open quest in Journal");

            ImGui.SameLine();
            if (Util.IconButton(FontAwesomeIcon.ProjectDiagram)) {
                this.Quest = quest;
                this._relayout = true;
            }

            Util.Tooltip("Show quest graph");

            ImGui.End();
            return !open;
        }

        private static Vector2 ConvertPoint(Point p) {
            return new((float) p.X, (float) p.Y);
        }

        private Vector2 GetTopLeft(GeometryObject item) {
            // imgui measures from top left as 0,0
            return ConvertPoint(item.BoundingBox.RightTop) + this._offset;
        }

        private Vector2 GetBottomRight(GeometryObject item) {
            return ConvertPoint(item.BoundingBox.LeftBottom) + this._offset;
        }

        private void DrawGraph(GraphInfo info) {
            var graph = info.Graph;

            // now the fun (tm) begins
            var space = ImGui.GetContentRegionAvail();
            var size = new Vector2(space.X, space.Y);
            var drawList = ImGui.GetWindowDrawList();

            ImGui.BeginGroup();

            ImGui.InvisibleButton("##NodeEmpty", size);
            var canvasTopLeft = ImGui.GetItemRectMin();
            var canvasBottomRight = ImGui.GetItemRectMax();

            if (this._recenter && info.Centre is not null) {
                this._offset = ConvertPoint(info.Centre.Center) * -1 + (canvasBottomRight - canvasTopLeft) / 2;
                this._recenter = false;
            }

            drawList.PushClipRect(canvasTopLeft, canvasBottomRight, true);

            drawList.AddRectFilled(canvasTopLeft, canvasBottomRight, Colours.Bg);
            // ========= GRID =========
            for (var i = 0; i < size.X / GridSmall; i++) {
                drawList.AddLine(new Vector2(canvasTopLeft.X + i * GridSmall, canvasTopLeft.Y), new Vector2(canvasTopLeft.X + i * GridSmall, canvasBottomRight.Y), Colours.Grid, 1.0f);
            }

            for (var i = 0; i < size.Y / GridSmall; i++) {
                drawList.AddLine(new Vector2(canvasTopLeft.X, canvasTopLeft.Y + i * GridSmall), new Vector2(canvasBottomRight.X, canvasTopLeft.Y + i * GridSmall), Colours.Grid, 1.0f);
            }

            for (var i = 0; i < size.X / GridLarge; i++) {
                drawList.AddLine(new Vector2(canvasTopLeft.X + i * GridLarge, canvasTopLeft.Y), new Vector2(canvasTopLeft.X + i * GridLarge, canvasBottomRight.Y), Colours.Grid, 2.0f);
            }

            for (var i = 0; i < size.Y / GridLarge; i++) {
                drawList.AddLine(new Vector2(canvasTopLeft.X, canvasTopLeft.Y + i * GridLarge), new Vector2(canvasBottomRight.X, canvasTopLeft.Y + i * GridLarge), Colours.Grid, 2.0f);
            }

            drawList.AddRect(canvasTopLeft, canvasBottomRight, Colours.Bg2);

            Vector2 ConvertDrawPoint(Point p) {
                var ret = canvasBottomRight - (ConvertPoint(p) + this._offset);
                return ret;
            }

            foreach (var edge in graph.Edges) {
                var start = canvasBottomRight - this.GetTopLeft(edge);
                if (IsHidden(edge, start)) {
                    continue;
                }

                var curve = edge.Curve;
                switch (curve) {
                    case Curve c: {
                        foreach (var s in c.Segments) {
                            switch (s) {
                                case LineSegment l:
                                    drawList.AddLine(
                                        ConvertDrawPoint(l.Start),
                                        ConvertDrawPoint(l.End),
                                        Colours.Line,
                                        3.0f
                                    );
                                    break;
                                case CubicBezierSegment cs:
                                    drawList.AddBezierCubic(
                                        ConvertDrawPoint(cs.B(0)),
                                        ConvertDrawPoint(cs.B(1)),
                                        ConvertDrawPoint(cs.B(2)),
                                        ConvertDrawPoint(cs.B(3)),
                                        Colours.Line,
                                        3.0f
                                    );
                                    break;
                            }
                        }

                        break;
                    }
                    case LineSegment l:
                        drawList.AddLine(
                            ConvertDrawPoint(l.Start),
                            ConvertDrawPoint(l.End),
                            Colours.Line,
                            3.0f
                        );
                        break;
                }

                void DrawArrow(Vector2 start, Vector2 end) {
                    const float arrowAngle = 30f;
                    var dir = end - start;
                    var h = dir;
                    dir /= dir.Length();

                    var s = new Vector2(-dir.Y, dir.X);
                    s *= (float) (h.Length() * Math.Tan(arrowAngle * 0.5f * (Math.PI / 180f)));

                    drawList.AddTriangleFilled(
                        start + s,
                        end,
                        start - s,
                        Colours.Line
                    );
                }

                if (edge.ArrowheadAtTarget) {
                    DrawArrow(
                        ConvertDrawPoint(edge.Curve.End),
                        ConvertDrawPoint(edge.EdgeGeometry.TargetArrowhead.TipPosition)
                    );
                }

                if (edge.ArrowheadAtSource) {
                    DrawArrow(
                        ConvertDrawPoint(edge.Curve.Start),
                        ConvertDrawPoint(edge.EdgeGeometry.SourceArrowhead.TipPosition)
                    );
                }
            }

            bool IsHidden(GeometryObject node, Vector2 start) {
                var width = (float) node.BoundingBox.Width;
                var height = (float) node.BoundingBox.Height;
                return start.X + width < canvasTopLeft.X
                       || start.Y + height < canvasTopLeft.Y
                       || start.X > canvasBottomRight.X
                       || start.Y > canvasBottomRight.Y;
            }

            var drawn = new List<(Vector2, Vector2, uint)>();

            foreach (var node in graph.Nodes) {
                var start = canvasBottomRight - this.GetTopLeft(node);

                if (IsHidden(node, start)) {
                    continue;
                }

                var questNode = (QuestNode)node.UserData;
                var quest = questNode.Quest;

                var colour = quest.Equals(default(Quest)) ? Colours.MsqQuest : quest.EventIconType.RowId switch {
                    1 => Colours.NormalQuest, // normal
                    3 => Colours.MsqQuest, // msq
                    8 => Colours.BlueQuest, // blue
                    10 => Colours.BlueQuest, // also blue
                    _ => Colours.NormalQuest,
                };
                var textColour = Colours.Text;

                var completed = QuestManager.IsQuestComplete(questNode.Id);
                if (completed) {
                    colour.W = .5f;
                    textColour = (uint) ((0x80 << 24) | (textColour & 0xFFFFFF));
                }

                var end = canvasBottomRight - this.GetBottomRight(node);

                drawn.Add((start, end, questNode.Id));

                if (this.Quest is not null && quest.RowId == this.Quest.Value.RowId) {
                    drawList.AddRect(start - Vector2.One, end + Vector2.One, Colours.Line, 5, ImDrawFlags.RoundCornersAll);
                }

                drawList.AddRectFilled(start, end, ImGui.GetColorU32(colour), 5, ImDrawFlags.RoundCornersAll);
                drawList.AddText(start + TextOffset, textColour, questNode.Name);
            }

            // HOW ABOUT DRAGGING THE VIEW?
            if (ImGui.IsItemActive()) {
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left)) {
                    var d = ImGui.GetMouseDragDelta();
                    if (this._viewDrag) {
                        var delta = d - this._lastDragPos;
                        this._offset -= delta;
                    }

                    this._viewDrag = true;
                    this._lastDragPos = d;
                } else {
                    this._viewDrag = false;
                }
            } else {
                if (!this._viewDrag) {
                    var left = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
                    var middle = ImGui.IsMouseReleased(ImGuiMouseButton.Middle);
                    var right = ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                    if (left || middle || right) {
                        var mousePos = ImGui.GetMousePos();
                        foreach (var (start, end, id) in drawn) {
                            var inBox = mousePos.X >= start.X && mousePos.X <= end.X && mousePos.Y >= start.Y && mousePos.Y <= end.Y;
                            if (!inBox) {
                                continue;
                            }

                            if (left) {
                                this.InfoWindows.Add(id);
                            }

                            if (middle)
                            {
                                var quest = this.Plugin.DataManager.Excel.GetSheet<Quest>().GetRowOrDefault(id);
                                if (quest is not null)
                                {
                                    this.Quest = quest.Value;
                                    this._relayout = true;
                                }

                            }

                            if (right) {
                                unsafe
                                {
                                    AgentQuestJournal.Instance()->OpenForQuest(id, 1);
                                }
                            }

                            break;
                        }
                    }
                }

                this._viewDrag = false;
            }

            drawList.PopClipRect();
            ImGui.EndGroup();
            // ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
        }

        private SeString Convert(SeString se)
        {
            for (var i = 0; i < se.Payloads.Count; i++)
            {
                switch (se.Payloads[i].Type)
                {
                    case PayloadType.NewLine:
                        if (se.Payloads[i] is NewLinePayload)
                        {
                            se.Payloads[i] = new TextPayload("\n");
                        }

                        break;
                    case PayloadType.RawText:
                        if (se.Payloads[i] is TextPayload payload)
                        {
                            payload.Text = this.Plugin.Interface.Sanitizer.Sanitize(payload.Text ?? string.Empty);
                        }

                        break;
                }
            }

            return se;
        }

        private SeString Convert(Lumina.Text.SeString lumina) => this.Convert(lumina.ToDalamudString());

        private SeString Convert(Lumina.Text.ReadOnly.ReadOnlySeString lumina) => this.Convert(lumina.ToDalamudString());
    }
}
