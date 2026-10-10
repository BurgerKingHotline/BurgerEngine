using System.Numerics;
using Burger.Core.Model;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>Outliner: every object of the game in a searchable tree (like Unity's Hierarchy).</summary>
public sealed partial class Studio
{
    private string _outlinerFilter = "";

    private void DrawOutliner()
    {
        if (!ImGui.Begin(WOutliner, ref _showOutliner)) { ImGui.End(); return; }
        TrackHover(WOutliner);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##outfilter", "Search...", ref _outlinerFilter, 128);
        string f = _outlinerFilter.Trim();
        ImGui.BeginChild("outlinerTree");

        // ------------------------------------------------ World
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (ImGui.CollapsingHeader("World"))
        {
            DrawCountriesTree(f);
            DrawStatesTree(f);
            DrawKindTree(Specs.Characters, f);
        }

        // ------------------------------------------------ Definitions
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (ImGui.CollapsingHeader("Mechanics"))
        {
            foreach (var k in new[] { Specs.Resources, Specs.Modifiers, Specs.Decisions, Specs.ScriptValues, Specs.Interface, Specs.OnActions })
                DrawKindTree(k, f);
        }
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (ImGui.CollapsingHeader("Content"))
        {
            foreach (var k in new[] { Specs.Events, Specs.DiplomaticActions, Specs.CharacterInteractions, Specs.CasusBelli, Specs.SubjectTypes, Specs.Traits })
                DrawKindTree(k, f);
        }
        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (ImGui.CollapsingHeader("Military & Terrain"))
        {
            foreach (var k in new[] { Specs.UnitTypes, Specs.DivisionTemplates, Specs.Terrain })
                DrawKindTree(k, f);
        }
        ImGui.EndChild();
        ImGui.End();
    }

    private static bool Matches(string filter, params string[] texts) => filter.Length == 0 || texts.Any(t => t.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private void DrawKindTree(BlockSpec kind, string filter)
    {
        var items = Catalog!.Of(kind);
        var shown = items.Where(b => Matches(filter, b.Id, b.Display)).ToList();
        if (filter.Length > 0 && shown.Count == 0) return;
        if (filter.Length > 0) ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"{kind.Title}  ({items.Count})##kind{kind.Title}", ImGuiTreeNodeFlags.SpanAvailWidth);
        KindContextMenu(kind);
        if (!open) return;
        foreach (var b in shown) BlockLeaf(b);
        if (items.Count == 0) ImGui.TextDisabled("  (none - right-click to create)");
        ImGui.TreePop();
    }

    private void KindContextMenu(BlockSpec kind)
    {
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem($"New {Singular(kind)}...")) CreateBlockAsset(kind);
            if (ImGui.MenuItem("Show folder in Content Browser")) { _browseDir = FolderOf(kind); _showContent = true; ImGui.SetWindowFocus(WContent); }
            ImGui.EndPopup();
        }
        Ui.Tip(kind.Help);
    }

    private void BlockLeaf(BlockRef b, string? labelOverride = null)
    {
        bool sel = Selected is SelBlock sb && sb.Ref.Same(b);
        var flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth | (sel ? ImGuiTreeNodeFlags.Selected : 0);
        ImGui.TreeNodeEx($"{labelOverride ?? b.Label}##{b.Spec.Title}{b.File}{b.Id}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) SelectBlock(b);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenFile(b.File, b.Line);
        BlockContextMenu(b);
    }

    private void BlockContextMenu(BlockRef b)
    {
        if (!ImGui.BeginPopupContextItem()) return;
        if (ImGui.MenuItem("Open script")) OpenFile(b.File, b.Line);
        if (ImGui.MenuItem("Duplicate...")) DuplicateBlock(b);
        if (ImGui.MenuItem("Delete")) DeleteBlock(b);
        ImGui.Separator();
        if (ImGui.MenuItem("Show in Content Browser")) { _browseDir = Path.GetDirectoryName(b.File)?.Replace('\\', '/') ?? ""; _contentSelected = b.File; _showContent = true; }
        if (b.Spec == Specs.Countries && ImGui.MenuItem($"Play as {b.Id}")) Play(b.Id);
        ImGui.EndPopup();
    }

    public void SelectBlock(BlockRef b)
    {
        FlushPendingStates();
        Selected = new SelBlock { Ref = b };
        // countries and characters: move the camera to them
        if (_world != null)
        {
            if (b.Spec == Specs.Countries && _world.World.GetCountry(b.Id)?.Capital is { } cap) FocusOn(cap);
            if (b.Spec == Specs.Characters && _world.World.GetCharacter(b.Id)?.Country?.Capital is { } cap2) FocusOn(cap2);
        }
    }

    private void DrawCountriesTree(string filter)
    {
        var items = Catalog!.Of(Specs.Countries);
        if (filter.Length > 0) ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"Countries  ({items.Count})##kindCountries", ImGuiTreeNodeFlags.SpanAvailWidth);
        KindContextMenu(Specs.Countries);
        if (!open) return;
        foreach (var b in items)
        {
            var c = _world?.World.GetCountry(b.Id);
            var states = c?.OwnedStates ?? new List<State>();
            if (!Matches(filter, b.Id, b.Display) && !states.Any(s => Matches(filter, s.Key))) continue;
            bool sel = Selected is SelBlock sb && sb.Ref.Same(b);
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | (sel ? ImGuiTreeNodeFlags.Selected : 0) | (states.Count == 0 ? ImGuiTreeNodeFlags.Leaf : 0);
            // color swatch
            var p = ImGui.GetCursorScreenPos();
            bool nodeOpen = ImGui.TreeNodeEx($"      {b.Label}##country{b.Id}", flags);
            if (c != null)
            {
                float hgt = ImGui.GetFontSize();
                var dl = ImGui.GetWindowDrawList();
                float x = p.X + ImGui.GetTreeNodeToLabelSpacing();
                dl.AddRectFilled(new Vector2(x, p.Y + 3), new Vector2(x + hgt * 1.2f, p.Y + hgt), Ui.Col(c.Color.R, c.Color.G, c.Color.B), 2);
            }
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) SelectBlock(b);
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenFile(b.File, b.Line);
            BlockContextMenu(b);
            if (!nodeOpen) continue;
            foreach (var s in states.OrderBy(s => s.Key)) StateLeaf(s);
            ImGui.TreePop();
        }
        ImGui.TreePop();
    }

    private void DrawStatesTree(string filter)
    {
        if (_world == null) return;
        var states = _world.World.States;
        var unowned = states.Where(s => s.Owner == null).ToList();
        var stateless = _world.World.Locations.Count(l => l.IsLand && l.State == null);
        if (filter.Length > 0 && !states.Any(s => Matches(filter, s.Key))) return;
        if (filter.Length > 0) ImGui.SetNextItemOpen(true);
        bool open = ImGui.TreeNodeEx($"States  ({states.Count})##kindStates", ImGuiTreeNodeFlags.SpanAvailWidth);
        Ui.Tip("States are groups of locations owned by a country (map/states). Edit them on the map: select a location, then use the Inspector or the Paint tool.");
        if (!open) return;
        if (stateless > 0) Ui.T($"{stateless} land locations have no state", Ui.Yellow);
        if (unowned.Count > 0) Ui.T($"{unowned.Count} states have no owner", Ui.Dim);
        foreach (var s in states.OrderBy(s => s.Key))
            if (Matches(filter, s.Key, s.Owner?.Tag ?? "")) StateLeaf(s);
        ImGui.TreePop();
    }

    private void StateLeaf(State s)
    {
        bool sel = Selected is SelState ss && ss.State == s;
        ImGui.TreeNodeEx($"{s.Key}  ({s.Locations.Count})##state{s.Key}", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth | (sel ? ImGuiTreeNodeFlags.Selected : 0));
        if (ImGui.IsItemClicked())
        {
            FlushPendingStates();
            Selected = new SelState { State = s };
            if (s.Locations.Count > 0) FocusOn(s.Locations[0]);
        }
    }

    public static string Singular(BlockSpec kind) => kind.Title switch
    {
        "Countries" => "country",
        "Characters" => "character",
        "Casus belli" => "casus belli",
        "Windows & map modes" => "window",
        "On actions" => "on action",
        _ => kind.Title.ToLowerInvariant().TrimEnd('s'),
    };
}
