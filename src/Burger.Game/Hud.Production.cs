using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Production (factories, lines, construction, stockpile) and the division designer.</summary>
public sealed partial class Hud
{
    private bool _addingLine;
    private ProductionLine? _changingLine;

    private string Fmt(double v) => v >= 10000 ? $"{v / 1000:0.#}k" : v >= 100 ? $"{v:0}" : $"{v:0.#}";

    private bool Tabs(float x, float y, float w, string[] names, ref int current)
    {
        bool changed = false;
        float tw = (w - (names.Length - 1) * 4 * S) / names.Length;
        for (int i = 0; i < names.Length; i++)
            if (Gui.Button(new Rectangle(x + i * (tw + 4 * S), y, tw, 26 * S), names[i], true, null, current == i, 15)) { if (current != i) changed = true; current = i; }
        return changed;
    }

    private void DrawProductionPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.Production)) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 440 * S;
        Gui.Panel(r, "Production");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        int tab = Ui.ProductionTab;
        if (Tabs(x, y, w, new[] { "Production", "Construction", "Stockpile" }, ref tab)) { _addingLine = false; _changingLine = null; }
        Ui.ProductionTab = tab;
        if (tab != 1) Ui.BuildType = null;
        y += 36 * S;
        switch (tab)
        {
            case 0: DrawProductionLines(player, r, x, y, w); break;
            case 1: DrawConstruction(player, r, x, y, w); break;
            default: DrawStockpile(player, r, x, y, w); break;
        }
    }

    private void DrawProductionLines(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Production;
        int mil = P.MilitaryFactories(c), dock = P.Dockyards(c);
        Gui.Label($"Military factories: {mil - P.FreeFactories(c, false)}/{mil} in use", x, y, 15);
        if (dock > 0) Gui.Label($"Dockyards: {dock - P.FreeFactories(c, true)}/{dock}", x + w - 150 * S, y, 15, Gui.TextDim);
        y += 24 * S;
        if (_addingLine || _changingLine != null)
        {
            Gui.Section(_changingLine != null ? "Produce instead" : "New production line", x, y, w);
            y += 26 * S;
            float top = Gui.BeginScroll("equip", new Rectangle(x, y, w, r.Y + r.Height - y - 50 * S), G.Data.Equipment.Count * 32 * S);
            float ey = top;
            foreach (var e in G.Data.Equipment.Values.OrderBy(e => e.Category).ThenBy(e => e.Cost))
            {
                string tip = $"{e.Name}\nCost: {e.Cost:0.##} per unit\nCategory: {e.Category}" + (P.UsesDockyards(e) ? "\nBuilt in dockyards" : "");
                if (Gui.Button(new Rectangle(x, ey, w - 10 * S, 28 * S), $"{e.Name}   ({Fmt(P.Stock(c, e.Key))} in stock)", true, tip, false, 15))
                {
                    if (_changingLine != null) P.ChangeLine(_changingLine, e);
                    else P.AddLine(c, e, Math.Min(1, P.FreeFactories(c, P.UsesDockyards(e))));
                    _addingLine = false;
                    _changingLine = null;
                }
                ey += 32 * S;
            }
            Gui.EndScroll();
            if (Gui.Button(new Rectangle(x, r.Y + r.Height - 42 * S, w, 30 * S), "Cancel", true, null, false, 15)) { _addingLine = false; _changingLine = null; }
            return;
        }
        float rowH = 76 * S;
        float listTop = Gui.BeginScroll("lines", new Rectangle(x, y, w, r.Y + r.Height - y - 50 * S), c.ProductionLines.Count * (rowH + 5 * S));
        float ly = listTop;
        foreach (var line in c.ProductionLines.ToList())
        {
            var box = new Rectangle(x, ly, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            bool naval = P.UsesDockyards(line.Equipment);
            Gui.Label(line.Equipment.Name, box.X + 8 * S, box.Y + 5 * S, 16, Gui.Gold, FontKind.Bold);
            double perDay = line.Factories * G.Data.Defines.Get("production_per_factory", 4.5) * line.Efficiency / line.Equipment.Cost;
            Gui.Label($"{perDay:0.##}/day   stock {Fmt(P.Stock(c, line.Equipment.Key))}", box.X + 8 * S, box.Y + 27 * S, 13, Gui.TextDim);
            var eff = new Rectangle(box.X + 8 * S, box.Y + 49 * S, 150 * S, 10 * S);
            Gui.Bar(eff, (float)line.Efficiency, new Color(90, 170, 220, 255));
            Gui.Label($"{line.Efficiency:P0} efficiency", eff.X + eff.Width + 6 * S, eff.Y - 3 * S, 13, Gui.TextDim);
            if (Gui.Hover(eff)) Gui.Tooltip("Efficiency grows every day the line keeps producing the same thing.");
            // factories
            float fx = box.X + box.Width - 150 * S;
            if (Gui.Button(new Rectangle(fx, box.Y + 6 * S, 26 * S, 24 * S), "-", line.Factories > 0, "One factory less", false, 16)) line.Factories--;
            Gui.Label($"{line.Factories} {(naval ? "dock." : "fact.")}", fx + 32 * S, box.Y + 9 * S, 14, Gui.Text, FontKind.Bold);
            if (Gui.Button(new Rectangle(fx + 90 * S, box.Y + 6 * S, 26 * S, 24 * S), "+", P.FreeFactories(c, naval) > 0, "One factory more (if free)", false, 16)) line.Factories++;
            if (Gui.Button(new Rectangle(fx + 120 * S, box.Y + 6 * S, 24 * S, 24 * S), "x", true, "Stop this line", false, 13)) P.RemoveLine(c, line);
            if (Gui.Button(new Rectangle(box.X + box.Width - 90 * S, box.Y + 44 * S, 82 * S, 24 * S), "Change", true, "Produce something else (efficiency drops)", false, 13)) _changingLine = line;
            ly += rowH + 5 * S;
        }
        Gui.EndScroll();
        if (c.ProductionLines.Count == 0) Gui.Label("No production lines. Add one below.", x, y, 14, Gui.TextDim);
        if (Gui.Button(new Rectangle(x, r.Y + r.Height - 42 * S, w, 30 * S), "+ Add production line", G.Data.Equipment.Count > 0, null, false, 15)) _addingLine = true;
    }

    private void DrawConstruction(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Production;
        int civ = P.CivilianFactories(c), free = P.CivilianForConstruction(c);
        Gui.Label($"Civilian factories: {civ}  ({free} for construction)", x, y, 15);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S)))
            Gui.Tooltip($"{civ - free} civilian factories make consumer goods.\nEach building gets up to {G.Data.Defines.Get("max_factories_per_project", 15):0} factories, top of the queue first.");
        y += 26 * S;
        Gui.Section("Build", x, y, w);
        y += 26 * S;
        var types = G.Data.BuildingTypes.Values.ToList();
        float bw = (w - 8 * S) / 2;
        for (int i = 0; i < types.Count; i++)
        {
            var b = types[i];
            var br = new Rectangle(x + (i % 2) * (bw + 8 * S), y + (i / 2) * 32 * S, bw, 28 * S);
            string tip = $"{b.Name}\nCost: {b.Cost:0}\nMax level: {b.MaxLevel}" + (b.Description.Length > 0 ? "\n\n" + Localisation.Get(b.Description) : "") + "\n\nPick it, then click your states on the map.";
            if (Gui.Button(br, b.Name, true, tip, Ui.BuildType == b, 14)) Ui.BuildType = Ui.BuildType == b ? null : b;
        }
        y += (types.Count + 1) / 2 * 32 * S + 6 * S;
        if (Ui.BuildType != null) { y += Gui.Wrapped($"Click one of your states to build a {Ui.BuildType.Name}. Right-click or Esc to stop.", x, y, w, 14, Gui.Gold) + 6 * S; }
        Gui.Section($"Queue ({c.Construction.Count})", x, y, w);
        y += 26 * S;
        float rowH = 46 * S;
        float top = Gui.BeginScroll("construction", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), c.Construction.Count * (rowH + 4 * S));
        float qy = top;
        for (int i = 0; i < c.Construction.Count; i++)
        {
            var p = c.Construction[i];
            var box = new Rectangle(x, qy, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(Gui.Fit($"{p.Building.Name} - {p.State.DisplayName}", box.Width - 100 * S, 15), box.X + 8 * S, box.Y + 4 * S, 15);
            Gui.Bar(new Rectangle(box.X + 8 * S, box.Y + 28 * S, box.Width - 110 * S, 9 * S), (float)(p.Progress / p.Building.Cost), Gui.Gold);
            Gui.Label($"{p.Factories} fact.", box.X + box.Width - 96 * S, box.Y + 24 * S, 13, Gui.TextDim);
            if (i > 0 && Gui.Button(new Rectangle(box.X + box.Width - 58 * S, box.Y + 4 * S, 24 * S, 20 * S), "^", true, "Move up", false, 13))
                (c.Construction[i - 1], c.Construction[i]) = (c.Construction[i], c.Construction[i - 1]);
            if (Gui.Button(new Rectangle(box.X + box.Width - 30 * S, box.Y + 4 * S, 24 * S, 20 * S), "x", true, "Cancel", false, 13)) { c.Construction.RemoveAt(i); Gui.EndScroll(); return; }
            qy += rowH + 4 * S;
        }
        Gui.EndScroll();
    }

    private void DrawStockpile(Country c, Rectangle r, float x, float y, float w)
    {
        var shortage = G.Production.Shortage(c);
        var produced = c.ProductionLines.GroupBy(l => l.Equipment.Key).ToDictionary(g => g.Key, g => g.Sum(l => l.LastOutput), StringComparer.OrdinalIgnoreCase);
        float top = Gui.BeginScroll("stockpile", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), G.Data.Equipment.Count * 46 * S);
        float sy = top;
        foreach (var e in G.Data.Equipment.Values)
        {
            var box = new Rectangle(x, sy, w - 8 * S, 42 * S);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(e.Name, box.X + 8 * S, box.Y + 4 * S, 15, Gui.Text, FontKind.Bold);
            double need = shortage.GetValueOrDefault(e.Key);
            Gui.Label($"+{produced.GetValueOrDefault(e.Key):0.#}/day" + (need > 0 ? $"   missing {Fmt(need)}" : ""), box.X + 8 * S, box.Y + 23 * S, 13, need > 0 ? Gui.Bad : Gui.TextDim);
            string stock = Fmt(G.Production.Stock(c, e.Key));
            var m = Gui.Measure(stock, 18, FontKind.Bold);
            Gui.Label(stock, box.X + box.Width - m.X - 10 * S, box.Y + 11 * S, 18, Gui.Gold, FontKind.Bold);
            if (Gui.Hover(box)) Gui.Tooltip($"{e.Name}: {G.Production.Stock(c, e.Key):0} in stock\nMissing for divisions and training: {need:0}");
            sy += 46 * S;
        }
        Gui.EndScroll();
    }

    // ===============================================================================================
    // Division designer
    // ===============================================================================================
    private DivisionTemplate? _designBase;
    private string _designName = "New Division";
    private readonly List<(UnitType Type, int Count)> _design = new();

    private void LoadDesign(DivisionTemplate t)
    {
        _designBase = t;
        _designName = t.Name;
        _design.Clear();
        _design.AddRange(t.Units);
    }

    private void DrawDesignerPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.DivisionDesigner)) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 560 * S;
        Gui.Panel(r, "Division Designer");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S;
        if (_design.Count == 0 && _designBase == null && G.Designer.TemplatesOf(player).FirstOrDefault() is { } first) LoadDesign(first);

        // templates list
        float lw = 170 * S;
        Gui.Section("Templates", x, y, lw);
        var templates = G.Designer.TemplatesOf(player).ToList();
        float top = Gui.BeginScroll("designs", new Rectangle(x, y + 26 * S, lw, r.Y + r.Height - y - 36 * S), templates.Count * 30 * S + 34 * S);
        float ty = top;
        if (Gui.Button(new Rectangle(x, ty, lw - 10 * S, 26 * S), "+ New", true, "Start an empty design", false, 14)) { _designBase = null; _designName = "New Division"; _design.Clear(); }
        ty += 32 * S;
        foreach (var t in templates)
        {
            if (Gui.Button(new Rectangle(x, ty, lw - 10 * S, 26 * S), t.Name, true, (t.Custom ? "Your design" : "From the game files") + $"\n{t.Battalions} battalions", _designBase == t, 13))
                LoadDesign(t);
            ty += 30 * S;
        }
        Gui.EndScroll();

        // editor
        float ex = x + lw + 12 * S, ew = r.X + r.Width - 12 * S - ex;
        Gui.TextField("designname", new Rectangle(ex, y, ew, 28 * S), ref _designName, 40, "Division name");
        y += 36 * S;
        int total = _design.Sum(u => u.Count);
        int max = G.Designer.Rules.MaxBattalions(G, player);
        Gui.Section($"Battalions ({total}/{max})", ex, y, ew);
        y += 26 * S;
        for (int i = 0; i < _design.Count; i++)
        {
            var (type, count) = _design[i];
            Gui.Label(Gui.Fit(type.Name, ew - 120 * S, 15), ex, y + 4 * S, 15);
            if (Gui.Button(new Rectangle(ex + ew - 110 * S, y, 28 * S, 24 * S), "-", true, null, false, 16))
            {
                if (count <= 1) { _design.RemoveAt(i); break; }
                _design[i] = (type, count - 1);
            }
            Gui.Label(count.ToString(), ex + ew - 72 * S, y + 3 * S, 16, Gui.Text, FontKind.Bold);
            if (Gui.Button(new Rectangle(ex + ew - 40 * S, y, 28 * S, 24 * S), "+", total < max, null, false, 16)) _design[i] = (type, count + 1);
            y += 28 * S;
        }
        // add a battalion type
        var addable = G.Data.UnitTypes.Values.Where(u => !_design.Any(d => d.Type == u) && G.Designer.Rules.CanUse(G, player, u)).ToList();
        if (addable.Count > 0)
        {
            Gui.Label("Add:", ex, y + 4 * S, 14, Gui.TextDim);
            float ax = ex + 40 * S;
            foreach (var u in addable)
            {
                float bw = Gui.Measure(u.Name, 13, FontKind.Bold).X + 16 * S;
                if (ax + bw > ex + ew) { ax = ex + 40 * S; y += 28 * S; }
                if (Gui.Button(new Rectangle(ax, y, bw, 24 * S), u.Name, total < max, $"Add a {u.Name} battalion", false, 13)) _design.Add((u, 1));
                ax += bw + 4 * S;
            }
            y += 32 * S;
        }

        // preview
        var preview = G.Designer.Preview(_designName, _design);
        Gui.Section("Division", ex, y, ew);
        y += 26 * S;
        if (preview.Units.Count > 0)
        {
            var shown = new[] { "max_strength", "max_organisation", "soft_attack", "hard_attack", "defense", "breakthrough", "armor", "piercing", "speed", "combat_width", "supply_use" }
                .Where(k => preview.Stats.ContainsKey(k)).ToList();
            float cw = ew / 2;
            for (int i = 0; i < shown.Count; i++)
            {
                float sx = ex + (i % 2) * cw, sy = y + (i / 2) * 20 * S;
                Gui.Label(Localisation.GetOrPretty(shown[i]), sx, sy, 14, Gui.TextDim);
                Gui.Label($"{preview.Stats[shown[i]]:0.##}", sx + cw - 60 * S, sy, 14, Gui.Text, FontKind.Bold);
            }
            y += (shown.Count + 1) / 2 * 20 * S + 8 * S;
            string costs = $"Manpower {preview.Manpower:0}   Training {preview.BuildDays} days" + (G.Economy.GoldCosts ? $"   Gold {preview.Cost:0}" : "");
            Gui.Label(costs, ex, y, 14);
            y += 20 * S;
            if (preview.Equipment.Count > 0)
            {
                y += Gui.Wrapped("Equipment: " + string.Join(", ", preview.Equipment.Select(kv => $"{kv.Value:0} {G.Data.Equipment.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}")), ex, y, ew, 14, Gui.TextDim);
            }
        }
        // actions
        var reason = G.Designer.CannotSaveReason(player, _designName, _design);
        float by = r.Y + r.Height - 42 * S, bw2 = (ew - 12 * S) / 4;
        if (reason != null) Gui.Label(reason, ex, by - 22 * S, 14, Gui.Bad);
        if (Gui.Button(new Rectangle(ex, by, bw2, 30 * S), "Save new", reason == null, "Save as a new template", false, 14))
        {
            var t = G.Designer.Create(player, _designName, _design);
            if (t != null) { LoadDesign(t); Ui.SetStatus($"Template '{t.Name}' saved"); }
        }
        bool canEdit = _designBase != null && G.Designer.CanEdit(player, _designBase);
        if (Gui.Button(new Rectangle(ex + bw2 + 4 * S, by, bw2, 30 * S), "Update", reason == null && canEdit,
                canEdit ? "Change this template - its divisions change too" : "Only your own designs can be changed (save a new one)", false, 14))
            if (G.Designer.Update(player, _designBase!, _designName, _design)) Ui.SetStatus($"Template '{_designName}' updated");
        if (Gui.Button(new Rectangle(ex + 2 * (bw2 + 4 * S), by, bw2, 30 * S), "Delete", canEdit, null, false, 14))
        {
            var err = G.Designer.Delete(player, _designBase!);
            Ui.SetStatus(err ?? "Template deleted");
            if (err == null) { _designBase = null; _design.Clear(); }
        }
        if (Gui.Button(new Rectangle(ex + 3 * (bw2 + 4 * S), by, bw2, 30 * S), "Train", _designBase != null && reason == null, "Open the recruit menu with this template", false, 14))
        {
            Ui.Left = LeftPanel.Recruit;
            Ui.RecruitTemplate = _designBase;
        }
    }
}
