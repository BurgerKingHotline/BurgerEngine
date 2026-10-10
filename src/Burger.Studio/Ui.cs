using System.Numerics;
using Burger.Core;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>Small helpers on top of Dear ImGui used by every panel.</summary>
public static class Ui
{
    public static readonly Vector4 Gold = new(0.93f, 0.77f, 0.36f, 1f);
    public static readonly Vector4 Dim = new(0.62f, 0.62f, 0.66f, 1f);
    public static readonly Vector4 Red = new(0.95f, 0.42f, 0.38f, 1f);
    public static readonly Vector4 Green = new(0.45f, 0.85f, 0.5f, 1f);
    public static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);
    public static readonly Vector4 Blue = new(0.5f, 0.72f, 1f, 1f);

    /// <summary>Text that may contain % characters (ImGui.Text treats its argument as a format string).</summary>
    public static void T(string s) => ImGui.TextUnformatted(s);

    public static void T(string s, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void Wrapped(string s, Vector4? color = null)
    {
        if (color != null) ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(s);
        ImGui.PopTextWrapPos();
        if (color != null) ImGui.PopStyleColor();
    }

    public static void Header(string s)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Gold);
        ImGui.TextUnformatted(s);
        ImGui.PopStyleColor();
    }

    public static void Tip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.Stationary | ImGuiHoveredFlags.AllowWhenDisabled) && ImGui.BeginTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    /// <summary>(?) marker with a tooltip.</summary>
    public static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        Tip(text);
    }

    /// <summary>Tab item without a close button that can be selected programmatically.</summary>
    public static unsafe bool TabItem(string label, ImGuiTabItemFlags flags)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(label + "\0");
        fixed (byte* p = bytes) return ImGuiNative.igBeginTabItem(p, null, flags) != 0;
    }

    public static uint Col(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);
    public static uint Col(int r, int g, int b, int a = 255) => ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));
    public static Vector4 V4(Rgb c, float a = 1) => new(c.R / 255f, c.G / 255f, c.B / 255f, a);

    /// <summary>Editable text; returns true when the value was committed (focus lost or Enter) and changed.</summary>
    public static bool InputCommit(string label, ref string value, uint max = 512)
    {
        ImGui.InputText(label, ref value, max);
        return ImGui.IsItemDeactivatedAfterEdit();
    }

    /// <summary>Combo with a filter box. Returns true when a new value was picked (or typed and Enter pressed).</summary>
    public static bool ComboFilter(string label, ref string value, IReadOnlyList<string> options, string? emptyText = "(none)")
    {
        bool changed = false;
        string preview = value.Length == 0 ? emptyText ?? "" : value;
        if (ImGui.BeginCombo(label, preview, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.IsWindowAppearing()) { _filter = ""; ImGui.SetKeyboardFocusHere(); }
            ImGui.SetNextItemWidth(-1);
            bool enter = ImGui.InputTextWithHint("##filter", "type to search or enter a new value", ref _filter, 128, ImGuiInputTextFlags.EnterReturnsTrue);
            if (enter && _filter.Trim().Length > 0)
            {
                value = _filter.Trim();
                changed = true;
                ImGui.CloseCurrentPopup();
            }
            if (emptyText != null && ImGui.Selectable(emptyText, value.Length == 0)) { value = ""; changed = true; }
            int shown = 0;
            foreach (var o in options)
            {
                if (_filter.Length > 0 && !o.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
                if (++shown > 400) { ImGui.TextDisabled("... (type to narrow the list)"); break; }
                if (ImGui.Selectable(o, o.Equals(value, StringComparison.OrdinalIgnoreCase))) { value = o; changed = true; }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private static string _filter = "";

    /// <summary>Applies the BurgerEngine look to Dear ImGui.</summary>
    public static void ApplyTheme()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 4;
        style.ChildRounding = 3;
        style.FrameRounding = 3;
        style.PopupRounding = 4;
        style.ScrollbarRounding = 6;
        style.GrabRounding = 3;
        style.TabRounding = 3;
        style.WindowBorderSize = 1;
        style.FrameBorderSize = 0;
        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(7, 4);
        style.ItemSpacing = new Vector2(8, 5);
        style.IndentSpacing = 16;

        var c = style.Colors;
        Vector4 V(int r, int g, int b, float a = 1) => new(r / 255f, g / 255f, b / 255f, a);
        c[(int)ImGuiCol.Text] = V(226, 224, 216);
        c[(int)ImGuiCol.TextDisabled] = V(130, 130, 136);
        c[(int)ImGuiCol.WindowBg] = V(30, 31, 36);
        c[(int)ImGuiCol.ChildBg] = V(26, 27, 31);
        c[(int)ImGuiCol.PopupBg] = V(34, 35, 41, 0.98f);
        c[(int)ImGuiCol.Border] = V(58, 58, 66);
        c[(int)ImGuiCol.FrameBg] = V(44, 45, 52);
        c[(int)ImGuiCol.FrameBgHovered] = V(58, 58, 68);
        c[(int)ImGuiCol.FrameBgActive] = V(68, 66, 74);
        c[(int)ImGuiCol.TitleBg] = V(22, 23, 27);
        c[(int)ImGuiCol.TitleBgActive] = V(30, 30, 36);
        c[(int)ImGuiCol.MenuBarBg] = V(22, 23, 27);
        c[(int)ImGuiCol.ScrollbarBg] = V(24, 24, 28);
        c[(int)ImGuiCol.ScrollbarGrab] = V(70, 70, 80);
        c[(int)ImGuiCol.CheckMark] = V(237, 196, 92);
        c[(int)ImGuiCol.SliderGrab] = V(200, 165, 80);
        c[(int)ImGuiCol.SliderGrabActive] = V(237, 196, 92);
        c[(int)ImGuiCol.Button] = V(52, 53, 62);
        c[(int)ImGuiCol.ButtonHovered] = V(86, 74, 50);
        c[(int)ImGuiCol.ButtonActive] = V(120, 98, 52);
        c[(int)ImGuiCol.Header] = V(70, 60, 40);
        c[(int)ImGuiCol.HeaderHovered] = V(92, 78, 48);
        c[(int)ImGuiCol.HeaderActive] = V(120, 98, 52);
        c[(int)ImGuiCol.Separator] = V(58, 58, 66);
        c[(int)ImGuiCol.Tab] = V(36, 37, 43);
        c[(int)ImGuiCol.TabHovered] = V(92, 78, 48);
        c[(int)ImGuiCol.TabSelected] = V(54, 50, 44);
        c[(int)ImGuiCol.TabSelectedOverline] = V(237, 196, 92);
        c[(int)ImGuiCol.TabDimmed] = V(30, 31, 36);
        c[(int)ImGuiCol.TabDimmedSelected] = V(44, 42, 40);
        c[(int)ImGuiCol.DockingPreview] = V(237, 196, 92, 0.5f);
        c[(int)ImGuiCol.TableHeaderBg] = V(40, 40, 48);
        c[(int)ImGuiCol.TableRowBgAlt] = V(255, 255, 255, 0.03f);
        c[(int)ImGuiCol.TextSelectedBg] = V(237, 196, 92, 0.35f);
        c[(int)ImGuiCol.NavCursor] = V(237, 196, 92);
        c[(int)ImGuiCol.ResizeGrip] = V(237, 196, 92, 0.2f);
        c[(int)ImGuiCol.ResizeGripHovered] = V(237, 196, 92, 0.6f);
        c[(int)ImGuiCol.DragDropTarget] = V(237, 196, 92);
    }

    // ---------------------------------------------------------------------------------------------
    // File icons drawn with the draw list (no icon font needed)
    // ---------------------------------------------------------------------------------------------

    public enum Icon { Folder, Script, CSharp, Image, Text, Map, Other }

    public static Icon IconFor(string name, bool dir)
    {
        if (dir) return Icon.Folder;
        string e = Path.GetExtension(name).ToLowerInvariant();
        return e switch
        {
            ".txt" => Icon.Script,
            ".cs" => Icon.CSharp,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".gif" => Icon.Image,
            ".md" or ".csv" or ".json" or ".yml" => Icon.Text,
            ".map" => Icon.Map,
            _ => Icon.Other,
        };
    }

    public static void DrawIcon(ImDrawListPtr dl, Icon icon, Vector2 p, float size)
    {
        float s = size;
        switch (icon)
        {
            case Icon.Folder:
                dl.AddRectFilled(p + new Vector2(s * 0.08f, s * 0.18f), p + new Vector2(s * 0.45f, s * 0.32f), Col(214, 170, 74), 2);
                dl.AddRectFilled(p + new Vector2(s * 0.08f, s * 0.27f), p + new Vector2(s * 0.92f, s * 0.82f), Col(232, 190, 92), 3);
                dl.AddRectFilled(p + new Vector2(s * 0.08f, s * 0.36f), p + new Vector2(s * 0.92f, s * 0.82f), Col(244, 206, 112), 3);
                break;
            default:
                var (body, label, text) = icon switch
                {
                    Icon.Script => (Col(70, 110, 160), Col(130, 180, 240), "TXT"),
                    Icon.CSharp => (Col(110, 70, 160), Col(190, 140, 250), "C#"),
                    Icon.Text => (Col(90, 90, 100), Col(170, 170, 180), "DOC"),
                    Icon.Map => (Col(60, 120, 80), Col(120, 210, 140), "MAP"),
                    _ => (Col(80, 80, 88), Col(150, 150, 160), "FILE"),
                };
                var a = p + new Vector2(s * 0.2f, s * 0.08f);
                var b = p + new Vector2(s * 0.8f, s * 0.92f);
                dl.AddRectFilled(a, b, body, 3);
                dl.AddRectFilled(a + new Vector2(0, s * 0.56f), new Vector2(b.X, a.Y + s * 0.72f), label);
                var ts = ImGui.CalcTextSize(text);
                float scale = Math.Min(1f, s * 0.55f / Math.Max(1, ts.X));
                dl.AddText(ImGui.GetFont(), ImGui.GetFontSize() * scale, new Vector2((a.X + b.X) / 2 - ts.X * scale / 2, a.Y + s * 0.2f), Col(255, 255, 255, 230), text);
                break;
        }
    }
}
