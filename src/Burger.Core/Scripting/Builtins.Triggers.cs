using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Scripting;

public static partial class Builtins
{
    /// <summary>yes/no comparison helper: <c>is_ai = yes</c> → value, <c>is_ai = no</c> → !value.</summary>
    private static bool YesNo(Node n, bool value) => n.AsBool(true) ? value : !value;

    private static State? StateArg(Game game, ScriptContext ctx, string? expr)
    {
        if (expr == null) return null;
        if (game.Script.TryResolveScope(expr, ctx, out var s)) return s as State ?? (s as Location)?.State;
        return game.World.GetState(expr);
    }

    private static Location? LocationArg(Game game, ScriptContext ctx, string? expr)
    {
        if (expr == null) return null;
        if (game.Script.TryResolveScope(expr, ctx, out var s)) return s as Location ?? (s as Country)?.Capital;
        return game.World.GetLocation(expr);
    }

    private static void RegisterTriggers(Game game)
    {
        var s = game.Script;
        var w = game.World;
        var R = w.Relations;

        // ------------------------------------------------------------------ generic
        s.RegisterTrigger("has_flag", (ctx, n) => ctx.This?.Vars.HasFlag(n.Value ?? "") == true,
            (ctx, n) => $"Has flag '{n.Value}'");
        s.RegisterTrigger("has_country_flag", (ctx, n) => ctx.This?.Vars.HasFlag(n.Value ?? "") == true);
        s.RegisterTrigger("has_character_flag", (ctx, n) => ctx.This?.Vars.HasFlag(n.Value ?? "") == true);
        s.RegisterTrigger("has_global_flag", (ctx, n) => game.World.Global.HasFlag(n.Value ?? ""));
        s.RegisterTrigger("has_variable", (ctx, n) => ctx.This?.Vars.Has(n.Value ?? "") == true);
        s.RegisterTrigger("check_variable", (ctx, n) =>
        {
            // check_variable = { name = x value >= 5 }   or   check_variable = { x >= 5 }
            string? name = n.GetString("name") ?? n.GetString("which");
            Node? cmp = n.Get("value");
            if (name == null)
            {
                cmp = n.All.FirstOrDefault(c => c.Value != null);
                name = cmp?.Key;
            }
            if (name == null || cmp == null) { Log.ErrorOnce("cv:" + n.Location, "check_variable needs  name = x value >= n", n.Location); return false; }
            double v = ctx.This?.Vars.Get(name) ?? 0;
            return Node.Compare(v, cmp.Op, s.ResolveNumber(cmp.Value, ctx));
        }, (ctx, n) => $"Variable {n.GetString("name")} {Node.OpToString(n.Get("value")?.Op ?? Op.Eq)} {n.GetString("value")}");
        s.RegisterTrigger("is_same_as", (ctx, n) => ReferenceEquals(ctx.This, s.ArgScope(n, ctx)));
        s.RegisterTrigger("exists", (ctx, n) =>
        {
            if (n.Value != null && !Node.ParseBool(n.Value, false) && !n.Value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                // exists = GER
                s.TryResolveScope(n.Value, ctx, out var target);
                return C(target)?.Exists == true;
            }
            bool ex = ctx.This switch { Country c => c.Exists, Character ch => ch.Alive, null => false, _ => true };
            return YesNo(n, ex);
        });
        s.RegisterTrigger("random_chance", (ctx, n) => game.Rng.NextDouble() * 100 < s.ResolveNumber(n.Value, ctx));
        s.RegisterTrigger("date", (ctx, n) =>
        {
            if (!GameDate.TryParse(n.Value, out var d)) { Log.ErrorOnce("date:" + n.Location, $"Invalid date '{n.Value}' (use 1936.1.1)", n.Location); return false; }
            return Node.Compare(game.World.Date.TotalDays, n.Op, d.TotalDays);
        }, (ctx, n) => $"Date {Node.OpToString(n.Op)} {n.Value}");
        s.RegisterTrigger("has_combat_model", (ctx, n) => string.Equals(game.Data.Defines.CombatModel, n.Value, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ country
        s.RegisterTrigger("tag", (ctx, n) => string.Equals(C(ctx.This)?.Tag, n.Value, StringComparison.OrdinalIgnoreCase) == (n.Op != Op.Ne),
            (ctx, n) => $"Is {game.World.GetCountry(n.Value)?.Name ?? n.Value}");
        s.RegisterTrigger("is_ai", (ctx, n) => YesNo(n, C(ctx.This)?.IsAI == true));
        s.RegisterTrigger("is_player", (ctx, n) => YesNo(n, C(ctx.This)?.IsPlayer == true));
        s.RegisterTrigger("has_feature", (ctx, n) => game.Features.Has(n.Value ?? ""),
            (ctx, n) => $"The game uses the feature '{n.Value}'");
        s.RegisterTrigger("government", (ctx, n) => string.Equals(C(ctx.This)?.Government, n.Value, StringComparison.OrdinalIgnoreCase));
        s.RegisterTrigger("is_at_war", (ctx, n) => YesNo(n, C(ctx.This) is { } c && game.World.WarsOf(c).Any()),
            (ctx, n) => n.AsBool(true) ? "Is at war" : "Is at peace");
        s.RegisterTrigger("is_at_war_with", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && game.World.AtWar(a, b),
            (ctx, n) => $"At war with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_allied_with", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.AreAllied(a, b),
            (ctx, n) => $"Allied with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("has_alliance_with", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.AreAllied(a, b));
        s.RegisterTrigger("is_subject", (ctx, n) => YesNo(n, C(ctx.This) is { } c && R.OverlordOf(c) != null));
        s.RegisterTrigger("is_subject_of", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.OverlordOf(a) == b,
            (ctx, n) => $"Is a subject of {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_overlord_of", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.OverlordOf(b) == a);
        s.RegisterTrigger("has_truce_with", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.HasTruce(a, b, game.World.Date),
            (ctx, n) => $"Has a truce with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("has_military_access", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.HasAccess(a, b),
            (ctx, n) => $"Has military access in {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_guaranteeing", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && R.IsGuaranteeing(a, b));
        s.RegisterTrigger("borders", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && NeighborCountries(game, a).Contains(b),
            (ctx, n) => $"Borders {s.ArgScope(n, ctx)?.DisplayName}");
        // enclave_held_by = scope:recipient: they hold a state that lies inside our land, with nobody else around it
        s.RegisterTrigger("enclave_held_by", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && game.Diplomacy.EnclaveHolders(a).Contains(b),
            (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName} holds an enclave in our land");
        // stronger_than = { target = scope:recipient ratio = 1.5 }: our military strength is at least ratio x theirs
        s.RegisterTrigger("stronger_than", (ctx, n) =>
        {
            if (C(ctx.This) is not { } a) return false;
            var tn = n.Children != null ? n.Get("target") : n;
            Country? b = tn != null && s.TryResolveScope(tn.Value ?? "", ctx, out var t) ? t as Country : null;
            if (b == null) return false;
            double ratio = n.Children != null ? n.GetDouble("ratio", 1) : 1;
            return game.Diplomacy.Strength(a) >= ratio * Math.Max(1, game.Diplomacy.Strength(b));
        }, (ctx, n) => $"Much stronger than {(n.Children != null ? n.GetString("target") : n.Value)}");
        s.RegisterTrigger("is_neighbor_of", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b && NeighborCountries(game, a).Contains(b));
        s.RegisterTrigger("can_declare_war_on", (ctx, n) => C(ctx.This) is { } a && s.ArgCountry(n, ctx) is { } b
            && game.Rules.Diplomacy.CannotDeclareWarReason(game, a, b, game.Diplomacy.BestCasusBelli(a, b)) == null && game.Diplomacy.BestCasusBelli(a, b) != null,
            (ctx, n) => $"Can declare war on {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("opinion", (ctx, n) =>
        {
            // opinion = { target = X value >= 50 }   (THIS's opinion of X)
            var from = C(ctx.This);
            var to = s.ArgCountry(n, ctx);
            var cmp = n.Get("value");
            if (from == null || to == null || cmp == null) return false;
            return Node.Compare(game.Diplomacy.Opinion(from, to), cmp.Op, s.ResolveNumber(cmp.Value, ctx));
        }, (ctx, n) => $"Opinion of {s.ArgScope(n, ctx)?.DisplayName} {Node.OpToString(n.Get("value")?.Op ?? Op.Eq)} {n.GetString("value")}");
        s.RegisterTrigger("owns_state", (ctx, n) => StateArg(game, ctx, n.Value) is { } st && st.Owner == C(ctx.This),
            (ctx, n) => $"Owns {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterTrigger("owns_location", (ctx, n) => LocationArg(game, ctx, n.Value) is { } l && l.Owner == C(ctx.This),
            (ctx, n) => $"Owns {LocationArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterTrigger("controls_location", (ctx, n) => LocationArg(game, ctx, n.Value) is { } l && l.Controller == C(ctx.This),
            (ctx, n) => $"Controls {LocationArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterTrigger("controls_state", (ctx, n) => StateArg(game, ctx, n.Value) is { } st && C(ctx.This) is { } c && st.Locations.All(l => l.Controller == c));
        s.RegisterTrigger("has_core_on", (ctx, n) => StateArg(game, ctx, n.Value) is { } st && C(ctx.This) is { } c && st.Cores.Contains(c),
            (ctx, n) => $"Has a core on {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterTrigger("has_claim_on", (ctx, n) =>
        {
            if (C(ctx.This) is not { } c) return false;
            if (s.TryResolveScope(n.Value ?? "", ctx, out var target) && target is Country other)
                return other.OwnedStates.Any(st => st.Claims.Contains(c)); // has_claim_on = FRA: any claim on FRA's land
            return StateArg(game, ctx, n.Value) is { } st && st.Claims.Contains(c);
        }, (ctx, n) => $"Has a claim on {(s.TryResolveScope(n.Value ?? "", ctx, out var t) ? t?.DisplayName : StateArg(game, ctx, n.Value)?.DisplayName) ?? n.Value}");

        // ------------------------------------------------------------------ state / location
        s.RegisterTrigger("is_owned_by", (ctx, n) => s.ArgCountry(n, ctx) is { } c && (ctx.This switch { State st => st.Owner, Location l => l.Owner, _ => null }) == c,
            (ctx, n) => $"Owned by {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_controlled_by", (ctx, n) => s.ArgCountry(n, ctx) is { } c && ctx.This switch
        {
            Location l => l.Controller == c,
            State st => st.Locations.All(l => l.Controller == c),
            _ => false,
        }, (ctx, n) => $"Controlled by {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_core_of", (ctx, n) => s.ArgCountry(n, ctx) is { } c && (ctx.This as State ?? (ctx.This as Location)?.State)?.Cores.Contains(c) == true,
            (ctx, n) => $"Is a core of {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterTrigger("is_claimed_by", (ctx, n) => s.ArgCountry(n, ctx) is { } c && (ctx.This as State ?? (ctx.This as Location)?.State)?.Claims.Contains(c) == true);
        s.RegisterTrigger("is_capital", (ctx, n) => YesNo(n, ctx.This is Location l && l.Owner?.Capital == l || ctx.This is State st && st.Owner?.Capital?.State == st));
        s.RegisterTrigger("is_occupied", (ctx, n) => YesNo(n, ctx.This is Location l && l.IsOccupied || ctx.This is State st && st.Locations.Any(x => x.IsOccupied)));
        s.RegisterTrigger("terrain", (ctx, n) => string.Equals((ctx.This as Location)?.Terrain.Key, n.Value, StringComparison.OrdinalIgnoreCase),
            (ctx, n) => $"Terrain is {Localisation.GetOrPretty(n.Value ?? "")}");
        s.RegisterTrigger("is_coastal", (ctx, n) => YesNo(n, ctx.This is Location l && l.IsCoastal || ctx.This is State st && st.Locations.Any(x => x.IsCoastal)));
        s.RegisterTrigger("is_water", (ctx, n) => YesNo(n, ctx.This is Location l && l.IsWater));
        s.RegisterTrigger("is_sea", (ctx, n) => YesNo(n, ctx.This is Location l && l.IsWater && !l.IsLake));
        s.RegisterTrigger("has_building", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            if (st == null) return false;
            if (n.Value != null) return st.Buildings.GetValueOrDefault(n.Value) > 0;
            string type = n.GetString("type", "")!;
            var lv = n.Get("level");
            int have = st.Buildings.GetValueOrDefault(type);
            return lv == null ? have > 0 : Node.Compare(have, lv.Op, s.ResolveNumber(lv.Value, ctx));
        });
        s.RegisterTrigger("category", (ctx, n) => string.Equals((ctx.This as State ?? (ctx.This as Location)?.State)?.Category, n.Value, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ character
        s.RegisterTrigger("has_trait", (ctx, n) => (ctx.This as Character)?.HasTrait(n.Value ?? "") == true,
            (ctx, n) => $"Has trait {(game.Data.Traits.TryGetValue(n.Value ?? "", out var t) ? Localisation.Get(t.Name) : n.Value)}");
        s.RegisterTrigger("is_ruler", (ctx, n) => YesNo(n, (ctx.This as Character)?.IsRuler == true), (ctx, n) => n.AsBool(true) ? "Is the ruler" : "Is not the ruler");
        s.RegisterTrigger("is_heir", (ctx, n) => YesNo(n, (ctx.This as Character)?.IsHeir == true));
        s.RegisterTrigger("is_general", (ctx, n) => YesNo(n, ctx.This is Character ch && (ch.IsGeneral || game.World.Armies.Any(a => a.General == ch))));
        s.RegisterTrigger("is_commanding", (ctx, n) => YesNo(n, ctx.This is Character ch && game.World.Armies.Any(a => a.General == ch)));
        s.RegisterTrigger("is_alive", (ctx, n) => YesNo(n, (ctx.This as Character)?.Alive == true));
        s.RegisterTrigger("is_female", (ctx, n) => YesNo(n, (ctx.This as Character)?.Female == true));
        s.RegisterTrigger("is_male", (ctx, n) => YesNo(n, ctx.This is Character { Female: false }));
        s.RegisterTrigger("is_married", (ctx, n) => YesNo(n, (ctx.This as Character)?.Spouse is { Alive: true }));
        s.RegisterTrigger("is_adult", (ctx, n) => YesNo(n, ctx.This is Character ch && ch.Age(game.World.Date) >= game.Data.Defines.AdultAge));
        s.RegisterTrigger("is_spouse_of", (ctx, n) => ctx.This is Character ch && ch.Spouse != null && ReferenceEquals(ch.Spouse, s.ArgScope(n, ctx)));
        s.RegisterTrigger("is_child_of", (ctx, n) => ctx.This is Character ch && s.ArgScope(n, ctx) is Character p && (ch.Father == p || ch.Mother == p));
        s.RegisterTrigger("is_parent_of", (ctx, n) => ctx.This is Character ch && s.ArgScope(n, ctx) is Character k && (k.Father == ch || k.Mother == ch));
        s.RegisterTrigger("family", (ctx, n) => string.Equals((ctx.This as Character)?.Family, n.Value, StringComparison.OrdinalIgnoreCase));
        _ = w;
    }
}
