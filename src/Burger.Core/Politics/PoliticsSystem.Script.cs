using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Politics;

public sealed partial class PoliticsSystem
{
    /// <summary>Triggers, effects and values of the politics features (registered even when the features are off,
    /// so scripts that check them still validate; they are false / do nothing then).</summary>
    private void RegisterScript()
    {
        var s = _game.Script;
        static bool YesNo(Node n, bool v) => n.AsBool(true) ? v : !v;
        string Name(string? ideology) => Ideology(ideology)?.Name ?? ideology ?? "?";

        // ------------------------------------------------------------------ triggers
        TriggerFn ruling = (ctx, n) => ctx.This is Country c && Enabled && string.Equals(Of(c).Ruling, n.Value, StringComparison.OrdinalIgnoreCase);
        s.RegisterTrigger("ruling_ideology", ruling, (ctx, n) => $"Ruled by a {Name(n.Value)} government");
        s.RegisterTrigger("has_government", ruling, (ctx, n) => $"Ruled by a {Name(n.Value)} government");
        s.RegisterTrigger("in_government", (ctx, n) => ctx.This is Country c && Enabled && InGovernment(c, n.Value ?? ""),
            (ctx, n) => $"The {Name(n.Value)} party is in government");
        s.RegisterTrigger("same_ideology", (ctx, n) =>
        {
            if (ctx.This is not Country a || !Enabled) return false;
            s.TryResolveScope(n.Value ?? "", ctx, out var o);
            return o is Country b && string.Equals(Of(a).Ruling, Of(b).Ruling, StringComparison.OrdinalIgnoreCase);
        }, (ctx, n) => $"Same ideology as {(s.TryResolveScope(n.Value ?? "", ctx, out var o) ? o?.DisplayName : n.Value)}");
        s.RegisterTrigger("holds_elections", (ctx, n) => YesNo(n, ctx.This is Country c && HoldsElections(c)),
            (ctx, n) => n.AsBool(true) ? "Holds elections" : "Holds no elections");
        s.RegisterTrigger("party_banned", (ctx, n) => ctx.This is Country c && Enabled && IsBanned(c, n.Value ?? ""),
            (ctx, n) => $"The {Name(n.Value)} party is banned");
        s.RegisterTrigger("has_law", (ctx, n) => ctx.This is Country c && HasLaw(c, n.Value ?? ""),
            (ctx, n) => $"Has the law {(Laws.TryGetValue(n.Value ?? "", out var l) ? l.Name : n.Value)}");
        s.RegisterTrigger("has_movement", (ctx, n) =>
        {
            if (ctx.This is not Country c) return false;
            if (n.Value != null && IdeologyByKey.ContainsKey(n.Value)) return MovementsOf(c).Any(m => m.Ideology.Equals(n.Value, StringComparison.OrdinalIgnoreCase));
            return YesNo(n, MovementsOf(c).Any());
        }, (ctx, n) => IdeologyByKey.ContainsKey(n.Value ?? "") ? $"Has a {Name(n.Value)} movement" : n.AsBool(true) ? "Has a political movement" : "Has no political movement");
        s.RegisterTrigger("in_civil_war", (ctx, n) => YesNo(n, ctx.This is Country c && InCivilWar(c)),
            (ctx, n) => n.AsBool(true) ? "In a civil war" : "Not in a civil war");
        s.RegisterTrigger("has_minister", (ctx, n) => ctx.This is Country c && CabinetEnabled && Positions.FirstOrDefault(p => p.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase)) is { } pos && MinisterOf(c, pos) != null,
            (ctx, n) => $"Has a {Positions.FirstOrDefault(p => p.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase))?.Name ?? n.Value}");

        // ------------------------------------------------------------------ values (popularity_IDEOLOGY and seats_IDEOLOGY are added per ideology on load)
        s.RegisterValue("ruling_popularity", (ctx, x) => x is Country c && Enabled ? PopularityOf(c, Of(c).Ruling) : null);
        s.RegisterValue("months_to_election", (ctx, x) => x is Country c && Enabled && Of(c).NextElection is { } d ? Math.Max(0, (d.TotalDays - _game.World.Date.TotalDays) / 30.0) : null);
        s.RegisterValue("movement_progress", (ctx, x) => x is Country c ? MovementsOf(c).Select(m => m.Progress).DefaultIfEmpty(0).Max() : null);

        // ------------------------------------------------------------------ effects
        s.RegisterEffect("add_popularity", (ctx, n) =>
        {
            if (ctx.This is not Country c || !Enabled) return;
            string? ideology = n.GetString("ideology");
            if (string.Equals(ideology, "ruling", StringComparison.OrdinalIgnoreCase)) ideology = Of(c).Ruling;
            if (ideology == null || !IdeologyByKey.ContainsKey(ideology)) { Log.ErrorOnce("addpop:" + ideology, $"add_popularity: unknown ideology '{ideology}'"); return; }
            AddPopularity(c, ideology, s.ResolveNumber(n.GetString("value") ?? n.GetString("popularity"), ctx));
        }, (ctx, n) => $"{Name(n.GetString("ideology"))} popularity: {ScriptEngine.FormatSigned(100 * s.ResolveNumber(n.GetString("value") ?? n.GetString("popularity"), ctx))}%");
        s.RegisterEffect("set_ruling_party", (ctx, n) =>
        {
            if (ctx.This is Country c && Enabled && IdeologyByKey.ContainsKey(n.Value ?? "")) SetRulingParty(c, n.Value!, "");
        }, (ctx, n) => $"The {Name(n.Value)} party takes power");
        s.RegisterEffect("set_law", (ctx, n) =>
        {
            if (ctx.This is not Country c || !LawsEnabled) return;
            if (!Laws.TryGetValue(n.Value ?? "", out var law)) { Log.ErrorOnce("setlaw:" + n.Value, $"set_law: unknown law '{n.Value}'"); return; }
            Enact(c, law, free: true);
        }, (ctx, n) => $"Enact {(Laws.TryGetValue(n.Value ?? "", out var l) ? l.Name : n.Value)}");
        // give_in_to_movement = fascist / suppress_movement = fascist: answer a movement that demands power
        s.RegisterEffect("give_in_to_movement", (ctx, n) => { if (ctx.This is Country c && MovementsOf(c).FirstOrDefault(m => m.Ideology == n.Value) is { } m) GiveIn(m); }, (ctx, n) => $"Give in: the {Name(n.Value)} party takes power");
        s.RegisterEffect("suppress_movement", (ctx, n) => { if (ctx.This is Country c && MovementsOf(c).FirstOrDefault(m => m.Ideology == n.Value) is { } m) Suppress(m); }, (ctx, n) => $"Suppress the {Name(n.Value)} movement");
        s.RegisterEffect("hold_election", (ctx, n) => { if (ctx.This is Country c && n.AsBool(true)) HoldElection(c); }, (ctx, n) => "Hold an election");
        s.RegisterEffect("start_civil_war", (ctx, n) =>
        {
            if (ctx.This is not Country c || !Enabled) return;
            string ideology = n.GetString("ideology") ?? "";
            if (!IdeologyByKey.ContainsKey(ideology)) { Log.ErrorOnce("cw:" + ideology, $"start_civil_war: unknown ideology '{ideology}'"); return; }
            double size = n.Has("size") ? s.ResolveNumber(n.GetString("size"), ctx) : 0.4;
            if (_game.Features.Has(Modding.FeatureSet.Military) && _game.Features.Has(Modding.FeatureSet.Diplomacy) && StartCivilWar(c, ideology, size) != null) return;
            SetRulingParty(c, ideology, "coup");
        }, (ctx, n) => $"A {Name(n.GetString("ideology"))} civil war breaks out");
        s.RegisterEffect("set_party_name", (ctx, n) =>
        {
            if (ctx.This is not Country c || !Enabled) return;
            string? ideology = n.GetString("ideology"), name = n.GetString("name");
            if (ideology != null && name != null) Of(c).PartyNames[ideology] = name;
        }, (ctx, n) => $"The {Name(n.GetString("ideology"))} party is now called {n.GetString("name")}");
    }

    /// <summary>Console commands (called once the console exists).</summary>
    public void RegisterCommands(Burger.Core.Modding.ConsoleCommands cmd)
    {
        Country Player() => _game.World.Player ?? throw new InvalidOperationException("No player country (use 'tag TAG' first)");
        string Ide(string k) => IdeologyByKey.ContainsKey(k) ? k : throw new InvalidOperationException($"Unknown ideology '{k}' ({string.Join(", ", Ideologies.Select(i => i.Key))})");
        cmd.Register("politics", "politics [TAG] - ruling party, popularity, seats and laws", a =>
        {
            var c = a.Length > 0 ? _game.World.GetCountry(a[0]) ?? Player() : Player();
            var p = Of(c);
            return $"{c.Name}: ruling {p.Ruling} (government: {string.Join(", ", p.Government)}), elections: {(HoldsElections(c) ? p.NextElection?.ToScript() : "none")}\n" +
                   "popularity: " + string.Join(", ", p.Popularity.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:P0}")) + "\n" +
                   "seats: " + string.Join(", ", p.Seats.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}")) + "\n" +
                   "laws: " + string.Join(", ", p.Laws.Select(kv => $"{kv.Key}={kv.Value}"));
        });
        cmd.Register("popularity", "popularity <ideology> <0-1> - add popularity to an ideology in the player country", a =>
        {
            var c = Player();
            AddPopularity(c, Ide(a[0]), double.Parse(a[1], CultureInfo.InvariantCulture));
            return $"{a[0]}: {PopularityOf(c, a[0]):P0}";
        });
        cmd.Register("ideology", "ideology <ideology> - put that party in power in the player country", a => { SetRulingParty(Player(), Ide(a[0]), "coup"); return "Done"; });
        cmd.Register("election", "election [TAG] - hold an election now", a =>
        {
            var c = a.Length > 0 ? _game.World.GetCountry(a[0]) ?? Player() : Player();
            HoldElection(c);
            return string.Join(", ", Of(c).Seats.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} {kv.Value}")) + $" - governing: {string.Join(", ", Of(c).Government)}";
        });
        cmd.Register("law", "law <law> - enact a law in the player country for free", a =>
        {
            if (!Laws.TryGetValue(a[0], out var law)) return $"Unknown law '{a[0]}'";
            return Enact(Player(), law, free: true) ?? $"Enacted {law.Name}";
        });
        cmd.Register("civilwar", "civilwar <ideology> [share] [TAG] - start a civil war", a =>
        {
            var c = a.Length > 2 ? _game.World.GetCountry(a[2]) ?? Player() : Player();
            double share = a.Length > 1 ? double.Parse(a[1], CultureInfo.InvariantCulture) : 0.4;
            var r = StartCivilWar(c, Ide(a[0]), share);
            return r == null ? "No civil war could start (needs at least 2 connected states)" : $"{r.Name} ({r.Tag}) rose up";
        });
    }

    /// <summary>Checks the scripts of laws (called with the other script checks).</summary>
    public void Validate()
    {
        foreach (var l in Laws.Values)
        {
            ScriptValidator.Trigger(_game, l.Potential);
            ScriptValidator.Trigger(_game, l.Allow);
            ScriptValidator.Effect(_game, l.OnEnact);
            ScriptValidator.Weight(_game, l.AiWillDo);
            foreach (var b in l.Bans) if (!IdeologyByKey.ContainsKey(b)) Log.Error($"Law '{l.Key}' bans unknown ideology '{b}'", l.Definition.Location);
        }
        foreach (var p in Positions)
            if (p.Stat.Length > 0 && !_game.Data.Defines.CharacterStats.Contains(p.Stat, StringComparer.OrdinalIgnoreCase))
                Log.Warn($"Cabinet post '{p.Key}' uses unknown character stat '{p.Stat}'");
    }
}
