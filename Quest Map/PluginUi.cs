using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using Dalamud;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using ImGuiNET;
using ImGuiScene;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;

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
        private GeometryGraph? Graph { get; set; }
        private Node? Centre { get; set; }
        private ChannelReader<GraphInfo> GraphChannel { get; }
        private CancellationTokenSource? CancellationTokenSource { get; set; }
        private HashSet<uint> InfoWindows { get; } = new();
        private Dictionary<uint, TextureWrap> Icons { get; } = new();
        private List<(Quest, bool, string)> FilteredQuests { get; } = new();

        internal bool Show;

        private bool _relayout;
        private Vector2 _offset = Vector2.Zero;
        private static readonly Vector2 TextOffset = new(5, 2);
        private const int GridSmall = 10;
        private const int GridLarge = 50;
        private bool _viewDrag;
        private Vector2 _lastDragPos;

        internal PluginUi(Plugin plugin, ChannelReader<GraphInfo> graphChannel) {
            this.Plugin = plugin;
            this.GraphChannel = graphChannel;

            this.Refilter();

            this.Plugin.Interface.UiBuilder.OnBuildUi += this.Draw;
            this.Plugin.Interface.UiBuilder.OnOpenConfigUi += this.OpenConfig;
        }

        public void Dispose() {
            this.Plugin.Interface.UiBuilder.OnOpenConfigUi -= this.OpenConfig;
            this.Plugin.Interface.UiBuilder.OnBuildUi -= this.Draw;

            foreach (var icon in this.Icons.Values) {
                icon.Dispose();
            }
        }

        private void OpenConfig(object sender, EventArgs e) {
            this.Show = true;
        }

        private void Refilter() {
            this.FilteredQuests.Clear();

            var filterLower = this._filter.ToLowerInvariant();
            var filtered = this.Plugin.Interface.Data.GetExcelSheet<Quest>()
                .Where(quest => {
                    if (quest.Name.ToString().Length == 0) {
                        return false;
                    }

                    if (!this.Plugin.Config.ShowSeasonal && quest.Festival.Row != 0) {
                        return false;
                    }

                    var completed = this.Plugin.Common.Functions.Journal.IsQuestCompleted(quest);
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

                        if (items.All(item => item.ItemUICategory.Row != 81)) {
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

                    return quest.Name.ToString().ToLowerInvariant().Contains(filterLower)
                           || this.Plugin.Quests.ItemRewards.TryGetValue(quest.RowId, out var items1) && items1.Any(item => item.Name.ToString().ToLowerInvariant().Contains(filterLower))
                           || this.Plugin.Quests.EmoteRewards.TryGetValue(quest.RowId, out var emote) && emote.Name.ToString().ToLowerInvariant().Contains(filterLower)
                           || this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action) && action.Name.ToString().ToLowerInvariant().Contains(filterLower)
                           || this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances) && instances.Any(instance => instance.Name.ToString().ToLowerInvariant().Contains(filterLower))
                           || this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe) && tribe.Name.ToString().ToLowerInvariant().Contains(filterLower)
                           || this.Plugin.Quests.JobRewards.TryGetValue(quest.RowId, out var job) && job.Name.ToString().ToLowerInvariant().Contains(filterLower);
                })
                .SelectMany(quest => {
                    var drawItems = new List<(Quest, bool, string)> {
                        (quest, false, $"{this.Convert(quest.Name)}##{quest.RowId}"),
                    };

                    var allItems = this.Plugin.Config.ItemVis != Visibility.Hidden;
                    var anyItemVisible = allItems || this.Plugin.Config.MinionVis != Visibility.Hidden;
                    if (anyItemVisible && this.Plugin.Quests.ItemRewards.TryGetValue(quest.RowId, out var items)) {
                        var toShow = items.Where(item => allItems || item.ItemUICategory.Row == 81);
                        drawItems.AddRange(toShow.Select(item => (quest, true, $"{this.Convert(item.Name)}##item-{quest.RowId}-{item.RowId}")));
                    }

                    if (this.Plugin.Config.EmoteVis != Visibility.Hidden && this.Plugin.Quests.EmoteRewards.TryGetValue(quest.RowId, out var emote)) {
                        drawItems.Add((quest, true, $"{this.Convert(emote.Name)}##emote-{quest.RowId}-{emote.RowId}"));
                    }

                    if (this.Plugin.Config.ActionsVis != Visibility.Hidden && this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action)) {
                        drawItems.Add((quest, true, $"{this.Convert(action.Name)}##action-{quest.RowId}-{action.RowId}"));
                    }

                    if (this.Plugin.Config.InstanceVis != Visibility.Hidden && this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances)) {
                        drawItems.AddRange(instances.Select(instance => (quest, true, $"{this.Convert(instance.Name)}##instance-{quest.RowId}-{instance.RowId}")));
                    }

                    if (this.Plugin.Config.TribeVis != Visibility.Hidden && this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe)) {
                        drawItems.Add((quest, true, $"{this.Convert(tribe.Name)}##tribe-{quest.RowId}-{tribe.RowId}"));
                    }

                    if (this.Plugin.Config.JobVis != Visibility.Hidden && this.Plugin.Quests.JobRewards.TryGetValue(quest.RowId, out var job)) {
                        drawItems.Add((quest, true, $"{this.Convert(job.Name)}##job-{quest.RowId}-{job.RowId}"));
                    }

                    return drawItems;
                });
            this.FilteredQuests.AddRange(filtered);
        }

        private void Draw() {
            if (this.GraphChannel.TryRead(out var graph)) {
                this.Graph = graph.Graph;
                this.Centre = graph.Centre;
                this.CancellationTokenSource = null;
            }

            this.DrawInfoWindows();

            this.DrawMainWindow();
        }

        private void DrawMainWindow() {
            if (!this.Show) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(675, 600), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(DalamudPlugin.PluginName, ref this.Show, ImGuiWindowFlags.MenuBar)) {
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
                        var (quest, indent, drawItem) = this.FilteredQuests[row];

                        void DrawSelectable(string name, Quest quest) {
                            var completed = this.Plugin.Common.Functions.Journal.IsQuestCompleted(quest);
                            if (completed) {
                                Vector4 disabled;
                                unsafe {
                                    disabled = *ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled);
                                }

                                ImGui.PushStyleColor(ImGuiCol.Text, disabled);
                            }

                            var ret = ImGui.Selectable(name, this.Quest == quest);

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
                            this.Graph = null;
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
                if (this.Quest != null && this.Graph == null) {
                    ImGui.TextUnformatted("Generating map...");
                }

                if (this.Graph != null) {
                    this.DrawGraph(this.Graph);
                }

                ImGui.EndChild();
            }

            if (this._relayout && this.Quest != null) {
                this.Graph = null;
                this.CancellationTokenSource?.Cancel();
                this.CancellationTokenSource = this.Plugin.Quests.StartGraphRecalculation(this.Quest);
                this._relayout = false;
            }

            ImGui.End();
        }

        private void DrawInfoWindows() {
            var remove = 0u;

            foreach (var id in this.InfoWindows) {
                var quest = this.Plugin.Interface.Data.GetExcelSheet<Quest>().GetRow(id);
                if (quest == null) {
                    continue;
                }

                if (this.DrawInfoWindow(quest)) {
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

            var completed = this.Plugin.Common.Functions.Journal.IsQuestCompleted(quest);

            ImGui.TextUnformatted($"Level: {quest.ClassJobLevel0}");

            if (completed) {
                ImGui.PushFont(UiBuilder.IconFont);
                var check = FontAwesomeIcon.Check.ToIconString();
                var width = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(check).X;
                ImGui.SameLine(width);
                ImGui.TextUnformatted(check);
                ImGui.PopFont();
            }

            TextureWrap GetIcon(uint id) {
                if (this.Icons.TryGetValue(id, out var wrap)) {
                    return wrap;
                }

                wrap = this.Plugin.Interface.Data.GetImGuiTextureIcon(this.Plugin.Interface.ClientState.ClientLanguage, (int) id);
                this.Icons[id] = wrap;

                return wrap;
            }

            var textWrap = ImGui.GetFontSize() * 20f;

            if (quest.Icon != 0) {
                var header = GetIcon(quest.Icon);
                textWrap = header.Width;
                ImGui.Image(header.ImGuiHandle, new Vector2(header.Width, header.Height));
            }

            var rewards = new List<string>();
            var paramGrow = this.Plugin.Interface.Data.GetExcelSheet<ParamGrow>().GetRow(quest.ClassJobLevel0);
            var xp = quest.ExpFactor * paramGrow.ScaledQuestXP * paramGrow.QuestExpModifier / 100;
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

                var maxHeight = items.Select(entry => GetIcon(entry.icon)).Max(image => image.Height);

                var originalY = ImGui.GetCursorPosY();
                foreach (var (name, icon, qty) in items) {
                    var image = GetIcon(icon);
                    if (image.Height < maxHeight) {
                        ImGui.SetCursorPosY(originalY + (maxHeight - image.Height) / 2f);
                    }

                    ImGui.Image(image.ImGuiHandle, new Vector2(image.Width, image.Height));
                    Util.Tooltip(name.ToString());
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

            for (var i = 0; i < quest.ItemCatalyst.Length; i++) {
                var catalyst = quest.ItemCatalyst[i];
                var amount = quest.ItemCountCatalyst[i];

                if (catalyst.Row != 0) {
                    additionalRewards.Add((this.Convert(catalyst.Value.Name), catalyst.Value.Icon, amount));
                }
            }

            foreach (var generalAction in quest.GeneralActionReward.Where(row => row.Row != 0)) {
                additionalRewards.Add((this.Convert(generalAction.Value.Name), (uint) generalAction.Value.Icon, 1));
            }

            if (this.Plugin.Quests.ActionRewards.TryGetValue(quest.RowId, out var action)) {
                additionalRewards.Add((this.Convert(action.Name), action.Icon, 1));
            }

            if (this.Plugin.Quests.EmoteRewards.TryGetValue(quest.RowId, out var emote)) {
                additionalRewards.Add((this.Convert(emote.Name), emote.Icon, 1));
            }

            if (quest.OtherReward.Row != 0) {
                additionalRewards.Add((this.Convert(quest.OtherReward.Value.Name), quest.OtherReward.Value.Icon, 1));
            }

            if (quest.ReputationReward > 0) {
                var beastTribe = quest.BeastTribe.Value;
                if (beastTribe != null) {
                    additionalRewards.Add((this.Convert(beastTribe.NameRelation), beastTribe.Icon, quest.ReputationReward));
                }
            }

            if (quest.TomestoneReward > 0) {
                var tomestone = this.Plugin.Interface.Data.GetExcelSheet<TomestonesItem>().First(row => row.Tomestones.Row == quest.TomestoneReward);
                var item = tomestone?.Item?.Value;
                if (item != null) {
                    additionalRewards.Add((this.Convert(item.Name), item.Icon, quest.TomestoneCountReward));
                }
            }

            if (quest.ItemRewardType is 0 or 1 or 3 or 5) {
                DrawItemRewards(
                    "Rewards",
                    quest.ItemReward0
                        .Zip(quest.ItemCountReward0, (id, qty) => (id, qty))
                        .Where(entry => entry.id != 0)
                        .Select(entry => (item: this.Plugin.Interface.Data.GetExcelSheet<Item>().GetRow(entry.id), entry.qty))
                        .Where(entry => entry.item != null)
                        .Select(entry => (this.Convert(entry.item.Name), (uint) entry.item.Icon, entry.qty))
                        .Concat(additionalRewards)
                );

                DrawItemRewards(
                    "Optional rewards",
                    quest.ItemReward1
                        .Zip(quest.ItemCountReward1, (row, qty) => (row, qty))
                        .Where(entry => entry.row.Row != 0)
                        .Select(entry => (item: entry.row.Value, entry.qty))
                        .Where(entry => entry.item != null)
                        .Select(entry => (this.Convert(entry.item.Name), (uint) entry.item.Icon, entry.qty))
                );
            }

            if (this.Plugin.Quests.InstanceRewards.TryGetValue(quest.RowId, out var instances)) {
                ImGui.TextUnformatted("Instances");

                foreach (var instance in instances) {
                    var icon = instance.ContentType.Value?.Icon ?? 0;
                    if (icon > 0) {
                        var image = GetIcon(icon);
                        ImGui.Image(image.ImGuiHandle, new Vector2(image.Width, image.Height));
                        Util.Tooltip(this.Convert(instance.Name).ToString());
                    } else {
                        ImGui.TextUnformatted(this.Convert(instance.Name).ToString());
                    }
                }

                ImGui.Separator();
            }

            if (this.Plugin.Quests.BeastRewards.TryGetValue(quest.RowId, out var tribe)) {
                ImGui.TextUnformatted("Beast tribe");

                var image = GetIcon(tribe.Icon);
                ImGui.Image(image.ImGuiHandle, new Vector2(image.Width, image.Height));
                Util.Tooltip(this.Convert(tribe.Name).ToString());

                ImGui.Separator();
            }

            var id = quest.RowId & 0xFFFF;
            var lang = this.Plugin.Interface.ClientState.ClientLanguage switch {
                ClientLanguage.English => Language.English,
                ClientLanguage.Japanese => Language.Japanese,
                ClientLanguage.German => Language.German,
                ClientLanguage.French => Language.French,
                _ => Language.English,
            };
            var path = $"quest/{id.ToString("00000").Substring(0, 3)}/{quest.Id.RawString.ToLowerInvariant()}";
            // FIXME: this is gross, but lumina caches incorrectly
            this.Plugin.Interface.Data.Excel.RemoveSheetFromCache<QuestData>();
            var sheet = this.Plugin.Interface.Data.Excel.GetType()
                .GetMethod("GetSheet", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.MakeGenericMethod(typeof(QuestData))
                // ReSharper disable once ConstantConditionalAccessQualifier
                ?.Invoke(this.Plugin.Interface.Data.Excel, new object?[] {
                    path,
                    lang,
                    null,
                }) as ExcelSheet<QuestData>;
            // default to english if reflection failed
            sheet ??= this.Plugin.Interface.Data.Excel.GetSheet<QuestData>(path);
            var firstData = sheet?.GetRow(0);
            if (firstData != null) {
                ImGui.PushTextWrapPos(textWrap);
                ImGui.TextUnformatted(this.Convert(firstData.Text).ToString());
                ImGui.PopTextWrapPos();
            }

            ImGui.Separator();

            void OpenMap(Level? level) {
                if (level == null) {
                    return;
                }

                var mapLink = new MapLinkPayload(
                    this.Plugin.Interface.Data,
                    level.Territory.Row,
                    level.Map.Row,
                    (int) (level.X * 1_000f),
                    (int) (level.Z * 1_000f)
                );

                this.Plugin.Interface.Framework.Gui.OpenMapWithMapLink(mapLink);
            }

            var issuer = this.Plugin.Interface.Data.GetExcelSheet<ENpcResident>().GetRow(quest.IssuerStart)?.Singular ?? "Unknown";
            var target = this.Plugin.Interface.Data.GetExcelSheet<ENpcResident>().GetRow(quest.TargetEnd)?.Singular ?? "Unknown";
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
                this.Plugin.Common.Functions.Journal.OpenQuest(quest);
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

        private void DrawGraph(GeometryGraph graph) {
            // now the fun (tm) begins
            var space = ImGui.GetContentRegionAvail();
            var size = new Vector2(space.X, space.Y);
            var drawList = ImGui.GetWindowDrawList();

            ImGui.BeginGroup();

            ImGui.InvisibleButton("##NodeEmpty", size);
            var canvasTopLeft = ImGui.GetItemRectMin();
            var canvasBottomRight = ImGui.GetItemRectMax();

            if (this.Centre != null) {
                this._offset = ConvertPoint(this.Centre.Center) * -1 + (canvasBottomRight - canvasTopLeft) / 2;
                this.Centre = null;
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

                var quest = (Quest) node.UserData;

                var colour = quest.EventIconType.Row switch {
                    1 => Colours.NormalQuest, // normal
                    3 => Colours.MsqQuest, // msq
                    8 => Colours.BlueQuest, // blue
                    10 => Colours.BlueQuest, // also blue
                    _ => Colours.NormalQuest,
                };
                var textColour = Colours.Text;

                var completed = this.Plugin.Common.Functions.Journal.IsQuestCompleted(quest.RowId);
                if (completed) {
                    colour.W = .5f;
                    textColour = (uint) ((0x80 << 24) | (textColour & 0xFFFFFF));
                }

                var end = canvasBottomRight - this.GetBottomRight(node);

                drawn.Add((start, end, quest.RowId));

                if (quest == this.Quest) {
                    drawList.AddRect(start - Vector2.One, end + Vector2.One, Colours.Line, 5, ImDrawFlags.RoundCornersAll);
                }

                drawList.AddRectFilled(start, end, ImGui.GetColorU32(colour), 5, ImDrawFlags.RoundCornersAll);
                drawList.AddText(start + TextOffset, textColour, this.Convert(quest.Name).ToString());
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
                    var right = ImGui.IsMouseReleased(ImGuiMouseButton.Right);
                    if (left || right) {
                        var mousePos = ImGui.GetMousePos();
                        foreach (var (start, end, id) in drawn) {
                            var inBox = mousePos.X >= start.X && mousePos.X <= end.X && mousePos.Y >= start.Y && mousePos.Y <= end.Y;
                            if (!inBox) {
                                continue;
                            }

                            if (left) {
                                this.InfoWindows.Add(id);
                            }

                            if (right) {
                                this.Plugin.Common.Functions.Journal.OpenQuest(id);
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

        private static readonly byte[] NewLinePayload = { 0x02, 0x10, 0x01, 0x03 };

        private SeString Convert(Lumina.Text.SeString lumina) {
            var se = this.Plugin.Interface.SeStringManager.Parse(lumina.RawData.ToArray());
            for (var i = 0; i < se.Payloads.Count; i++) {
                switch (se.Payloads[i].Type) {
                    case PayloadType.Unknown:
                        if (se.Payloads[i].Encode().SequenceEqual(NewLinePayload)) {
                            se.Payloads[i] = new TextPayload("\n");
                        }

                        break;
                    case PayloadType.RawText:
                        if (se.Payloads[i] is TextPayload payload) {
                            payload.Text = this.Plugin.Interface.Sanitizer.Sanitize(payload.Text);
                        }

                        break;
                }
            }

            return se;
        }
    }
}
