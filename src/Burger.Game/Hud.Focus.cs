using System.Numerics;
using Burger.Core.Mechanics;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private string _focusSelected = "";

    /// <summary>
    /// National focuses: a wide window with the whole tree scaled to fit (laid out by x and y, elbow lines from prerequisites), the current focus with its
    /// progress on top, and a strip at the bottom with what the selected focus needs, what it gives, and a Start button.
    /// </summary>
    private void DrawFocusPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        float left = LeftMenuW + 10 * S;
        var r = new Rectangle(left, TopH + 8 * S, Raylib.GetScreenWidth() - left - 12 * S, Raylib.GetScreenHeight() - TopH - 20 * S);
        Gui.Panel(r, "National Focus");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 14 * S, y = r.Y + 36 * S, w = r.Width - 28 * S;
        var F = G.Focus;
        var focuses = F.FocusesOf(player).ToList();
        if (focuses.Count == 0) { Gui.Label("No focus tree for this country.", x, y, 15, Gui.TextDim); return; }
        var cur = F.Current(player);
        double days = cur == null ? 0 : G.Rules.Focus.Days(G, player, cur);
        if (cur != null)
        {
            Gui.Label($"Working on {cur.Name}: {F.Progress(player):0} of {days:0} days", x, y, 15, Gui.Gold, FontKind.Bold);
            Gui.Bar(new Rectangle(x, y + 22 * S, w - 90 * S, 10 * S), (float)Math.Clamp(F.Progress(player) / Math.Max(1, days), 0, 1), new Color(110, 170, 230, 255));
            if (Gui.Button(new Rectangle(x + w - 80 * S, y + 12 * S, 76 * S, 26 * S), "Drop", true, "Stop this focus (the progress is lost)", false, 13)) F.Cancel(player);
        }
        else Gui.Label($"Pick a focus to work on. Completed: {F.DoneCount(player)} of {focuses.Count}", x, y, 15, Gui.TextDim);
        y += 46 * S;

        // the detail strip at the bottom
        float stripH = 120 * S;
        var strip = new Rectangle(x, r.Y + r.Height - stripH - 12 * S, w, stripH);
        var area = new Rectangle(x, y, w, strip.Y - y - 10 * S);

        // fit the grid into the area: columns and rows shrink (down to a minimum card), then the tree scrolls
        int cols = focuses.Max(f => f.X) + 1, rows = focuses.Max(f => f.Y) + 1;
        float gx = 20 * S, gy = 26 * S;
        float cw = Math.Clamp((area.Width - gx * (cols - 1)) / cols, 110 * S, 210 * S);
        float ch = Math.Clamp((area.Height - gy * (rows - 1)) / rows, 54 * S, 70 * S);
        float gridW = cols * cw + (cols - 1) * gx;
        float ox = area.X + Math.Max(0, (area.Width - gridW) / 2);
        float top = Gui.BeginScroll("focuses", area, rows * (ch + gy) - gy + 4 * S);
        Rectangle Box(FocusDef f) => new(ox + f.X * (cw + gx), top + f.Y * (ch + gy), cw, ch);

        // connectors first (under the cards): down from the prerequisite, across, down into the focus
        foreach (var f in focuses)
            foreach (var p in f.Prerequisite.Concat(f.Requires).Distinct())
                if (F.Defs.TryGetValue(p, out var pf) && pf.Tree == f.Tree)
                {
                    var a = Box(pf); var b = Box(f);
                    var col = F.IsDone(player, pf.Key) ? new Color(120, 200, 130, 255) : new Color(120, 120, 135, 255);
                    float ax = a.X + a.Width / 2, ay = a.Y + a.Height, bx = b.X + b.Width / 2, by = b.Y, mid = (ay + by) / 2;
                    float t = 2 * S;
                    Raylib.DrawLineEx(new Vector2(ax, ay), new Vector2(ax, mid), t, col);
                    Raylib.DrawLineEx(new Vector2(ax, mid), new Vector2(bx, mid), t, col);
                    Raylib.DrawLineEx(new Vector2(bx, mid), new Vector2(bx, by), t, col);
                }
        // mutually exclusive pairs: a red dash between them
        foreach (var f in focuses)
            foreach (var m in f.MutuallyExclusive)
                if (F.Defs.TryGetValue(m, out var mf) && string.CompareOrdinal(f.Key, mf.Key) < 0)
                {
                    var a = Box(f); var b = Box(mf);
                    Raylib.DrawLineEx(new Vector2(a.X + a.Width, a.Y + a.Height / 2), new Vector2(b.X, b.Y + b.Height / 2), 2 * S, new Color(200, 80, 80, 255));
                }

        foreach (var f in focuses)
        {
            var box = Box(f);
            bool done = F.IsDone(player, f.Key), busy = cur == f, sel = _focusSelected == f.Key;
            string? why = done ? "Completed" : F.CannotStart(player, f);
            Card(box, done ? new Color(40, 62, 46, 255) : busy ? new Color(40, 55, 80, 255) : why == null ? new Color(58, 58, 68, 255) : new Color(32, 33, 38, 255), busy || sel);
            Gui.Label(Gui.Fit(f.Name, cw - 12 * S, 14, FontKind.Bold), box.X + 6 * S, box.Y + 5 * S, 14, done ? Gui.Good : why == null ? Gui.Text : Gui.TextDim, FontKind.Bold);
            string sub = done ? "done" : busy ? $"{F.Progress(player):0}/{days:0} days" : why == null ? $"{G.Rules.Focus.Days(G, player, f):0} days" : "locked";
            Gui.Label(sub, box.X + 6 * S, box.Y + box.Height - 20 * S, 12, busy ? Gui.Gold : Gui.TextDim);
            if (Gui.Hover(box) && Raylib.IsMouseButtonPressed(MouseButton.Left)) _focusSelected = f.Key;
        }
        Gui.EndScroll();

        // details
        Card(strip, new Color(30, 32, 38, 255), false);
        if (_focusSelected.Length == 0 || !F.Defs.TryGetValue(_focusSelected, out var s) || s.Tree != F.TreeOf(player))
        {
            Gui.Label("Click a focus to see what it needs and what it gives.", strip.X + 10 * S, strip.Y + 10 * S, 14, Gui.TextDim);
            return;
        }
        string? block = F.IsDone(player, s.Key) ? "Completed" : F.CannotStart(player, s);
        float tx = strip.X + 10 * S, ty = strip.Y + 8 * S, tw = strip.Width - 140 * S;
        Gui.Label(s.Name + $"  ({G.Rules.Focus.Days(G, player, s):0} days)", tx, ty, 16, Gui.Gold, FontKind.Bold);
        if (s.Desc.Length > 0) Gui.Label(Gui.Fit(s.Desc, tw, 13), tx, ty + 22 * S, 13, Gui.TextDim);
        var needs = F.Requirements(player, s).Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text).ToList();
        if (s.Prerequisite.Count > 0) needs.Insert(0, "After one of: " + string.Join(", ", s.Prerequisite.Select(k => (F.IsDone(player, k) ? "[OK] " : "[X] ") + (F.Defs.TryGetValue(k, out var d) ? d.Name : k))));
        if (s.Requires.Count > 0) needs.Insert(0, "After all of: " + string.Join(", ", s.Requires.Select(k => (F.IsDone(player, k) ? "[OK] " : "[X] ") + (F.Defs.TryGetValue(k, out var d) ? d.Name : k))));
        if (s.MutuallyExclusive.Count > 0) needs.Add("Excludes: " + string.Join(", ", s.MutuallyExclusive.Select(k => F.Defs.TryGetValue(k, out var d) ? d.Name : k)));
        Gui.Label(Gui.Fit("Needs: " + (needs.Count == 0 ? "nothing" : string.Join("; ", needs)), tw, 13), tx, ty + 44 * S, 13, Gui.Text);
        var reward = F.DescribeReward(player, s);
        Gui.Label(Gui.Fit("Gives: " + (reward.Count == 0 ? "-" : string.Join("; ", reward)), tw, 13), tx, ty + 64 * S, 13, Gui.Good);
        if (block != null && block != "Completed") Gui.Label(Gui.Fit(block, tw, 13), tx, ty + 84 * S, 13, new Color(220, 120, 110, 255));
        if (block == null && Gui.Button(new Rectangle(strip.X + strip.Width - 120 * S, strip.Y + strip.Height / 2 - 16 * S, 106 * S, 32 * S), "Start", true, "Work on this focus", false, 14))
            Ui.SetStatus(F.Start(player, s) ?? $"Focus: {s.Name}");
    }
}
