using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Population;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The population menu, the people of a state in the location panel, and the Ledger charts (population features).</summary>
public sealed partial class Hud
{
    private string _ledgerMetric = "population";
    private bool _ledgerWorld;
    public bool LedgerWorld { get => _ledgerWorld; set => _ledgerWorld = value; }
    public string LedgerMetric { get => _ledgerMetric; set => _ledgerMetric = value; }
    private string? _releaseArmed;

    public static string People(double n) => n >= 1e9 ? $"{n / 1e9:0.##}B" : n >= 1e6 ? $"{n / 1e6:0.##}M" : n >= 1e4 ? $"{n / 1e3:0}k" : n >= 1e3 ? $"{n / 1e3:0.#}k" : $"{n:0}";

    /// <summary>A stacked bar of shares with a tooltip listing them.</summary>
    private void ShareBar(Rectangle bar, List<(string Key, double Size)> parts, Func<string, (string Name, Rgb Color)> info, string title)
    {
        double total = parts.Sum(p => p.Size);
        if (total <= 0) return;
        float bx = bar.X;
        foreach (var (key, size) in parts)
        {
            float bw = (float)(size / total * bar.Width);
            if (bw <= 0) continue;
            var (_, col) = info(key);
            Raylib.DrawRectangleRec(new Rectangle(bx, bar.Y, bw, bar.Height), C(col));
            bx += bw;
        }
        Raylib.DrawRectangleLinesEx(bar, 1, new Color(0, 0, 0, 180));
        if (Gui.Hover(bar))
            Gui.Tooltip(title + "\n" + string.Join("\n", parts.Take(12).Select(p => $"{info(p.Key).Name}: {People(p.Size)} ({p.Size / total:P1})")) + (parts.Count > 12 ? $"\n... and {parts.Count - 12} more" : ""));
    }

    /// <summary>A pie chart; the hovered slice grows and its share shows in a tooltip. Returns the hovered slice or -1.</summary>
    private int Pie(Vector2 c, float r, IReadOnlyList<(string Name, double Value, Rgb Color)> parts, string title, Func<double, string>? amount = null)
    {
        double total = parts.Sum(p => Math.Max(0, p.Value));
        if (total <= 0) return -1;
        var d = Viewport.Mouse - c;
        bool inside = d.Length() <= r && Gui.Hover(new Rectangle(c.X - r, c.Y - r, 2 * r, 2 * r));
        float mouseAngle = MathF.Atan2(d.Y, d.X) * 180 / MathF.PI;
        if (mouseAngle < -90) mouseAngle += 360;
        int hover = -1;
        float a = -90;
        Raylib.DrawCircleV(c, r + 1.5f * S, new Color(0, 0, 0, 150));
        for (int i = 0; i < parts.Count; i++)
        {
            float sweep = (float)(Math.Max(0, parts[i].Value) / total * 360);
            if (sweep <= 0) continue;
            bool hov = inside && mouseAngle >= a && mouseAngle < a + sweep;
            if (hov) hover = i;
            Raylib.DrawCircleSector(c, hov ? r + 3 * S : r, a, a + sweep, Math.Max(2, (int)(sweep / 3)), C(parts[i].Color));
            a += sweep;
        }
        if (hover >= 0)
        {
            var h = parts[hover];
            Gui.Tooltip($"{title}\n{h.Name}: {h.Value / total:P1}" + (amount != null ? $" ({amount(h.Value)})" : ""));
        }
        return hover;
    }

    private List<(string, double, Rgb)> Parts(List<(string Key, double Size)> parts, Func<string, (string Name, Rgb Color)> info, int max = 8)
    {
        var result = parts.Take(max).Select(p => { var (n, col) = info(p.Key); return (n, p.Size, col); }).ToList();
        double rest = parts.Skip(max).Sum(p => p.Size);
        if (rest > 0) result.Add(("Others", rest, new Rgb(110, 110, 110)));
        return result;
    }

    private (string, Rgb) CultInfo(string k) => G.Population.Culture(k) is { } c ? (c.Name, c.Color) : (k, Rgb.Gray);
    private (string, Rgb) RelInfo(string k) => G.Population.Religion(k) is { } r ? (r.Name, r.Color) : (k, Rgb.Gray);
    private (string, Rgb) ClassInfo(string k) => G.Population.PopTypeByKey.TryGetValue(k, out var t) ? (t.Name, t.Color) : (k, Rgb.Gray);

    // ------------------------------------------------------------------------------------- population menu
    private void DrawPopulationPanel()
    {
        var player = G.World.Player;
        var P = G.Population;
        if (player == null || !P.Enabled) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 450 * S;
        Gui.Panel(r, "Population");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var states = player.OwnedStates;
        double total = P.TotalOf(player);
        double growth = states.Sum(s => P.HasPops(s) ? P.Of(s).LastGrowth : 0);
        Gui.Label($"{People(total)} people", x, y, 18, Gui.Gold, FontKind.Bold);
        string gtxt = $"{(growth >= 0 ? "+" : "-")}{People(Math.Abs(growth))} last month";
        Gui.Label(gtxt, x + w - Gui.Measure(gtxt, 14).X, y + 3 * S, 14, growth >= 0 ? Gui.Good : Gui.Bad);
        y += 28 * S;
        // the cultures, religions and classes below count the people outside our colonies
        var home = P.HomeStates(player).ToList();
        double homeTotal = home.Sum(P.TotalOf);
        double colonial = total - homeTotal;
        if (colonial >= 1)
        {
            string ct = $"{People(homeTotal)} at home, {People(colonial)} in colonies (not counted below)";
            Gui.Label(Gui.Fit(ct, w, 13), x, y, 13, Gui.TextDim);
            if (Gui.Hover(new Rectangle(x, y, w, 18 * S))) Gui.Tooltip("Colonies: states marked as colonies, or states that are not our cores and not connected by land to the capital.\nTheir people do not count for our cultures, religions and stability, and their cultures cannot be accepted.");
            y += 20 * S;
        }
        var cp = P.Of(player);
        string ident = $"Culture: {P.Culture(cp.Culture)?.Name ?? "-"}" + (P.ReligionEnabled ? $"    Religion: {P.Religion(cp.Religion)?.Name ?? "-"}" : "");
        Gui.Label(Gui.Fit(ident, w, 14), x, y, 14);
        y += 20 * S;
        var (acc, faith) = P.Shares(player);
        string shares = $"Accepted cultures: {acc:P0} of the people" + (P.ReligionEnabled ? $"    State religion: {faith:P0}" : "");
        Gui.Label(Gui.Fit(shares, w, 13), x, y, 13, Gui.TextDim);
        if (Gui.Hover(new Rectangle(x, y, w, 18 * S)))
            Gui.Tooltip("People of unaccepted cultures pay less tax, give less manpower and slowly take your culture.\n" +
                        (P.ReligionEnabled ? "People of other faiths pay a little less tax and slowly convert.\n" : "") +
                        "Both lower stability (with politics).\nCultures of your culture group count as accepted.");
        y += 24 * S;

        Gui.Section("Cultures", x, y, w);
        y += 26 * S;
        var cultures = P.Breakdown(home, p => p.Culture);
        float pr = 52 * S, listX = x + 2 * pr + 14 * S;
        float pieTop = y;
        Pie(new Vector2(x + pr, y + pr), pr, Parts(cultures, CultInfo), "Cultures", People);
        float lw = x + w - listX;
        foreach (var (key, size) in cultures.Take(5))
        {
            var (name, col) = CultInfo(key);
            bool accepted = P.IsAccepted(player, key);
            Raylib.DrawRectangleRec(new Rectangle(listX, y + 4 * S, 10 * S, 10 * S), C(col));
            Gui.Label(Gui.Fit($"{name}{(key == cp.Culture ? " (primary)" : accepted ? " (accepted)" : "")}", lw - 130 * S, 13), listX + 14 * S, y, 13, accepted ? Gui.Text : Gui.TextDim);
            Gui.Label($"{size / Math.Max(1, homeTotal):P0}", listX + lw - 112 * S, y, 13);
            if (!accepted)
            {
                var reason = P.CannotAccept(player, key);
                var (cost, cur) = P.AcceptCost(player);
                if (Gui.Button(new Rectangle(x + w - 64 * S, y - 2 * S, 64 * S, 19 * S), "Accept", reason == null,
                        reason ?? $"Accept {name} culture: its people count as ours (full tax and manpower){(cost > 0 ? $"\nCost: {cost:0} {cur}" : "")}", false, 12))
                {
                    var err = P.AcceptCulture(player, key);
                    if (err != null) Ui.SetStatus(err);
                }
            }
            y += 21 * S;
        }
        y = Math.Max(y, pieTop + 2 * pr) + 10 * S;
        if (P.ReligionEnabled)
        {
            Gui.Section("Religions", x, y, w);
            y += 26 * S;
            var rel = P.Breakdown(home, p => p.Religion);
            pieTop = y;
            Pie(new Vector2(x + pr, y + pr), pr, Parts(rel, RelInfo), "Religions", People);
            foreach (var (key, size) in rel.Take(5))
            {
                var (name, col) = RelInfo(key);
                Raylib.DrawRectangleRec(new Rectangle(listX, y + 4 * S, 10 * S, 10 * S), C(col));
                Gui.Label(Gui.Fit(name + (key == cp.Religion ? " (state religion)" : ""), lw - 60 * S, 13), listX + 14 * S, y, 13);
                string pct = $"{size / Math.Max(1, homeTotal):P0}";
                Gui.Label(pct, x + w - Gui.Measure(pct, 13).X, y, 13);
                y += 21 * S;
            }
            y = Math.Max(y, pieTop + 2 * pr) + 10 * S;
        }
        if (P.ClassesEnabled)
        {
            Gui.Section("Social classes", x, y, w);
            y += 26 * S;
            ShareBar(new Rectangle(x, y, w, 16 * S), P.Breakdown(home, p => p.Type), ClassInfo, "Social classes");
            y += 26 * S;
        }
        if (P.ColonizationEnabled)
        {
            var colonies = P.ColoniesOf(player).ToList();
            Gui.Section($"Colonies ({colonies.Count}/{P.MaxColonies(player)} colonists)", x, y, w);
            y += 26 * S;
            if (colonies.Count == 0) { Gui.Label("Click an unowned state on the map to send colonists.", x, y, 13, Gui.TextDim); y += 22 * S; }
            foreach (var s in colonies)
            {
                var sp = P.Of(s);
                Gui.Label(Gui.Fit(s.DisplayName, w * 0.45f, 14), x, y, 14);
                Gui.Bar(new Rectangle(x + w * 0.47f, y + 4 * S, w * 0.3f, 10 * S), (float)(sp.ColonyProgress / 100), new Color(110, 190, 90, 255));
                if (Gui.Button(new Rectangle(x + w - 70 * S, y - 2 * S, 70 * S, 20 * S), "Abandon", true, "Call the colonists back", false, 12)) P.AbandonColony(s);
                y += 22 * S;
            }
            y += 6 * S;
        }
        if (P.IndependenceEnabled)
        {
            var moves = P.IndependenceOf(player).OrderByDescending(m => m.Progress).ToList();
            if (moves.Count > 0)
            {
                Gui.Section("Independence movements", x, y, w);
                y += 26 * S;
                foreach (var m in moves.Take(4))
                {
                    var (name, col) = CultInfo(m.Culture);
                    int n = P.SeparatistStates(player, m.Culture).Count;
                    Raylib.DrawRectangleRec(new Rectangle(x, y + 4 * S, 10 * S, 10 * S), C(col));
                    Gui.Label(Gui.Fit($"{name} ({n} states)", w * 0.4f, 13), x + 14 * S, y, 13);
                    var bar = new Rectangle(x + w * 0.42f, y + 4 * S, w * 0.2f, 10 * S);
                    Gui.Bar(bar, (float)(m.Progress / 100), new Color(200, 80, 70, 255));
                    if (Gui.Hover(bar)) Gui.Tooltip($"{m.Progress:0}% - at 100% they declare independence (and fight for it).\nAccepting their culture ends the movement.");
                    var why = P.CannotSuppressIndependence(m);
                    if (Gui.Button(new Rectangle(x + w * 0.64f, y - 2 * S, w * 0.18f, 19 * S), "Suppress", why == null,
                            why ?? $"Set the movement back ({P.SuppressIndependenceCost(m):0}); costs some stability. Each time costs more.", false, 11))
                        P.SuppressIndependence(m);
                    bool armed = _releaseArmed == m.Culture;
                    if (Gui.Button(new Rectangle(x + w * 0.83f, y - 2 * S, w * 0.17f, 19 * S), armed ? "Sure?" : "Release", true,
                            $"Let their {n} states go in peace as an independent country" + (armed ? "\nClick again to confirm" : ""), armed, 11))
                    {
                        if (armed) { P.GrantIndependence(m); _releaseArmed = null; }
                        else _releaseArmed = m.Culture;
                    }
                    y += 22 * S;
                }
                y += 6 * S;
            }
        }
        // most populous states
        Gui.Section("States by population", x, y, w);
        y += 26 * S;
        var list = states.Where(P.HasPops).OrderByDescending(P.TotalOf).ToList();
        float top = Gui.BeginScroll("popstates", new Rectangle(x, y, w, r.Y + r.Height - y - 10 * S), list.Count * 22 * S);
        float ly = top;
        foreach (var s in list)
        {
            var sp = P.Of(s);
            double cap = P.Capacity(s);
            var row = new Rectangle(x, ly, w - 10 * S, 20 * S);
            bool hover = Gui.Hover(row);
            if (hover) Raylib.DrawRectangleRec(row, new Color(255, 255, 255, 18));
            Gui.Label(Gui.Fit(s.DisplayName, w * 0.5f, 14), x + 2 * S, ly + 1 * S, 14, s.Cores.Contains(player) ? Gui.Text : Gui.TextDim);
            Gui.Label(People(sp.Total), x + w * 0.52f, ly + 1 * S, 14);
            Gui.Bar(new Rectangle(x + w * 0.70f, ly + 6 * S, w * 0.26f, 8 * S), (float)Math.Min(1, sp.Total / Math.Max(1, cap)), new Color(200, 170, 90, 255));
            if (hover)
            {
                Gui.Tooltip($"{s.DisplayName}: {People(sp.Total)} people (room for {People(cap)})\n{(s.Cores.Contains(player) ? "Core state" : "Not a core state")}\nClick to look at it");
                if (Gui.Clicked(row))
                {
                    var loc = s.Locations.FirstOrDefault(l => l.IsLand) ?? s.Locations[0];
                    Ui.SelectedLocation = loc;
                    Map.Selected = loc;
                    Cam.LookAt(Map.LocationPos(loc));
                    Ui.Left = LeftPanel.Location;
                }
            }
            ly += 22 * S;
        }
        Gui.EndScroll();
    }

    /// <summary>People, colonization and gold buildings of a state (in the location panel). Returns the new y.</summary>
    private float DrawStatePopulation(State st, float x, float y, float w)
    {
        var P = G.Population;
        if (!P.Enabled) return y;
        var player = G.World.Player;
        var sp = P.Of(st);
        Gui.Section("People", x, y, w);
        y += 26 * S;
        double cap = P.Capacity(st);
        Gui.Label($"{People(sp.Total)} people", x, y, 15, Gui.Gold);
        string g = $"{(sp.LastGrowth >= 0 ? "+" : "-")}{People(Math.Abs(sp.LastGrowth))}/month, room for {People(cap)}";
        Gui.Label(Gui.Fit(g, w - 130 * S, 13), x + 130 * S, y + 2 * S, 13, Gui.TextDim);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S)))
            Gui.Tooltip("People grow towards the state's room (capacity); buildings and modifiers (pop_growth, pop_capacity) change both.\nTax and manpower grow with the people.");
        y += 22 * S;
        if (sp.Pops.Count > 0)
        {
            // small pies: cultures, religions, classes
            float r = 30 * S, px = x;
            void SmallPie(string title, List<(string Key, double Size)> parts, Func<string, (string, Rgb)> info)
            {
                Pie(new Vector2(px + r, y + r), r, Parts(parts, info), title, People);
                string top = parts.Count > 0 ? info(parts[0].Key).Item1 : "-";
                Gui.Label(Gui.Fit(title, 2 * r + 60 * S, 11), px + 2 * r + 6 * S, y + r - 16 * S, 11, Gui.TextDim);
                Gui.Label(Gui.Fit(top, 2 * r + 60 * S, 12), px + 2 * r + 6 * S, y + r - 2 * S, 12);
                px += 2 * r + 76 * S;
            }
            SmallPie("Cultures", P.Breakdown(new[] { st }, p => p.Culture), CultInfo);
            if (P.ReligionEnabled) SmallPie("Religions", P.Breakdown(new[] { st }, p => p.Religion), RelInfo);
            if (P.ClassesEnabled) SmallPie("Classes", P.Breakdown(new[] { st }, p => p.Type), ClassInfo);
            y += 2 * r + 8 * S;
            string major = P.Culture(P.MajorityCulture(st))?.Name ?? "-";
            string text = $"Mostly {major}" + (P.ReligionEnabled ? $", {P.Religion(P.MajorityReligion(st))?.Name}" : "");
            Gui.Label(Gui.Fit(text, w, 13), x, y, 13, Gui.TextDim);
            y += 20 * S;
        }
        // colonization of unowned land
        if (P.ColonizationEnabled && st.Owner == null && player != null)
        {
            if (sp.Colonizer != null)
            {
                Gui.Label($"Being settled by {sp.Colonizer.Name}", x, y, 14);
                y += 20 * S;
                Gui.Bar(new Rectangle(x, y, w, 10 * S), (float)(sp.ColonyProgress / 100), new Color(110, 190, 90, 255));
                y += 16 * S;
            }
            else
            {
                var reason = P.CannotColonize(player, st);
                string cost = P.ColonizeCost > 0 ? $" ({P.ColonizeCost:0} gold)" : "";
                if (Gui.Button(new Rectangle(x, y, w, 26 * S), "Send colonists" + cost, reason == null,
                        reason ?? "Colonists settle the land over the next years (they cost gold every month); natives may resist. Then it becomes your state.", false, 14))
                {
                    var err = P.Colonize(player, st);
                    if (err != null) Ui.SetStatus(err);
                }
                y += 32 * S;
            }
        }
        // buildings bought with gold (no production feature)
        if (player != null && st.Owner == player)
        {
            var gold = P.GoldBuildings().ToList();
            if (gold.Count > 0)
            {
                Gui.Section("Build", x, y, w);
                y += 26 * S;
                float bx = x;
                foreach (var b in gold)
                {
                    int queued = P.GoldProjects.Count(p => p.State == st && p.Building == b);
                    string label = $"{b.Name} ({P.GoldCost(b):0})" + (queued > 0 ? $" +{queued}" : "");
                    float bw = Math.Max(120 * S, Gui.Measure(label, 12).X + 16 * S);
                    if (bx + bw > x + w) { bx = x; y += 28 * S; }
                    var reason = P.CannotGoldBuild(player, b, st);
                    string tip = $"{b.Name}\n{b.Description}\nLevel {st.Buildings.GetValueOrDefault(b.Key)}/{b.MaxLevel}, {P.BuildMonths(b)} months to build, {P.GoldCost(b):0} gold" +
                                 (b.Definition.Get("modifier") is { } m && m.Children != null ? "\nPer level: " + string.Join(", ", m.All.Where(c => c.Value != null).Select(c => ModifierText(c.Key, c.AsDouble()))) : "") +
                                 (reason != null ? $"\n\n{reason}" : "");
                    if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), label, reason == null, tip, false, 12))
                    {
                        var err = P.GoldBuild(player, b, st);
                        if (err != null) Ui.SetStatus(err);
                    }
                    bx += bw + 6 * S;
                }
                y += 30 * S;
            }
        }
        return y;
    }

    // ------------------------------------------------------------------------------------- ledger
    private void DrawLedgerPanel()
    {
        var r = RightRect(2000);
        r.Width = Math.Max(r.Width, 620 * S);
        r.X = Raylib.GetScreenWidth() - r.Width - 8 * S;
        Gui.Panel(r, "Ledger");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var st = G.Statistics;
        var metrics = st.Available.ToList();
        if (metrics.Count == 0 || st.Dates.Count < 2) { Gui.Wrapped("Statistics are recorded every month. Come back after a few months.", x, y, w, 15, Gui.TextDim); return; }
        if (!metrics.Any(m => m.Key == _ledgerMetric)) _ledgerMetric = metrics[0].Key;
        float bx = x;
        foreach (var m in metrics)
        {
            float bw = Math.Max(80 * S, Gui.Measure(m.Name, 13).X + 16 * S);
            if (bx + bw > x + w) { bx = x; y += 30 * S; }
            if (Gui.Button(new Rectangle(bx, y, bw, 26 * S), m.Name, true, null, m.Key == _ledgerMetric, 13)) _ledgerMetric = m.Key;
            bx += bw + 4 * S;
        }
        y += 36 * S;
        // countries or the whole world
        if (Gui.Button(new Rectangle(x, y, 110 * S, 24 * S), "Countries", true, "The biggest countries and us", !_ledgerWorld, 13)) _ledgerWorld = false;
        if (Gui.Button(new Rectangle(x + 114 * S, y, 110 * S, 24 * S), "World", true, "The whole world (total, or average for levels like stability)", _ledgerWorld, 13)) _ledgerWorld = true;
        y += 32 * S;
        var player = G.World.Player;
        var series = new List<(string Name, Rgb Color, List<double> Values, bool Mine)>();
        if (_ledgerWorld)
        {
            bool sum = Burger.Core.Mechanics.StatisticsSystem.Summable.Contains(_ledgerMetric);
            var total = st.WorldSeries(_ledgerMetric);
            if (!sum)
            {
                // average of the countries that have a value
                var counts = new int[st.Dates.Count];
                if (st.Values.TryGetValue(_ledgerMetric, out var pc))
                    foreach (var l in pc.Values) for (int i = 0; i < l.Count && i < counts.Length; i++) if (!double.IsNaN(l[i])) counts[i]++;
                for (int i = 0; i < total.Count; i++) if (counts[i] > 0) total[i] /= counts[i];
            }
            series.Add((sum ? "World" : "World average", new Rgb(230, 210, 140), total, true));
        }
        else
        {
            // the 8 biggest countries now, and the player
            var tags = G.World.Countries.Where(c => c.Exists)
                .Select(c => (c, v: st.Series(_ledgerMetric, c.Tag).LastOrDefault(v => !double.IsNaN(v))))
                .OrderByDescending(t => t.v).Take(8).Select(t => t.c).ToList();
            if (player != null && player.Exists && !tags.Contains(player)) tags.Add(player);
            foreach (var c in tags) series.Add((c.Name, c.Color, st.Series(_ledgerMetric, c.Tag), c == player));
        }
        double max = series.SelectMany(s => s.Values).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max();
        if (max <= 0) max = 1;
        var chart = new Rectangle(x + 46 * S, y, w - 50 * S, Math.Min(330 * S, r.Y + r.Height - y - 190 * S));
        Raylib.DrawRectangleRec(chart, new Color(0, 0, 0, 70));
        for (int i = 0; i <= 4; i++)
        {
            float gy = chart.Y + chart.Height * (1 - i / 4f);
            Raylib.DrawLineEx(new Vector2(chart.X, gy), new Vector2(chart.X + chart.Width, gy), 1, new Color(255, 255, 255, 25));
            string lbl = LedgerFmt(max * i / 4);
            Gui.Label(lbl, chart.X - Gui.Measure(lbl, 11).X - 4 * S, gy - 7 * S, 11, Gui.TextDim);
        }
        int n = st.Dates.Count;
        Gui.Label(st.Dates[0].ToDisplay(), chart.X, chart.Y + chart.Height + 4 * S, 11, Gui.TextDim);
        string last = st.Dates[^1].ToDisplay();
        Gui.Label(last, chart.X + chart.Width - Gui.Measure(last, 11).X, chart.Y + chart.Height + 4 * S, 11, Gui.TextDim);
        // x follows the date, so thinned old samples keep their place in time
        int d0 = st.Dates[0].TotalDays, span = Math.Max(1, st.Dates[^1].TotalDays - d0);
        Vector2 Pt(int i, double v) => new(chart.X + chart.Width * (st.Dates[i].TotalDays - d0) / (float)span, chart.Y + chart.Height * (float)(1 - v / max));
        foreach (var (_, colr, values, mine) in series)
        {
            var col = C(colr);
            float thick = mine ? 3.2f * S : 1.8f * S;
            for (int i = 1; i < n && i < values.Count; i++)
                if (!double.IsNaN(values[i - 1]) && !double.IsNaN(values[i])) Raylib.DrawLineEx(Pt(i - 1, values[i - 1]), Pt(i, values[i]), thick, col);
        }
        // hover: values at that month
        if (Gui.Hover(chart))
        {
            int day = d0 + (int)Math.Round((Viewport.Mouse.X - chart.X) / chart.Width * span);
            int i = 0;
            for (int j = 1; j < n; j++) if (Math.Abs(st.Dates[j].TotalDays - day) < Math.Abs(st.Dates[i].TotalDays - day)) i = j;
            float hx = Pt(i, 0).X;
            Raylib.DrawLineEx(new Vector2(hx, chart.Y), new Vector2(hx, chart.Y + chart.Height), 1, new Color(255, 255, 255, 90));
            Gui.Tooltip(st.Dates[i].ToDisplay() + "\n" + string.Join("\n", series.Select(s => (s.Name, v: i < s.Values.Count ? s.Values[i] : double.NaN))
                .Where(t => !double.IsNaN(t.v)).OrderByDescending(t => t.v).Select(t => $"{t.Name}: {LedgerFmt(t.v)}")));
        }
        // legend
        y = chart.Y + chart.Height + 24 * S;
        float colW = w / 3;
        int k = 0;
        foreach (var (name, colr, values, mine) in series)
        {
            float lx = x + (k % 3) * colW, lyy = y + (k / 3) * 22 * S;
            Raylib.DrawRectangleRec(new Rectangle(lx, lyy + 5 * S, 14 * S, 6 * S), C(colr));
            double v = values.LastOrDefault(t => !double.IsNaN(t));
            Gui.Label(Gui.Fit($"{name} {LedgerFmt(v)}", colW - 22 * S, 13), lx + 20 * S, lyy, 13, mine ? Gui.Gold : Gui.Text);
            k++;
        }
    }

    private string LedgerFmt(double v) => _ledgerMetric is "population" or "manpower" or "war_dead" ? People(v) : v >= 1000 ? People(v) : v.ToString(v < 10 ? "0.#" : "0");
}
