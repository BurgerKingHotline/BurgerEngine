using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Politics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The politics menu: government and parliament, laws, cabinet and political movements (politics features).</summary>
public sealed partial class Hud
{
    public int PoliticsTab;
    private string? _cabinetPick;


    /// <summary>"Manpower +50%", "Political Power Gain +0.25" ... for modifier tooltips.</summary>
    public static string ModifierText(string key, double v)
    {
        bool points = key.EndsWith("_drift", StringComparison.OrdinalIgnoreCase) || key is "political_power_gain" or "stability_target" or "opinion" or "relations";
        string val = points ? (v >= 0 ? "+" : "") + v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            : (v >= 0 ? "+" : "") + (v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
        string name = key.EndsWith("_drift", StringComparison.OrdinalIgnoreCase)
            ? $"{Localisation.GetOrPretty(key[..^6])} popularity per month"
            : Localisation.GetOrPretty(key);
        return $"{name} {val}";
    }

    private static string ModifierLines(Dictionary<string, double> m)
        => m.Count == 0 ? "No effects" : string.Join("\n", m.Select(kv => ModifierText(kv.Key, kv.Value)));

    private void DrawPoliticsPanel()
    {
        var player = G.World.Player;
        var P = G.Politics;
        if (player == null || !P.Enabled) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 470 * S;
        Gui.Panel(r, "Politics");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var tabs = new List<(string Name, int Id)> { ("Government", 0) };
        if (P.LawsEnabled) tabs.Add(("Laws", 1));
        if (P.CabinetEnabled) tabs.Add(("Cabinet", 2));
        if (P.CivilWarsEnabled) tabs.Add(($"Movements{(P.MovementsOf(player).Any() ? $" ({P.MovementsOf(player).Count()})" : "")}", 3));
        int idx = Math.Max(0, tabs.FindIndex(t => t.Id == PoliticsTab));
        if (tabs.Count > 1)
        {
            Tabs(x, y, w, tabs.Select(t => t.Name).ToArray(), ref idx);
            y += 36 * S;
        }
        PoliticsTab = tabs[Math.Clamp(idx, 0, tabs.Count - 1)].Id;
        switch (PoliticsTab)
        {
            case 1: DrawLawsTab(player, r, x, y, w); break;
            case 2: DrawCabinetTab(player, r, x, y, w); break;
            case 3: DrawMovementsTab(player, r, x, y, w); break;
            default: DrawGovernmentTab(player, r, x, y, w); break;
        }
    }

    // ------------------------------------------------------------------------------------- government
    private void DrawGovernmentTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var p = P.Of(c);
        var ruling = P.Ideology(p.Ruling);
        // ruling party
        var sw = new Rectangle(x, y + 3 * S, 16 * S, 16 * S);
        Raylib.DrawRectangleRec(sw, C(ruling?.Color ?? Rgb.Gray));
        Gui.Label(Gui.Fit(P.PartyName(c, p.Ruling), w - 30 * S, 18, FontKind.Bold), x + 24 * S, y, 18, Gui.Gold, FontKind.Bold);
        y += 24 * S;
        string gov = $"{ruling?.Name} government" + (c.Government.Length > 0 ? $" - {Localisation.GetOrPretty(c.Government)}" : "");
        Gui.Label(Gui.Fit(gov, w, 14), x, y, 14, Gui.TextDim);
        y += 20 * S;
        if (p.Government.Count > 1)
        {
            Gui.Label(Gui.Fit("Coalition: " + string.Join(", ", p.Government.Select(g => P.PartyName(c, g))), w, 14), x, y, 14);
            y += 20 * S;
        }
        string elections = P.HoldsElections(c) && p.NextElection is { } ne ? $"Next election: {ne.ToDisplay()}" : "No elections";
        if (p.LastElection is { } le) elections += $"   (last: {le.ToDisplay()})";
        Gui.Label(Gui.Fit(elections, w, 14), x, y, 14);
        y += 26 * S;

        // popularity
        Gui.Section("Popularity", x, y, w);
        y += 26 * S;
        var bar = new Rectangle(x, y, w, 18 * S);
        float bx = bar.X;
        foreach (var i in P.Ideologies)
        {
            float bw = (float)(p.Popularity.GetValueOrDefault(i.Key) * bar.Width);
            if (bw <= 0) continue;
            Raylib.DrawRectangleRec(new Rectangle(bx, bar.Y, bw, bar.Height), C(i.Color));
            bx += bw;
        }
        Raylib.DrawRectangleLinesEx(bar, 1, new Color(0, 0, 0, 180));
        y += 26 * S;
        foreach (var i in P.Ideologies.OrderByDescending(i => p.Popularity.GetValueOrDefault(i.Key)))
        {
            double pop = p.Popularity.GetValueOrDefault(i.Key);
            Raylib.DrawRectangleRec(new Rectangle(x, y + 4 * S, 11 * S, 11 * S), C(i.Color));
            bool banned = P.IsBanned(c, i.Key);
            string label = $"{P.PartyName(c, i.Key)} ({i.Name})" + (banned ? " - banned" : "") + (P.InGovernment(c, i.Key) ? " - in government" : "");
            Gui.Label(Gui.Fit(label, w - 70 * S, 14), x + 18 * S, y, 14, banned ? Gui.TextDim : Gui.Text);
            string pct = $"{pop:P1}";
            Gui.Label(pct, x + w - Gui.Measure(pct, 14).X, y, 14, Gui.Gold);
            var row = new Rectangle(x, y, w, 20 * S);
            if (Gui.Hover(row))
                Gui.Tooltip($"{i.Name}: {pop:P1} support" + (i.Elections ? "\nHolds elections when in power" : "\nHolds no elections when in power") +
                            (i.Extremist ? "\nExtremist: gains support when stability is low or wars are lost" : "") +
                            (i.Modifier.Count > 0 ? "\nWhile ruling: " + string.Join(", ", i.Modifier.Select(kv => ModifierText(kv.Key, kv.Value))) : ""));
            y += 21 * S;
        }
        y += 8 * S;

        // parliament
        Gui.Section(P.HoldsElections(c) ? "Parliament" : "Parliament (no elections)", x, y, w);
        y += 28 * S;
        float ph = Math.Min(130 * S, w * 0.32f);
        DrawParliament(c, new Rectangle(x, y, w, ph));
        y += ph + 8 * S;
        int total = Math.Max(1, p.Seats.Values.Sum());
        int govSeats = p.Seats.Where(kv => p.Government.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).Sum(kv => kv.Value);
        string seatsLine = string.Join("   ", p.Seats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{P.Ideology(kv.Key)?.Name} {kv.Value}"));
        y += Gui.Wrapped(seatsLine, x, y, w, 13) + 2 * S;
        Gui.Label($"Government: {govSeats} of {total} seats" + (govSeats * 2 > total ? " (majority)" : " (minority)"), x, y, 13, govSeats * 2 > total ? Gui.Good : Gui.Bad);
        y += 26 * S;

        // where political modifiers come from
        var sources = P.ModifierSources(c);
        if (sources.Count > 0)
        {
            Gui.Section("Effects", x, y, w);
            y += 26 * S;
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, k, v) in sources) totals[k] = totals.GetValueOrDefault(k) + v;
            string text = string.Join(", ", totals.Where(kv => Math.Abs(kv.Value) > 1e-6).Select(kv => ModifierText(kv.Key, kv.Value)));
            float th = Gui.Wrapped(text, x, y, w, 13, Gui.TextDim);
            if (Gui.Hover(new Rectangle(x, y, w, th))) Gui.Tooltip(string.Join("\n", sources.Where(s => Math.Abs(s.Value) > 1e-6).Select(s => $"{s.Source}: {ModifierText(s.Key, s.Value)}")));
        }
    }

    /// <summary>A half-circle parliament: one dot per seat, parties in wedges (ideology order).</summary>
    private void DrawParliament(Country c, Rectangle area)
    {
        var P = G.Politics;
        var p = P.Of(c);
        int n = p.Seats.Values.Sum();
        if (n <= 0) { Gui.Label("No parliament", area.X, area.Y, 14, Gui.TextDim); return; }
        float outer = Math.Min(area.Width / 2, area.Height) - 4 * S;
        var center = new Vector2(area.X + area.Width / 2, area.Y + area.Height - 2 * S);
        int rows = Math.Clamp((int)Math.Ceiling(Math.Sqrt(n / 4.0)), 2, 12);
        float inner = outer * 0.42f;
        var radii = Enumerable.Range(0, rows).Select(i => inner + (outer - inner) * (rows == 1 ? 1 : i / (float)(rows - 1))).ToArray();
        float sumR = radii.Sum();
        var counts = radii.Select(rr => (int)Math.Floor(n * rr / sumR)).ToArray();
        counts[^1] += n - counts.Sum();
        var seats = new List<(float Angle, Vector2 Pos)>();
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < counts[i]; k++)
            {
                float a = counts[i] == 1 ? MathF.PI / 2 : MathF.PI * (1 - k / (float)(counts[i] - 1));
                seats.Add((a, center + new Vector2(MathF.Cos(a), -MathF.Sin(a)) * radii[i]));
            }
        seats.Sort((s1, s2) => s2.Angle.CompareTo(s1.Angle));
        float dot = Math.Max(1.5f, Math.Min((outer - inner) / rows * 0.42f, outer * MathF.PI / Math.Max(1, counts[^1]) * 0.42f));
        int si = 0;
        foreach (var i in P.Ideologies)
        {
            int cnt = p.Seats.GetValueOrDefault(i.Key);
            var col = C(i.Color);
            for (int k = 0; k < cnt && si < seats.Count; k++, si++) Raylib.DrawCircleV(seats[si].Pos, dot, col);
        }
        if (Gui.Hover(area))
            Gui.Tooltip(string.Join("\n", p.Seats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{P.PartyName(c, kv.Key)}: {kv.Value} seats")) +
                        $"\n\n{n} seats. The biggest party that can find a majority governs (alone or in a coalition).");
    }

    // ------------------------------------------------------------------------------------- laws
    private void DrawLawsTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        Gui.Label($"{P.PowerName}: {P.PowerOf(c):0}", x, y, 15, Gui.Gold);
        y += 26 * S;
        float rowsH = 0;
        foreach (var g in P.LawGroups) rowsH += 30 * S + LawButtonsHeight(c, g, w) + 10 * S;
        float top = Gui.BeginScroll("laws", new Rectangle(x, y, w, r.Y + r.Height - y - 12 * S), rowsH);
        float ly = top;
        foreach (var g in P.LawGroups)
        {
            var current = P.LawOf(c, g);
            Gui.Section(g.Name, x, ly, w - 10 * S);
            int cd = P.LawCooldownLeft(c, g);
            if (cd > 0) { string t = $"{cd} days"; Gui.Label(t, x + w - 14 * S - Gui.Measure(t, 12).X, ly + 4 * S, 12, Gui.TextDim); }
            ly += 30 * S;
            float bx = x, bh = 26 * S;
            foreach (var l in g.Laws.Where(l => l == current || P.IsLawVisible(c, l)))
            {
                float bw = Math.Max(110 * S, Gui.Measure(l.Name, 13).X + 18 * S);
                if (bx + bw > x + w - 10 * S) { bx = x; ly += bh + 4 * S; }
                bool isCurrent = l == current;
                var reason = isCurrent ? null : P.CannotEnact(c, l);
                string tip = $"{l.Name}\n{l.Desc}\n\n{ModifierLines(l.Modifier)}" +
                             (l.Bans.Count > 0 ? $"\nBans: {string.Join(", ", l.Bans.Select(b => P.Ideology(b)?.Name ?? b))}" : "") +
                             (l.SingleParty ? "\nOnly the ruling party is allowed, no elections" : l.SuspendsElections ? "\nNo elections" : "");
                if (!isCurrent)
                {
                    tip += $"\n\nCost: {l.Cost:0} {P.PowerName}";
                    if (l.Allow != null)
                        foreach (var (text, ok) in G.Script.ExplainTrigger(l.Allow, G.Context(c))) tip += $"\n{(ok ? "[x]" : "[ ]")} {text}";
                    if (reason != null) tip += $"\n\n{reason}";
                }
                if (Gui.Button(new Rectangle(bx, ly, bw, bh), l.Name, isCurrent || reason == null, tip, isCurrent, 13) && !isCurrent)
                {
                    var err = P.Enact(c, l);
                    if (err != null) Ui.SetStatus(err);
                }
                bx += bw + 6 * S;
            }
            ly += bh + 10 * S;
        }
        Gui.EndScroll();
    }

    private float LawButtonsHeight(Country c, LawGroup g, float w)
    {
        var P = G.Politics;
        float bx = 0, h = 26 * S, rowsH = 26 * S;
        foreach (var l in g.Laws.Where(l => l == P.LawOf(c, g) || P.IsLawVisible(c, l)))
        {
            float bw = Math.Max(110 * S, Gui.Measure(l.Name, 13).X + 18 * S);
            if (bx + bw > w - 10 * S) { bx = 0; rowsH += h + 4 * S; }
            bx += bw + 6 * S;
        }
        return rowsH;
    }

    // ------------------------------------------------------------------------------------- cabinet
    private void DrawCabinetTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        Gui.Label($"{P.PowerName}: {P.PowerOf(c):0}", x, y, 15, Gui.Gold);
        y += 26 * S;
        foreach (var pos in P.Positions)
        {
            var box = new Rectangle(x, y, w, 58 * S);
            Card(box, new Color(36, 38, 44, 255), _cabinetPick == pos.Key);
            Gui.Label(pos.Name, box.X + 8 * S, box.Y + 5 * S, 15, Gui.Gold, FontKind.Bold);
            var m = P.MinisterOf(c, pos);
            string who = m == null ? "Vacant" : $"{m.DisplayName} ({P.Ideology(P.MinisterParty(m))?.Name}), {Localisation.GetOrPretty(pos.Stat)} {m.GetStat(pos.Stat):0}";
            Gui.Label(Gui.Fit(who, box.Width - 120 * S, 13), box.X + 8 * S, box.Y + 28 * S, 13, m == null ? Gui.TextDim : Gui.Text);
            var effects = new Dictionary<string, double>(pos.Modifier);
            if (m != null) foreach (var kv in pos.PerPoint) effects[kv.Key] = effects.GetValueOrDefault(kv.Key) + kv.Value * m.GetStat(pos.Stat);
            if (Gui.Hover(box))
                Gui.Tooltip($"{pos.Name}\nGood ministers have high {Localisation.GetOrPretty(pos.Stat)}.\nPer point: {string.Join(", ", pos.PerPoint.Select(kv => ModifierText(kv.Key, kv.Value)))}" +
                            (m != null ? $"\n\nNow: {string.Join(", ", effects.Select(kv => ModifierText(kv.Key, kv.Value)))}" : ""));
            if (Gui.Button(new Rectangle(box.X + box.Width - 104 * S, box.Y + 16 * S, 96 * S, 26 * S), m == null ? "Appoint" : "Replace", true, $"Choose a minister ({pos.Cost:0} {P.PowerName})", _cabinetPick == pos.Key, 13))
                _cabinetPick = _cabinetPick == pos.Key ? null : pos.Key;
            y += 62 * S;
            if (_cabinetPick == pos.Key)
            {
                foreach (var ch in P.Candidates(c, pos).Take(5))
                {
                    var row = new Rectangle(x + 16 * S, y, w - 16 * S, 26 * S);
                    string text = $"{ch.DisplayName} - {P.Ideology(P.MinisterParty(ch))?.Name}, {Localisation.GetOrPretty(pos.Stat)} {ch.GetStat(pos.Stat):0}, age {ch.Age(G.World.Date)}";
                    var reason = P.CannotAppoint(c, pos, ch);
                    if (Gui.Button(row, Gui.Fit(text, row.Width - 12 * S, 13), reason == null, reason ?? $"Appoint for {pos.Cost:0} {P.PowerName}", false, 13))
                    {
                        var err = P.Appoint(c, pos, ch);
                        if (err != null) Ui.SetStatus(err); else _cabinetPick = null;
                    }
                    y += 29 * S;
                }
                y += 6 * S;
            }
            if (y > r.Y + r.Height - 40 * S) break;
        }
    }

    // ------------------------------------------------------------------------------------- movements
    private void DrawMovementsTab(Country c, Rectangle r, float x, float y, float w)
    {
        var P = G.Politics;
        var list = P.MovementsOf(c).ToList();
        y += Gui.Wrapped("Supporters of an ideology that is shut out of power (banned, or no elections to win) form a movement once it has enough support. " +
                         "Movements grow faster with low stability. At 100% they revolt: a civil war, or a coup in games without armies. " +
                         "A shut-out ideology with majority support may also start a civil war at any time.", x, y, w, 13, Gui.TextDim) + 10 * S;
        if (list.Count == 0) { Gui.Label("No political movements.", x, y, 15, Gui.Good); return; }
        foreach (var m in list)
        {
            var i = P.Ideology(m.Ideology);
            var box = new Rectangle(x, y, w, 98 * S);
            Card(box, new Color(44, 36, 36, 255));
            Raylib.DrawRectangleRec(new Rectangle(box.X + 8 * S, box.Y + 8 * S, 12 * S, 12 * S), C(i?.Color ?? Rgb.Gray));
            Gui.Label($"{i?.Adjective} movement", box.X + 26 * S, box.Y + 4 * S, 16, Gui.Gold, FontKind.Bold);
            Gui.Label(Gui.Fit(P.DemandText(m), box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 28 * S, 13);
            var bar = new Rectangle(box.X + 8 * S, box.Y + 50 * S, box.Width - 16 * S, 10 * S);
            Gui.Bar(bar, (float)(m.Progress / 100), new Color(220, 90, 60, 255));
            if (Gui.Hover(bar)) Gui.Tooltip($"{m.Progress:0}% - at 100% they revolt. Support: {P.PopularityOf(c, m.Ideology):P0}");
            float by = box.Y + 66 * S;
            if (Gui.Button(new Rectangle(box.X + 8 * S, by, 140 * S, 24 * S), "Give in", true, P.DemandText(m), false, 13)) P.GiveIn(m);
            var reason = P.CannotSuppress(m);
            if (Gui.Button(new Rectangle(box.X + 156 * S, by, 160 * S, 24 * S), $"Suppress ({P.SuppressCost:0})", reason == null,
                    reason ?? $"Set the movement back. Costs {P.SuppressCost:0} {P.PowerName} and some stability; martyrs make the ideology a little more popular.", false, 13))
            {
                var err = P.Suppress(m);
                if (err != null) Ui.SetStatus(err);
            }
            y += 104 * S;
            if (y > r.Y + r.Height - 100 * S) break;
        }
    }

    /// <summary>Government and popularity of another country (in the country view).</summary>
    private float DrawCountryPolitics(Country c, float x, float y, float w)
    {
        var P = G.Politics;
        if (!P.Enabled) return y;
        var p = P.Of(c);
        var ruling = P.Ideology(p.Ruling);
        Gui.Section("Politics", x, y, w);
        y += 26 * S;
        Raylib.DrawRectangleRec(new Rectangle(x, y + 3 * S, 12 * S, 12 * S), C(ruling?.Color ?? Rgb.Gray));
        Gui.Label(Gui.Fit($"{P.PartyName(c, p.Ruling)} ({ruling?.Name})" + (p.Government.Count > 1 ? " + coalition" : ""), w - 20 * S, 14), x + 18 * S, y, 14);
        y += 20 * S;
        var bar = new Rectangle(x, y, w, 12 * S);
        float bx = bar.X;
        foreach (var i in P.Ideologies)
        {
            float bw = (float)(p.Popularity.GetValueOrDefault(i.Key) * bar.Width);
            if (bw > 0) Raylib.DrawRectangleRec(new Rectangle(bx, bar.Y, bw, bar.Height), C(i.Color));
            bx += bw;
        }
        if (Gui.Hover(bar)) Gui.Tooltip(string.Join("\n", P.Ideologies.Select(i => $"{i.Name}: {p.Popularity.GetValueOrDefault(i.Key):P0}")) +
                                        (P.HoldsElections(c) && p.NextElection is { } ne ? $"\nNext election: {ne.ToDisplay()}" : "\nNo elections"));
        y += 18 * S;
        double op = P.IdeologyOpinion(G.World.Player ?? c, c);
        if (G.World.Player != null && G.World.Player != c && Math.Abs(op) > 0.01)
        {
            Gui.Label($"Ideology: {(op >= 0 ? "+" : "")}{op:0} opinion of them", x, y, 13, op >= 0 ? Gui.Good : Gui.Bad);
            y += 18 * S;
        }
        return y + 6 * S;
    }
}
