using Burger.Core.Model;
using Burger.Core.Technology;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private string _techCategory = "";

    /// <summary>Technology: research slots and progress, what can be researched now, and what the country knows by category.</summary>
    private void DrawTechnologyPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(2000);
        Gui.Panel(r, "Technology");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        var T = G.Tech;
        var rules = G.Rules.Tech;
        double points = rules.MonthlyPoints(G, player);
        int slots = rules.Slots(G, player);
        Gui.Label($"Research: {points:0.0} points a month, {slots} slot(s). Known: {T.Count(player)} of {T.Defs.Count}", x, y, 15, Gui.TextDim);
        y += 26 * S;

        // the research slots
        var current = T.SlotsOf(player).ToList();
        for (int i = 0; i < slots; i++)
        {
            var box = new Rectangle(x, y, w - 8 * S, 44 * S);
            Card(box, new Color(40, 46, 58, 255), i < current.Count);
            if (i < current.Count)
            {
                var slot = current[i];
                double cost = rules.Cost(G, player, slot.Tech);
                Gui.Label(slot.Tech.Name, x + 8 * S, y + 4 * S, 15, Gui.Gold, FontKind.Bold);
                Gui.Bar(new Rectangle(x + 8 * S, y + 26 * S, w - 80 * S, 10 * S), (float)Math.Clamp(slot.Progress / Math.Max(1, cost), 0, 1), new Color(110, 170, 230, 255));
                if (Gui.Button(new Rectangle(x + w - 62 * S, y + 8 * S, 50 * S, 26 * S), "Drop", true, "Stop researching (the progress is lost)", false, 13)) T.Cancel(player, slot);
                if (Gui.Hover(box)) Gui.Tooltip(TechTip(slot.Tech, player));
            }
            else Gui.Label("Free slot - pick a technology below", x + 8 * S, y + 13 * S, 14, Gui.TextDim);
            y += 50 * S;
        }

        // categories
        var cats = T.Defs.Values.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        if (_techCategory.Length == 0 || !cats.Contains(_techCategory)) _techCategory = cats.FirstOrDefault() ?? "";
        float bx = x;
        foreach (var c in cats)
        {
            float bw = Math.Max(54 * S, Gui.Measure(c, 12).X + 14 * S);
            if (bx + bw > x + w) break;
            if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), c, true, $"{T.Defs.Values.Count(t => t.Category == c && T.Has(player, t.Key))} of {T.Defs.Values.Count(t => t.Category == c)} known", c == _techCategory, 12)) _techCategory = c;
            bx += bw + 3 * S;
        }
        y += 30 * S;

        var list = T.Defs.Values.Where(t => t.Category == _techCategory).OrderBy(t => t.Year).ThenBy(t => t.Order).ToList();
        float rowH = 40 * S;
        float top = Gui.BeginScroll("techs", new Rectangle(x, y, w, r.Bottom() - y - 10 * S), list.Count * (rowH + 4 * S));
        float dy = top;
        foreach (var t in list)
        {
            var box = new Rectangle(x, dy, w - 8 * S, rowH);
            bool known = T.Has(player, t.Key);
            string? why = known ? "Known" : T.CannotResearch(player, t);
            bool busy = current.Any(s => s.Tech == t);
            Card(box, known ? new Color(40, 58, 44, 255) : busy ? new Color(40, 50, 70, 255) : new Color(36, 38, 44, 255), busy);
            Gui.Label(Gui.Fit(t.Name, w - 150 * S, 15, FontKind.Bold), x + 8 * S, dy + 4 * S, 15, known ? Gui.Good : Gui.Text, FontKind.Bold);
            string sub = known ? "known" : $"{t.Year:0}, {rules.Cost(G, player, t):0} points";
            Gui.Label(sub, x + 8 * S, dy + 22 * S, 12, Gui.TextDim);
            if (Gui.Hover(box)) Gui.Tooltip(TechTip(t, player));
            if (!known && !busy && Gui.Button(new Rectangle(x + w - 90 * S, dy + 6 * S, 76 * S, 26 * S), "Research", why == null, why ?? TechTip(t, player), false, 13))
                Ui.SetStatus(T.Start(player, t) ?? $"Researching {t.Name}");
            dy += rowH + 4 * S;
        }
        Gui.EndScroll();
    }

    private string TechTip(TechDef t, Country c)
    {
        var parts = new List<string> { t.Name + $" ({t.Year:0})" };
        if (t.Desc.Length > 0) parts.Add(t.Desc);
        if (t.Requires.Count > 0) parts.Add("Needs: " + string.Join(", ", t.Requires.Select(q => (G.Tech.Has(c, q) ? "[OK] " : "[X] ") + G.Tech.Defs[q].Name)));
        if (t.Unlocks.Count > 0) parts.Add("Unlocks: " + string.Join(", ", t.Unlocks));
        if (t.Modifier.Count > 0) parts.Add("Gives: " + string.Join(", ", t.Modifier.Select(kv => $"{kv.Key.Replace('_', ' ')} {kv.Value:+0.##;-0.##}")));
        return string.Join("\n", parts);
    }
}

internal static class RectExtensions
{
    public static float Bottom(this Rectangle r) => r.Y + r.Height;
}
