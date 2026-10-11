using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Scripting;

/// <summary>Triggers, values and effects of technology, commodities, AI profiles and demilitarization.</summary>
public static partial class Builtins
{
    private static void RegisterExtras(Game game)
    {
        var s = game.Script;
        // ---- technology
        s.RegisterTrigger("has_tech", (ctx, n) => C(ctx.This) is { } c && game.Tech.Has(c, n.Value ?? ""), (ctx, n) => $"Knows {game.Tech.Defs.GetValueOrDefault(n.Value ?? "")?.Name ?? n.Value}");
        s.RegisterValue("num_of_techs", (ctx, x) => C(x) is { } c ? game.Tech.Count(c) : null);
        s.RegisterEffect("grant_tech", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) game.Tech.Grant(c, n.Value ?? "", silent: false); }, (ctx, n) => $"Learns {game.Tech.Defs.GetValueOrDefault(n.Value ?? "")?.Name ?? n.Value}");
        // ---- AI personality
        s.RegisterTrigger("ai_archetype", (ctx, n) => C(ctx.This) is { } c && string.Equals(game.AiProfiles.PersonalityOf(c).Archetype, n.Value, StringComparison.OrdinalIgnoreCase), (ctx, n) => $"Has the {n.Value} outlook");
        s.RegisterValue("ai_aggression", (ctx, x) => C(x) is { } c ? game.AiProfiles.PersonalityOf(c).Trait("aggression") : null);
        s.RegisterValue("ai_expansion", (ctx, x) => C(x) is { } c ? game.AiProfiles.PersonalityOf(c).Trait("expansion") : null);
        s.RegisterValue("ai_risk", (ctx, x) => C(x) is { } c ? game.AiProfiles.PersonalityOf(c).Trait("risk") : null);
        s.RegisterValue("ai_defense", (ctx, x) => C(x) is { } c ? game.AiProfiles.PersonalityOf(c).Trait("defense") : null);
        s.RegisterValue("ai_isolation", (ctx, x) => C(x) is { } c ? game.AiProfiles.PersonalityOf(c).Trait("isolation") : null);
        s.RegisterValue("historical_bias", (ctx, x) => game.HistoricalBias);
        // ---- commodities
        s.RegisterValue("commodity_shortage", (ctx, x) => C(x) is { } c && game.Commodities.Enabled ? game.Commodities.Shortages(c).Sum(t => 1 - t.Satisfied) : (double?)0);
        s.RegisterValue("oil_satisfied", (ctx, x) => C(x) is { } c && game.Commodities.Enabled ? game.Commodities.BalanceOf(c, "oil").Satisfied : (double?)1);
        s.RegisterValue("steel_satisfied", (ctx, x) => C(x) is { } c && game.Commodities.Enabled ? game.Commodities.BalanceOf(c, "steel").Satisfied : (double?)1);
        // ---- AI ambitions: ai_ambition = { target = ETH min = 1 } - the country's history wants that country's land (profile want, once awakened)
        s.RegisterTrigger("ai_ambition", (ctx, n) => { bool r = C(ctx.This) is { } c && game.World.GetCountry(n.GetString("target") ?? "") is { } t && game.Rules.AiProfile.Ambition(game, c, t, game.AiProfiles.PersonalityOf(c)) >= n.GetDouble("min", 0.01); if (Environment.GetEnvironmentVariable("BURGER_AMB_LOG") != null) Console.WriteLine($"AMB {game.World.Date.ToDisplay()} {C(ctx.This)?.Tag} {n.GetString("target")} min {n.GetDouble("min", 0.01)} => {r} (profile {(C(ctx.This) is { } cc ? game.AiProfiles.PersonalityOf(cc).Profile?.Key : null)})"); return r; },
            (ctx, n) => $"Has ambitions on {n.GetString("target")}");
        // can_demand = { target = AUS action = demand_subjugation }: the diplomatic action can be put to that country now
        s.RegisterTrigger("can_demand", (ctx, n) => C(ctx.This) is { } c && game.World.GetCountry(n.GetString("target") ?? "") is { } t
            && game.Data.DiplomaticActions.GetValueOrDefault(n.GetString("action", "demand_state")!) is { } a && game.Diplomacy.CanUse(a, c, t),
            (ctx, n) => $"Can put a demand to {n.GetString("target")}");
        // ---- focuses
        s.RegisterTrigger("has_completed_focus", (ctx, n) => C(ctx.This) is { } c && game.Focus.IsDone(c, n.Value ?? ""), (ctx, n) => $"Has completed the focus {game.Focus.Defs.GetValueOrDefault(n.Value ?? "")?.Name ?? n.Value}");
        s.RegisterTrigger("is_working_on_focus", (ctx, n) => C(ctx.This) is { } c && game.Focus.Current(c)?.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase) == true);
        s.RegisterValue("num_of_focuses", (ctx, x) => C(x) is { } c ? game.Focus.DoneCount(c) : null);
        // make_demand = TAG   or   make_demand = { target = TAG action = demand_subjugation }: puts a demand to another country (the action's own acceptance rules decide)
        s.RegisterEffect("propose_action", MakeDemand(game), (ctx, n) => "Puts a proposal to " + (n.Value ?? n.GetString("target")));
        s.RegisterEffect("make_demand", MakeDemand(game), (ctx, n) => "Puts a demand to " + (n.Value ?? n.GetString("target")));
        // ---- demilitarization
        s.RegisterTrigger("has_demilitarized_states", (ctx, n) => C(ctx.This) is { } c && YesNo(n, game.Diplomacy.DemilitarizedOf(c).Count > 0));
        s.RegisterTrigger("demilitarized", (ctx, n) => ctx.This is State st ? YesNo(n, st.Demilitarized) : ctx.This is Location l && l.State != null && YesNo(n, l.State.Demilitarized));
        s.RegisterValue("remilitarization_odds", (ctx, x) => C(x) is { } c ? game.Diplomacy.RemilitarizationOdds(c) : null);
        s.RegisterEffect("remilitarize_states", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) game.Diplomacy.RemilitarizeAll(c); }, (ctx, n) => "Remilitarizes its demilitarized states");
        s.RegisterEffect("demilitarize_state", (ctx, n) => { if (ctx.This is State st) game.Diplomacy.Demilitarize(st, null); }, (ctx, n) => "The state is demilitarized");
    }

    // make_demand = TAG  /  make_demand = { target = TAG action = demand_subjugation }; propose_action is the same for friendly actions (offer_alliance ...)
    private static EffectFn MakeDemand(Game game) => (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is not { } c) return;
            IScope? target = null;
            string action = "demand_state";
            if (n.Children != null) { action = n.GetString("action", "demand_state")!; if (n.GetString("target") is { } t) game.Script.TryResolveScope(t, ctx, out target); }
            else if (n.Value != null) game.Script.TryResolveScope(n.Value, ctx, out target);
            var a = game.Data.DiplomaticActions.GetValueOrDefault(action);
            if (a == null || target == null || target == c) return;
            bool can = game.Diplomacy.CanUse(a, c, target);
            if (Environment.GetEnvironmentVariable("BURGER_FOCUS_LOG") != null) Console.WriteLine($"FOCUS {game.World.Date.ToDisplay()} {c.Tag} {action} -> {target.DisplayName}: {(can ? "put" : "not possible")}");
            if (can) game.Diplomacy.Use(a, c, target);
        };
}
