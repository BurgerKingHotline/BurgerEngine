// Supply rules: which land carries supply, how far it reaches (terrain, infrastructure), the capital, local supply,
// supply hubs and railways (hub mode) and how much supply a division uses.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable supply rules. Two modes come with the engine (choose one feature):
/// <list type="bullet">
/// <item><b>supply_range</b>: supply spreads from the capital and from every own, controlled location, getting weaker
/// with every step (rough terrain makes steps longer). It only travels through friendly land, so encircled armies starve.</item>
/// <item><b>supply_hubs</b>: supply comes from the capital and from supply hubs; a hub only works while railways
/// (or friendly land next to the capital) connect it to the capital. Cut the railway and the hub goes dark.</item>
/// </list>
/// Subclass and assign to <c>Game.Rules.Supply</c> (the feature puts an editable copy in backend/Supply.cs).
/// </summary>
public class GameSupplyRules : SupplyRules
{
    /// <summary>Can supply of <paramref name="c"/> pass through this location? (own, allied, subject/overlord or military access land)</summary>
    public override bool Friendly(Game game, Country c, Location l)
    {
        if (!l.IsLand || !l.IsPassable) return false;
        var ctrl = l.Controller ?? l.Owner;
        if (ctrl == null) return false;
        if (ctrl == c) return true;
        var r = game.World.Relations;
        return r.AreAllied(c, ctrl) || r.OverlordOf(c) == ctrl || r.OverlordOf(ctrl) == c || r.HasAccess(c, ctrl) && !game.World.AtWar(c, ctrl);
    }

    /// <summary>Cost of carrying supply into a location (1 = one normal step; rough terrain costs more, infrastructure less).</summary>
    public override double StepCost(Game game, Location l)
    {
        double infra = l.State?.Working("infrastructure") ?? 0;
        return Math.Max(0.3, l.Terrain.MovementCost) / (1 + 0.1 * infra);
    }

    /// <summary>Supply a location produces on its own (own, controlled land: a little; more with infrastructure).</summary>
    public override double LocalSupply(Game game, Country c, Location l)
    {
        if (l.Owner != c || l.Controller != c) return 0;
        double infra = l.State?.Working("infrastructure") ?? 0;
        return game.Data.Defines.Get("supply_local", 2) * (1 + 0.15 * infra);
    }

    /// <summary>Supply sources (location, amount) for range mode.</summary>
    public override IEnumerable<(Location L, double Amount)> RangeSources(Game game, Country c)
    {
        if (c.Capital != null && c.Capital.Controller == c) yield return (c.Capital, game.Data.Defines.Get("supply_capital", 15));
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
            {
                double local = LocalSupply(game, c, l);
                if (local > 0) yield return (l, local);
            }
    }

    /// <summary>Supply sources for hub mode: the capital and every hub connected to it by railways.</summary>
    public override IEnumerable<(Location L, double Amount)> HubSources(Game game, Country c)
    {
        if (c.Capital == null || c.Capital.Controller != c) yield break;
        yield return (c.Capital, game.Data.Defines.Get("supply_capital", 15));
        var connected = RailwayNetwork(game, c);
        foreach (var s in connected)
        {
            double hub = s.Working("supply_hub");
            if (hub <= 0) continue;
            var loc = s.Locations.FirstOrDefault(l => l.Controller != null && Friendly(game, c, l));
            if (loc == null) continue;
            double rail = Math.Min(5, s.Working("railway"));
            yield return (loc, game.Data.Defines.Get("supply_hub", 10) * hub * (0.6 + 0.1 * rail));
        }
        // a little local supply everywhere at home
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
            {
                double local = LocalSupply(game, c, l) * 0.35;
                if (local > 0) yield return (l, local);
            }
    }

    /// <summary>States reachable from the capital's state through friendly states with railways (the capital's neighbours count too).</summary>
    public override HashSet<State> RailwayNetwork(Game game, Country c)
    {
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start == null) return result;
        var q = new Queue<State>();
        result.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            bool rail = s == start || s.Working("railway") > 0.5;
            if (!rail) continue;
            foreach (var l in s.Locations)
            {
                if (!Friendly(game, c, l)) continue;
                foreach (var adj in l.Neighbors)
                {
                    var ns = adj.To.State;
                    if (ns == null || result.Contains(ns) || !adj.To.IsLand) continue;
                    // the next state must be friendly where it touches
                    if (!Friendly(game, c, adj.To)) continue;
                    result.Add(ns);
                    q.Enqueue(ns);
                }
            }
        }
        return result;
    }

    /// <summary>Supply a division needs per day.</summary>
    public override double Usage(Game game, Division d) => d.Stat("supply_use", 0.05 * Math.Max(1, d.Template.Battalions));
}

/// <summary>Installs the supply rules.</summary>
public sealed class SupplySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Supply = new GameSupplyRules();
    }
}
