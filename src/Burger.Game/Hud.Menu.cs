using System.Numerics;
using Burger.Core;
using Burger.Core.Mechanics;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The left menu bar (military, recruit, characters, decisions, custom panels), the recruit menu and the world overview.</summary>
public sealed partial class Hud
{
    private enum MenuIcon { Military, Recruit, Characters, Decisions, Production, Designer, Navy, Air }

    private float LeftMenuW => 66 * S;

    /// <summary>Card background for list rows inside panels.</summary>
    private void Card(Rectangle r, Color bg, bool highlight = false)
    {
        Gui.Gradient(r, Gui.Lighter(bg, 0.06f), Gui.Darker(bg, 0.15f));
        Raylib.DrawRectangleLinesEx(r, 1, highlight ? Gui.PanelBorder : new Color(0, 0, 0, 150));
    }

    private void DrawLeftMenu()
    {
        var player = G.World.Player;
        if (player == null) return;
        var items = new List<(string Label, string Tip, bool Active, Action Click, MenuIcon? Icon, PanelDef? Panel, int Badge)>();
        if (Has(FeatureSet.Military))
        {
            items.Add(("Military", "Armies, generals and fronts (F1)", Ui.Left == LeftPanel.Military, () => ToggleLeft(LeftPanel.Military), MenuIcon.Military, null, 0));
            items.Add(("Recruit", "Train new divisions (R)", Ui.Left == LeftPanel.Recruit, () => ToggleLeft(LeftPanel.Recruit), MenuIcon.Recruit, null, player.Recruitment.Count));
            if (Has(FeatureSet.DivisionDesigner))
                items.Add(("Designer", "Design your own division templates", Ui.Left == LeftPanel.Designer, () => ToggleLeft(LeftPanel.Designer), MenuIcon.Designer, null, 0));
            if (Has(FeatureSet.Navy))
                items.Add(("Navy", "Fleets, ships and transports (N)", Ui.Left == LeftPanel.Navy, () => ToggleLeft(LeftPanel.Navy), MenuIcon.Navy, null, G.Navy.OrdersOf(player).Count));
            if (Has(FeatureSet.Air))
                items.Add(("Air", "Air wings, air bases and missions", Ui.Left == LeftPanel.Air, () => ToggleLeft(LeftPanel.Air), MenuIcon.Air, null, G.Air.OrdersOf(player).Count));
        }
        if (Has(FeatureSet.Production))
            items.Add(("Production", "Factories, production lines, construction and the equipment stockpile (P)", Ui.Left == LeftPanel.Production, () => ToggleLeft(LeftPanel.Production), MenuIcon.Production, null, player.Construction.Count));
        if (Has(FeatureSet.Characters))
            items.Add(("Characters", "Ruler, heir, family and interactions (F3)", Ui.Left == LeftPanel.Characters, () => ToggleLeft(LeftPanel.Characters), MenuIcon.Characters, null, 0));
        if (G.Decisions.Defs.Count > 0)
        {
            int available = G.Decisions.Defs.Values.Count(d => G.Decisions.IsVisible(d, player) && G.Decisions.CanTake(d, player));
            items.Add(("Decisions", "Decisions you can take (F6)", Ui.Left == LeftPanel.Decisions, () => ToggleLeft(LeftPanel.Decisions), MenuIcon.Decisions, null, available));
        }
        foreach (var p in G.Interface.Panels)
        {
            if (!PanelVisible(p, player)) continue;
            var panel = p;
            items.Add((p.ButtonText, p.Title + (p.Hotkey != null ? $" ({p.Hotkey})" : ""), Ui.Left == LeftPanel.Custom && _customPanel == p, () => OpenCustomPanel(panel), null, p, 0));
        }

        if (items.Count == 0) return;
        float bs = LeftMenuW - 12 * S, gap = 4 * S;
        var bar = new Rectangle(4 * S, TopH + 8 * S, LeftMenuW, items.Count * (bs + gap) - gap + 12 * S);
        Gui.Block(bar);
        Gui.Frame(bar, Gui.MenuBar);
        float y = bar.Y + 6 * S;
        foreach (var it in items)
        {
            var r = new Rectangle(bar.X + 6 * S, y, bs, bs);
            bool hover = Gui.Hover(r);
            Gui.ButtonFrame(r, true, hover, it.Active);
            var iconArea = new Rectangle(r.X, r.Y + 3 * S, r.Width, r.Height - 19 * S);
            var col = it.Active ? Color.White : hover ? Gui.Gold : Gui.Text;
            if (it.Icon != null) DrawMenuIcon(it.Icon.Value, iconArea, col);
            else DrawPanelIcon(it.Panel!, iconArea, col);
            string label = Gui.Fit(it.Label, r.Width - 4 * S, 11, FontKind.Bold);
            var m = Gui.Measure(label, 11, FontKind.Bold);
            Gui.Label(label, r.X + (r.Width - m.X) / 2, r.Y + r.Height - m.Y - 3 * S, 11, col, FontKind.Bold);
            if (it.Badge > 0) Badge(new Vector2(r.X + r.Width - 3 * S, r.Y + 4 * S), it.Badge.ToString(), it.Icon == MenuIcon.Decisions ? new Color(60, 150, 60, 255) : Gui.Accent);
            if (hover) Gui.Tooltip(it.Tip);
            Gui.Block(r);
            if (Gui.Clicked(r)) it.Click();
            y += bs + gap;
        }
    }

    private void DrawPanelIcon(PanelDef p, Rectangle r, Color col)
    {
        var tex = Assets.Texture(p.Icon);
        float s = Math.Min(r.Width, r.Height) * 0.8f;
        if (tex != null)
        {
            Gui.Image(tex.Value, new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s));
            return;
        }
        // no icon file: the first letters of the button text in the title font
        string t = new string(p.ButtonText.Where(char.IsLetterOrDigit).Take(2).ToArray());
        if (t.Length == 0) t = "?";
        var m = Gui.Measure(t, 20, FontKind.Header);
        Gui.LabelShadow(t, r.X + (r.Width - m.X) / 2, r.Y + (r.Height - m.Y) / 2, 20, col, FontKind.Header);
    }

    /// <summary>Simple vector icons so the menu works without any image files.</summary>
    private void DrawMenuIcon(MenuIcon icon, Rectangle r, Color col)
    {
        float s = Math.Min(r.Width, r.Height) * 0.78f;
        var c = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2);
        float h = s / 2, t = Math.Max(2f, 2.4f * S);
        var dark = new Color(0, 0, 0, 160);
        switch (icon)
        {
            case MenuIcon.Military:
                // crossed swords
                foreach (var dir in new[] { -1f, 1f })
                {
                    var a = c + new Vector2(-h * dir, h);
                    var b = c + new Vector2(h * 0.85f * dir, -h * 0.85f);
                    Raylib.DrawLineEx(a + new Vector2(1, 1), b + new Vector2(1, 1), t + 1, dark);
                    Raylib.DrawLineEx(a, b, t, col);
                    var g = Vector2.Lerp(a, b, 0.22f);
                    var perp = Vector2.Normalize(new Vector2(-(b - a).Y, (b - a).X)) * h * 0.28f;
                    Raylib.DrawLineEx(g - perp, g + perp, t, col);
                }
                break;
            case MenuIcon.Recruit:
                // a helmet with a plus
                Raylib.DrawCircleSector(c + new Vector2(0, h * 0.25f), h * 0.85f, 180, 360, 16, col);
                Raylib.DrawRectangleRec(new Rectangle(c.X - h, c.Y + h * 0.2f, 2 * h, t), col);
                Raylib.DrawLineEx(c + new Vector2(h * 0.55f, -h * 0.85f), c + new Vector2(h * 0.55f, -h * 0.05f), t, Gui.Good);
                Raylib.DrawLineEx(c + new Vector2(h * 0.15f, -h * 0.45f), c + new Vector2(h * 0.95f, -h * 0.45f), t, Gui.Good);
                break;
            case MenuIcon.Characters:
                // head and shoulders
                Raylib.DrawCircleV(c + new Vector2(0, -h * 0.38f), h * 0.36f, col);
                Raylib.DrawCircleSector(c + new Vector2(0, h * 0.75f), h * 0.78f, 180, 360, 20, col);
                break;
            case MenuIcon.Production:
                // a factory with a chimney
                Raylib.DrawRectangleRec(new Rectangle(c.X - h, c.Y - h * 0.1f, 2 * h, h * 1.0f), col);
                Raylib.DrawTriangle(c + new Vector2(-h, -h * 0.1f), c + new Vector2(-h * 0.33f, -h * 0.1f), c + new Vector2(-h, -h * 0.55f), col);
                Raylib.DrawTriangle(c + new Vector2(-h * 0.33f, -h * 0.1f), c + new Vector2(h * 0.33f, -h * 0.1f), c + new Vector2(-h * 0.33f, -h * 0.55f), col);
                Raylib.DrawRectangleRec(new Rectangle(c.X + h * 0.45f, c.Y - h, h * 0.35f, h * 0.95f), col);
                break;
            case MenuIcon.Designer:
                // a grid of battalions
                for (int gx = 0; gx < 3; gx++)
                    for (int gy = 0; gy < 3; gy++)
                    {
                        var cell = new Rectangle(c.X - h + gx * h * 0.7f, c.Y - h + gy * h * 0.7f, h * 0.55f, h * 0.55f);
                        if (gx + gy < 4) Raylib.DrawRectangleRec(cell, col); else Raylib.DrawRectangleLinesEx(cell, 1.5f, col);
                    }
                break;
            case MenuIcon.Navy:
                // a ship: hull, deck house and a mast over waves
                Raylib.DrawTriangle(c + new Vector2(-h, h * 0.05f), c + new Vector2(-h * 0.65f, h * 0.55f), c + new Vector2(h, h * 0.05f), col);
                Raylib.DrawTriangle(c + new Vector2(-h * 0.65f, h * 0.55f), c + new Vector2(h * 0.7f, h * 0.55f), c + new Vector2(h, h * 0.05f), col);
                Raylib.DrawRectangleRec(new Rectangle(c.X - h * 0.45f, c.Y - h * 0.3f, h * 0.8f, h * 0.35f), col);
                Raylib.DrawLineEx(c + new Vector2(-h * 0.05f, -h * 0.3f), c + new Vector2(-h * 0.05f, -h * 0.9f), t, col);
                Raylib.DrawLineEx(c + new Vector2(-h, h * 0.8f), c + new Vector2(h, h * 0.8f), Math.Max(1, 1.6f * S), Gui.Accent);
                break;
            case MenuIcon.Air:
                // a plane seen from above: fuselage, wings and tail
                Raylib.DrawLineEx(c + new Vector2(0, -h * 0.95f), c + new Vector2(0, h * 0.85f), t * 1.6f, col);
                Raylib.DrawTriangle(c + new Vector2(0, -h * 0.35f), c + new Vector2(-h, h * 0.15f), c + new Vector2(h, h * 0.15f), col);
                Raylib.DrawTriangle(c + new Vector2(0, h * 0.5f), c + new Vector2(-h * 0.45f, h * 0.85f), c + new Vector2(h * 0.45f, h * 0.85f), col);
                break;
            case MenuIcon.Decisions:
                // a scroll with lines
                var page = new Rectangle(c.X - h * 0.7f, c.Y - h * 0.85f, h * 1.4f, h * 1.7f);
                Raylib.DrawRectangleRec(page, col);
                Raylib.DrawRectangleLinesEx(page, 1, dark);
                for (int i = 0; i < 4; i++)
                {
                    float ly = page.Y + page.Height * (0.22f + i * 0.18f);
                    Raylib.DrawLineEx(new Vector2(page.X + page.Width * 0.18f, ly), new Vector2(page.X + page.Width * (i == 3 ? 0.55f : 0.82f), ly), Math.Max(1, 1.4f * S), dark);
                }
                break;
        }
    }

    // ===============================================================================================
    // Recruit menu: pick a template, then click your locations on the map
    // ===============================================================================================
    private void RecruitAt(Country player, DivisionTemplate t, Location l)
    {
        var reason = G.Rules.Economy.CannotRecruitReason(G, player, t, l);
        if (reason != null) { Ui.SetStatus($"Cannot train in {l.DisplayName}: {reason}"); return; }
        var err = G.Economy.Recruit(player, t, l);
        Ui.SetStatus(err ?? $"Training {t.Name} in {l.DisplayName}");
    }

    private void DrawRecruitPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(2000);
        Gui.Panel(r, "Recruit & Train");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var templates = G.Data.Templates.Values
            .Where(t => G.Rules.Economy.CannotRecruitReason(G, player, t, player.Capital ?? G.World.Locations[0]) != "This template belongs to another country").ToList();
        string hint = Ui.RecruitTemplate == null
            ? "Pick a division template, then click your locations on the map to train it there."
            : $"Click one of your locations to train a {Ui.RecruitTemplate.Name}. Right-click or Esc to stop.";
        y += Gui.Wrapped(hint, x, y, w, 14, Ui.RecruitTemplate == null ? Gui.TextDim : Gui.Gold) + 8 * S;

        Gui.Section("Division templates", x, y, w);
        y += 26 * S;
        float rowH = 50 * S;
        float listH = Math.Min(templates.Count * (rowH + 4 * S), r.Height * 0.45f);
        float top = Gui.BeginScroll("templates", new Rectangle(x, y, w, listH), templates.Count * (rowH + 4 * S));
        float ty = top;
        foreach (var t in templates)
        {
            var box = new Rectangle(x, ty, w - 8 * S, rowH);
            bool sel = Ui.RecruitTemplate == t;
            bool hover = Gui.Hover(box);
            Card(box, sel ? new Color(78, 62, 34, 255) : hover ? new Color(50, 50, 56, 255) : new Color(36, 38, 44, 255), sel);
            Gui.Label(t.Name, box.X + 8 * S, box.Y + 5 * S, 16, sel ? Color.White : Gui.Gold, FontKind.Bold);
            string comp = string.Join(", ", t.Units.Select(u => $"{u.Count}x {u.Type.Name}"));
            Gui.Label(Gui.Fit(comp, box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 27 * S, 13, Gui.TextDim);
            string cost = (G.Economy.GoldCosts ? $"{t.Cost:0}g  " : "") + $"{t.Manpower:0}mp  {t.BuildDays}d";
            if (G.Production.Enabled && t.Equipment.Count > 0)
            {
                double share = G.Production.AvailableShare(player, t.Equipment);
                cost += $"  eq {share:P0}";
            }
            var cm = Gui.Measure(cost, 13, FontKind.Bold);
            Gui.Label(cost, box.X + box.Width - cm.X - 8 * S, box.Y + 6 * S, 13, Gui.Text, FontKind.Bold);
            if (hover)
            {
                string equip = G.Production.Enabled && t.Equipment.Count > 0
                    ? "\nEquipment: " + string.Join(", ", t.Equipment.Select(kv => $"{kv.Value:0} {G.Data.Equipment.GetValueOrDefault(kv.Key)?.Name ?? kv.Key} ({G.Production.Stock(player, kv.Key):0} in stock)")) : "";
                Gui.Tooltip($"{t.Name}\nCost: " + (G.Economy.GoldCosts ? $"{t.Cost:0} gold, " : "") + $"{t.Manpower:0} manpower{equip}\nTraining time: {t.BuildDays} days\n{comp}\n\n" +
                            string.Join("\n", t.Stats.Select(kv => $"{Localisation.GetOrPretty(kv.Key)}: {kv.Value:0.##}")) + "\n\nClick to pick it, then click your locations on the map.");
                if (Gui.Clicked(box)) Ui.RecruitTemplate = sel ? null : t;
            }
            Gui.Block(box);
            ty += rowH + 4 * S;
        }
        Gui.EndScroll();
        y += listH + 8 * S;
        if (Ui.RecruitTemplate != null && player.Capital != null)
        {
            var cap = player.Capital;
            var reason = G.Rules.Economy.CannotRecruitReason(G, player, Ui.RecruitTemplate, cap);
            if (Gui.Button(new Rectangle(x, y, w, 28 * S), $"Train at the capital ({cap.DisplayName})", reason == null, reason ?? "Train one division in the capital", false, 15))
                RecruitAt(player, Ui.RecruitTemplate, cap);
            y += 36 * S;
        }

        Gui.Section($"In training ({player.Recruitment.Count})", x, y, w);
        y += 26 * S;
        if (player.Recruitment.Count == 0)
        {
            Gui.Label("Nothing in training.", x, y, 14, Gui.TextDim);
            return;
        }
        float qTop = Gui.BeginScroll("training", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), player.Recruitment.Count * 28 * S);
        float qy = qTop;
        foreach (var o in player.Recruitment.ToList())
        {
            float done = 1 - o.HoursLeft / (float)Math.Max(1, o.TotalHours);
            Gui.Label(Gui.Fit($"{o.Template.Name} - {o.Location.DisplayName}", w - 170 * S, 14), x, qy + 3 * S, 14);
            if (o.WaitingForEquipment)
            {
                var wr = new Rectangle(x + w - 160 * S, qy + 1 * S, 110 * S, 20 * S);
                Gui.Label("needs equipment", wr.X, qy + 3 * S, 13, Gui.Bad);
                if (Gui.Hover(wr)) Gui.Tooltip("Training is done. The division deploys when the stockpile has enough equipment (Production menu).");
            }
            else Gui.Bar(new Rectangle(x + w - 160 * S, qy + 7 * S, 110 * S, 10 * S), done, Gui.Gold);
            if (Gui.Button(new Rectangle(x + w - 44 * S, qy, 36 * S, 22 * S), "x", true, "Cancel (refunds the remaining gold and all manpower)", false, 13))
                G.Economy.CancelRecruitment(player, o);
            qy += 28 * S;
        }
        Gui.EndScroll();
    }

    // ===============================================================================================
    // World overview (top right)
    // ===============================================================================================
    private void DrawWorldPanel()
    {
        var r = RightRect(2000);
        Gui.Panel(r, "World");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var player = G.World.Player;
        var countries = G.World.Countries.Where(c => c.Exists)
            .OrderByDescending(c => c.OwnedStates.Sum(s => s.Locations.Count)).ToList();
        Gui.Label($"{countries.Count} countries, {G.World.Wars.Count} war(s)", x, y, 14, Gui.TextDim);
        y += 24 * S;
        float rowH = 44 * S;
        float top = Gui.BeginScroll("world", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), countries.Count * (rowH + 4 * S));
        float cy = top;
        int rank = 1;
        foreach (var c in countries)
        {
            var box = new Rectangle(x, cy, w - 8 * S, rowH);
            bool hover = Gui.Hover(box);
            bool atWar = player != null && c != player && G.World.AtWar(player, c);
            Card(box, c == player ? new Color(60, 52, 34, 255) : hover ? new Color(50, 50, 56, 255) : new Color(36, 38, 44, 255), c == player);
            Gui.Label($"{rank++}", box.X + 6 * S, box.Y + 13 * S, 14, Gui.TextDim, FontKind.Bold);
            Gui.Flag(c, new Rectangle(box.X + 28 * S, box.Y + 7 * S, 45 * S, 30 * S));
            Gui.Label(Gui.Fit(c.Name, box.Width - 200 * S, 16, FontKind.Bold), box.X + 82 * S, box.Y + 4 * S, 16, atWar ? Gui.Bad : Gui.Text, FontKind.Bold);
            int locs = c.OwnedStates.Sum(s => s.Locations.Count);
            int divs = G.World.Divisions.Count(d => d.Owner == c);
            Gui.Label($"{c.OwnedStates.Count} states, {locs} locations", box.X + 82 * S, box.Y + 24 * S, 13, Gui.TextDim);
            string right = Has(FeatureSet.Military) ? $"{divs} div." : "";
            var m = Gui.Measure(right, 13, FontKind.Bold);
            Gui.Label(right, box.X + box.Width - m.X - 8 * S, box.Y + 6 * S, 13, Gui.Text, FontKind.Bold);
            int wars = G.World.WarsOf(c).Count();
            if (wars > 0)
            {
                string ws = wars == 1 ? "at war" : $"{wars} wars";
                var wm = Gui.Measure(ws, 13);
                Gui.Label(ws, box.X + box.Width - wm.X - 8 * S, box.Y + 24 * S, 13, Gui.Bad);
            }
            if (hover)
            {
                Gui.Tooltip($"{c.Name} ({c.Tag})\nRuler: {c.Ruler?.DisplayName ?? "none"}\nClick to open it and move the camera there");
                if (Gui.Clicked(box))
                {
                    Ui.SelectedCountry = c;
                    Ui.Left = LeftPanel.Country;
                    if (c.Capital != null) Cam.LookAt(Map.LocationPos(c.Capital));
                }
            }
            Gui.Block(box);
            cy += rowH + 4 * S;
        }
        Gui.EndScroll();
    }
}
