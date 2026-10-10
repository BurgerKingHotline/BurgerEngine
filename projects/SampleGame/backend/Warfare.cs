// Warfare rules: who may enter where, movement speed, organisation recovery and division power.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable movement and military rules. Subclass and assign to <c>Game.Rules.Warfare</c>.
/// (Combat itself is swapped with <see cref="ICombatModel"/>.)
/// </summary>
public class GameWarfareRules : WarfareRules
{
    /// <summary>May units of <paramref name="country"/> enter this location?</summary>
    public override bool CanEnter(Game game, Country country, Location l)
    {
        if (!l.IsPassable) return false;
        var ctrl = l.Controller ?? l.Owner;
        if (ctrl == null || ctrl == country) return true;
        var w = game.World;
        var r = w.Relations;
        if (r.AreAllied(country, ctrl)) return true;
        if (r.OverlordOf(country) == ctrl || r.OverlordOf(ctrl) == country) return true;
        if (r.HasAccess(country, ctrl)) return true;
        foreach (var war in w.Wars)
        {
            if (!war.Involves(country) || !war.Involves(ctrl)) continue;
            return true; // enemies (attack) or co-belligerents (move through)
        }
        return false;
    }

    /// <summary>Hours a division needs to cross from one location to the next.</summary>
    public override double MoveHours(Game game, Division d, Adjacency adj)
    {
        double speed = Math.Max(0.5, d.Stat("speed", 4));
        double h = adj.DistanceKm / (speed * Math.Max(0.1, game.Modifiers.Factor(d.Owner, "speed"))) * Math.Max(0.1, adj.To.Terrain.MovementCost);
        if (adj.River) h *= 1.25;
        if (adj.Kind == AdjacencyKind.Strait) h *= 2;
        if (d.Retreating) h *= 0.75;
        return Math.Max(1, h);
    }

    /// <summary>Daily organisation recovery for a division not in battle.</summary>
    public override double OrgRecovery(Game game, Division d)
    {
        double r = game.ScriptValues.TryEval("division_org_recovery", d.Owner, out var scripted)
            ? d.MaxOrg * scripted
            : d.MaxOrg * game.Data.Defines.OrgRecoveryRate * d.Stat("org_recovery", 1) * game.Modifiers.Factor(d.Owner, "org_recovery");
        if (d.IsMoving) r *= 0.5;
        return r;
    }

    /// <summary>
    /// Multiplier on a division's combat power (set every hour before combat): supply, and anything features add
    /// through <see cref="Game"/> (air superiority, landing from the sea...).
    /// </summary>
    public override double CombatFactor(Game game, Division d)
    {
        double f = 1;
        if (game.Features.HasSupply) f *= 0.5 + 0.5 * d.Supply;
        foreach (var extra in game.Warfare.CombatFactorSources) f *= extra(d);
        return Math.Max(0.05, f);
    }

    /// <summary>Rough combat power of a division, used by the AI and front planning.</summary>
    public override double DivisionPower(Game game, Division d)
    {
        double s = d.Stat("soft_attack") + d.Stat("hard_attack") + d.Stat("defense") * 0.5 + d.Stat("breakthrough") * 0.5
                   + 10 * (d.Stat("fire") + d.Stat("shock") + d.Stat("offense") + d.Stat("defensive"));
        if (s <= 0) s = d.MaxStrength;
        return s * (0.4 * d.StrengthFraction + 0.6 * d.OrgFraction + 0.05);
    }
}

/// <summary>Installs the warfare rules.</summary>
public sealed class WarfareSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Warfare = new GameWarfareRules();
    }
}
