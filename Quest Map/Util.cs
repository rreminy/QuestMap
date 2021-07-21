using Dalamud.Interface;
using ImGuiNET;

namespace QuestMap {
    internal static class Util {
        internal static bool IconButton(FontAwesomeIcon icon, string? id = null) {
            ImGui.PushFont(UiBuilder.IconFont);

            var label = icon.ToIconString();
            if (id != null) {
                label += $"##{id}";
            }

            var ret = ImGui.Button(label);

            ImGui.PopFont();

            return ret;
        }

        internal static void Tooltip(string tooltip) {
            if (!ImGui.IsItemHovered()) {
                return;
            }

            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
    }
}
