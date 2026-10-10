using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    /// <summary>Area of a panel on the left, next to the left menu bar.</summary>
    private Rectangle LeftRect(float height)
    {
        float x = (G.World.Player != null && !Ui.EditorActive ? LeftMenuW + 10 * S : 8 * S);
        return new(x, TopH + 8 * S, 390 * S, Math.Min(height, Raylib.GetScreenHeight() - TopH - 20 * S));
    }

    private string CountryLink(Country? c) => c?.Name ?? "-";

    private static string Abbrev(string stat)
    {
        var p = Localisation.GetOrPretty(stat);
        return p.Length <= 3 ? p : p[..3];
    }

    private void DrawLocationPanel()
    {
        var l = Ui.SelectedLocation;
        if (l == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(800 * S);
        Gui.Panel(r, l.DisplayName);
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + 36 * S, lh = 22 * S;
        void Row(string k, string v, Color? col = null)
        {
            Gui.Label(k, x, y, 16, Gui.TextDim);
            Gui.Label(v, x + 120 * S, y, 16, col ?? Gui.Text);
            y += lh;
        }
        Row("Terrain", l.Terrain.Name + (l.IsCoastal ? " (coastal)" : ""));
        if (l.IsWater) { Row("Type", l.IsLake ? "Lake" : "Sea"); return; }
        var st = l.State;
        Row("State", st?.DisplayName ?? "(none)");
        Row("Owner", CountryLink(l.Owner));
        Row("Controller", CountryLink(l.Controller), l.IsOccupied ? Gui.Bad : null);
        if (st != null)
        {
            Row("Cores", st.Cores.Count == 0 ? "-" : string.Join(", ", st.Cores.Select(c => c.Tag)));
            Row("Claims", st.Claims.Count == 0 ? "-" : string.Join(", ", st.Claims.Select(c => c.Tag)));
            Row("Tax / month", $"{st.Tax:0.##}");
            Row("Manpower / month", $"{st.Manpower:0}");
            Row("Locations", st.Locations.Count.ToString());
            if (st.Buildings.Count > 0)
            {
                // buildings in two columns so long lists stay inside the panel
                y += 4 * S;
                Gui.Section($"Buildings in {st.DisplayName}", x, y, r.Width - 24 * S);
                y += 26 * S;
                float colW = (r.Width - 24 * S) / 2;
                int i = 0;
                foreach (var b in st.Buildings.OrderBy(b => b.Key))
                {
                    string name = G.Data.BuildingTypes.TryGetValue(b.Key, out var bt) ? bt.Name : Localisation.GetOrPretty(b.Key);
                    double dmg = st.Damaged.GetValueOrDefault(b.Key);
                    string text = Gui.Fit($"{name}: {b.Value}" + (dmg >= 0.05 ? $" ({dmg:0.#} bombed)" : ""), colW - 8 * S, 15);
                    Gui.Label(text, x + (i % 2) * colW, y + (i / 2) * 20 * S, 15, dmg >= 0.05 ? Gui.Bad : Gui.Text);
                    i++;
                }
                y += (i + 1) / 2 * 20 * S + 6 * S;
            }
        }
        if (l.Fort > 0) Row("Fort", l.Fort.ToString());
        if (l.SiegeProgress > 0) Row("Siege", $"{l.SiegeProgress:0} h by {l.Sieger?.Tag}");
        var rivers = l.Neighbors.Count(a => a.River);
        if (rivers > 0) Row("Rivers", $"{rivers} border(s) with a river");
        y += DrawModifierList(l, x, y, r.Width - 24 * S);
        if (st != null) y += DrawModifierList(st, x, y, r.Width - 24 * S);
        y += 4 * S;
        if (l.Owner != null && Gui.Button(new Rectangle(x, y, 170 * S, 28 * S), "View " + (l.Owner.Tag), true, "Open the owner's country panel"))
        {
            Ui.SelectedCountry = l.Owner;
            Ui.Left = LeftPanel.Country;
            return;
        }
        y += 36 * S;

        var divs = G.Warfare.DivisionsAt(l);
        if (divs.Count > 0)
        {
            Gui.Section("Units here", x, y, r.Width - 24 * S);
            y += 6 * S;
            y += lh;
            foreach (var g in divs.GroupBy(d => d.Owner))
            {
                Gui.Label($"{g.Key.Name}: {g.Count()} division(s)", x, y, 15);
                y += lh;
            }
        }

        var player = G.World.Player;
        if (player != null && Has(FeatureSet.Military) && l.Owner == player && l.Controller == player && Gui.Button(new Rectangle(x, y + 6 * S, r.Width - 24 * S, 28 * S), "Recruit divisions...", true, "Open the recruit menu (R) - pick a template, then click your locations", false, 15))
            Ui.Left = LeftPanel.Recruit;
    }

    // ===============================================================================================
    // Country / diplomacy panel
    // ===============================================================================================
    private void DrawCountryPanel()
    {
        var c = Ui.SelectedCountry;
        if (c == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(Raylib.GetScreenHeight() - TopH - 20 * S);
        Gui.Panel(r, c.Name);
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + 36 * S, lh = 21 * S;
        Gui.Flag(c, new Rectangle(x, y, 84 * S, 56 * S));
        float tx = x + 94 * S;
        Gui.Label($"Tag: {c.Tag}", tx, y, 15, Gui.TextDim);
        if (c.Government.Length > 0) Gui.Label(Localisation.GetOrPretty(c.Government), tx, y + 18 * S, 15);
        Gui.Label($"{c.OwnedStates.Count} states, {G.World.Divisions.Count(d => d.Owner == c)} divisions", tx, y + 36 * S, 15, Gui.TextDim);
        y += 64 * S;

        if (c.Ruler != null)
        {
            var ruler = c.Ruler;
            Gui.Label($"Ruler: {ruler.DisplayName} ({ruler.Age(G.World.Date)})", x, y, 16);
            y += lh;
            Gui.Label(string.Join("  ", G.Data.Defines.CharacterStats.Select(s => $"{Abbrev(s)} {ruler.GetStat(s):0}")), x, y, 14, Gui.TextDim);
            y += lh;
            if (ruler.Traits.Count > 0)
            {
                Gui.Label(string.Join(", ", ruler.Traits.Select(t => Localisation.Get(t.Name))), x, y, 14, Gui.TextDim);
                y += lh;
            }
            if (c.Heir != null) { Gui.Label($"Heir: {c.Heir.DisplayName} ({c.Heir.Age(G.World.Date)})", x, y, 15, Gui.TextDim); y += lh; }
        }

        foreach (var res in G.Resources.Defs.Where(d => d.Scope == Burger.Core.ScopeType.Country))
        {
            Gui.Label($"{Localisation.Get(res.Name)}: {G.Resources.Get(c, res):0.#}", x, y, 15, Gui.TextDim);
            y += lh;
        }
        y += DrawModifierList(c, x, y, r.Width - 24 * S);
        var player = G.World.Player;
        var R = G.World.Relations;
        y += 4 * S;
        if (player != null && player != c && Has(FeatureSet.Diplomacy))
        {
            double theirs = G.Diplomacy.Opinion(c, player), ours = G.Diplomacy.Opinion(player, c);
            var opR = new Rectangle(x, y, r.Width - 24 * S, lh);
            Gui.Label($"Opinion of us: {theirs:+0;-0;0}   Our opinion: {ours:+0;-0;0}", x, y, 15, theirs >= 0 ? Gui.Good : Gui.Bad);
            if (Gui.Hover(opR))
            {
                var mods = R.Opinions.TryGetValue((c, player), out var list) ? list : new List<OpinionModifier>();
                Gui.Tooltip($"Base: {G.Rules.Diplomacy.BaseOpinion(G, c, player):+0;-0;0}\n" + string.Join("\n", mods.Select(m => $"{m.Name}: {m.Value:+0;-0;0}")));
            }
            y += lh;
            var rel = new List<string>();
            if (G.World.AtWar(player, c)) rel.Add("AT WAR");
            if (R.AreAllied(player, c)) rel.Add("Allied");
            if (R.OverlordOf(c) == player) rel.Add("Our subject");
            if (R.OverlordOf(player) == c) rel.Add("Our overlord");
            if (R.HasAccess(player, c)) rel.Add("We have access");
            if (R.HasAccess(c, player)) rel.Add("They have access");
            if (R.IsGuaranteeing(player, c)) rel.Add("We guarantee them");
            if (R.IsGuaranteeing(c, player)) rel.Add("They guarantee us");
            if (R.HasTruce(player, c, G.World.Date)) rel.Add("Truce");
            Gui.Label(rel.Count == 0 ? "No relations" : string.Join(", ", rel), x, y, 15, rel.Contains("AT WAR") ? Gui.Bad : Gui.Text);
            y += lh;
        }
        else if (player == c && Has(FeatureSet.Diplomacy))
        {
            if (Has(FeatureSet.Economy)) { Gui.Label($"Gold {c.Gold:0}   Manpower {c.Manpower:0}", x, y, 15); y += lh; }
            Gui.Label($"Diplomatic relations: {G.Diplomacy.RelationsUsed(c)}/{G.Diplomacy.MaxRelations(c)}", x, y, 15, Gui.TextDim);
            y += lh;
            var allies = R.AlliesOf(c).ToList();
            if (allies.Count > 0) { Gui.Label("Allies: " + string.Join(", ", allies.Select(a => a.Tag)), x, y, 15); y += lh; }
            var subjects = R.SubjectsOf(c).ToList();
            if (subjects.Count > 0) { Gui.Label("Subjects: " + string.Join(", ", subjects.Select(a => a.Tag)), x, y, 15); y += lh; }
        }

        if (Has(FeatureSet.Diplomacy)) y = DrawCountryRelations(c, x, y + 6 * S, r.Width - 24 * S);
        if (player != null && player != c && Has(FeatureSet.LendLease) && Has(FeatureSet.Military)) y = DrawSupportSummary(player, c, x, y, r.Width - 24 * S);
        if (player == null || player == c || !Has(FeatureSet.Diplomacy)) return;
        y += 8 * S;
        Gui.Section("Diplomatic actions", x, y, r.Width - 24 * S);
        y += 26 * S;
        if (G.World.AtWar(player, c))
        {
            var war = G.World.WarBetween(player, c)!;
            if (Gui.Button(new Rectangle(x, y, r.Width - 24 * S, 28 * S), "Negotiate peace", true, $"War score: {war.ScoreFor(player):0}"))
                OpenPeace(war, war.EnemyLeaderOf(player));
            y += 32 * S;
        }
        var actions = G.Diplomacy.ActionsFor(ActionTarget.Country).Where(a => G.Diplomacy.IsVisible(a, player, c)).ToList();
        float listH = r.Y + r.Height - y - 8 * S;
        float rowH = 30 * S;
        float top = Gui.BeginScroll("diplo", new Rectangle(x, y, r.Width - 20 * S, listH), actions.Count * rowH);
        float ry = top;
        foreach (var a in actions)
        {
            var reqs = G.Diplomacy.Requirements(a, player, c);
            bool can = reqs.All(q => q.Ok);
            string tip = Localisation.Get(a.Desc);
            if (reqs.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text));
            if (a.RequiresAcceptance)
            {
                double acc = G.Diplomacy.Acceptance(a, player, c);
                tip += $"\n\nThey would {(acc > 0 ? "ACCEPT" : "DECLINE")} (score {acc:0})";
            }
            var effects = G.Script.Describe(a.OnAccept, G.Diplomacy.ActionContext(a, player, c));
            if (effects.Count > 0) tip += "\n\nEffects:\n" + string.Join("\n", effects);
            if (a.Dialog != null) tip += "\n\nOpens a window to choose the details.";
            if (Gui.Button(new Rectangle(x, ry, r.Width - 30 * S, rowH - 4 * S), Localisation.Get(a.Name) + (a.Dialog != null ? "..." : ""), can, tip, false, 16))
            {
                if (a.Dialog != null) OpenSupportDialog(a.Dialog, c);
                else
                {
                    string result = G.Diplomacy.Use(a, player, c);
                    Ui.SetStatus($"{Localisation.Get(a.Name)}: {result}");
                }
            }
            ry += rowH;
        }
        Gui.EndScroll();
    }

    /// <summary>The country's own relations (also shown on the map: allies blue, enemies red). Returns the new y.</summary>
    private float DrawCountryRelations(Country c, float x, float y, float w)
    {
        var R = G.World.Relations;
        var lines = new List<(string Label, List<Country> List, Color Col)>
        {
            ("At war with", G.World.WarsOf(c).SelectMany(war => war.EnemiesOf(c)).Distinct().Where(e => e.Exists).ToList(), Gui.Bad),
            ("Allies", R.AlliesOf(c).ToList(), new Color(110, 160, 245, 255)),
            ("Subjects", R.SubjectsOf(c).ToList(), new Color(140, 200, 235, 255)),
            ("Overlord", R.OverlordOf(c) is { } ov ? new List<Country> { ov } : new List<Country>(), new Color(140, 200, 235, 255)),
            ("Guarantees", R.Guarantees.Where(g => g.Guarantor == c).Select(g => g.Guaranteed).ToList(), Gui.Text),
            ("Guaranteed by", R.GuarantorsOf(c).ToList(), Gui.Text),
            ("Access through", R.MilitaryAccess.Where(m => m.Receiver == c).Select(m => m.Granter).ToList(), Gui.TextDim),
        };
        if (lines.All(l => l.List.Count == 0))
        {
            Gui.Label($"{c.Name} has no allies, subjects or wars.", x, y, 14, Gui.TextDim);
            return y + 22 * S;
        }
        Gui.Section("Relations", x, y, w);
        y += 26 * S;
        foreach (var (label, list, col) in lines)
        {
            if (list.Count == 0) continue;
            Gui.Label(label + ":", x, y, 14, Gui.TextDim);
            string names = string.Join(", ", list.Select(o => o.Name));
            y += Gui.Wrapped(names, x + 110 * S, y, w - 110 * S, 14, col) + 3 * S;
        }
        if (Map.Mode == MapMode.Diplomacy && Map.DiplomacyFocus == c)
        {
            Gui.Label("Map: green = " + c.Name + ", blue = allies, red = enemies, light blue = subjects", x, y, 12, Gui.TextDim);
            y += 18 * S;
        }
        return y + 4 * S;
    }

    /// <summary>A short line about our volunteers and lend-lease with this country (the actions open the windows).</summary>
    private float DrawSupportSummary(Country player, Country c, float x, float y, float w)
    {
        var sup = G.Support;
        int abroad = sup.VolunteersOf(player).Count(d => d.Owner == c);
        var deals = sup.Deals.Where(d => d.From == player && d.To == c).ToList();
        if (abroad == 0 && deals.Count == 0) return y;
        if (abroad > 0)
        {
            Gui.Label($"Our volunteers here: {abroad} division(s)", x, y + 3 * S, 14);
            if (Gui.Button(new Rectangle(x + w - 90 * S, y, 90 * S, 22 * S), "Recall", true, "Bring them home", false, 13))
                foreach (var d in sup.VolunteersOf(player).Where(d => d.Owner == c).ToList()) sup.Recall(d);
            y += 26 * S;
        }
        if (deals.Count > 0)
        {
            Gui.Label(Gui.Fit("Lend-lease: " + string.Join(", ", deals.Select(d => $"{d.Equipment.Name} {d.Share:P0}/month")), w, 14), x, y, 14, Gui.TextDim);
            y += 22 * S;
        }
        return y + 4 * S;
    }
}
