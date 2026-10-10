// Naval rules: what counts as a port, ship damage and targeting (screens protect capital ships), when fleets retreat,
// repairs, transport capacity, how long armies take to cross the sea and the naval invasion penalty.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>Overridable naval rules. Subclass and assign to <c>Game.Rules.Navy</c> (the navy feature puts a copy in backend/Navy.cs).</summary>
public class GameNavyRules : NavyRules
{
    /// <summary>Can fleets of <paramref name="c"/> use this land location as a port? (own or allied coastal land)</summary>
    public override bool IsPort(Game game, Country c, Location l)
    {
        if (!l.IsLand || !l.IsCoastal || l.Controller == null) return false;
        return l.Controller == c || game.World.Relations.AreAllied(c, l.Controller) || game.World.Relations.HasAccess(c, l.Controller) && !game.World.AtWar(c, l.Controller);
    }

    /// <summary>Damage one ship does to another in one hour of battle.</summary>
    public override double Damage(Game game, Ship shooter, Ship target)
    {
        bool capitalTarget = target.Type.Category.Equals("capital", StringComparison.OrdinalIgnoreCase);
        double attack = capitalTarget ? Math.Max(shooter.Type.Stat("heavy_attack"), shooter.Type.Stat("light_attack") * 0.3) : shooter.Type.Stat("light_attack") + shooter.Type.Stat("heavy_attack") * 0.5;
        double armor = target.Type.Stat("armor");
        double hit = game.Rng.NextDouble() < 0.35 ? 1 : 0;
        return attack * hit * (0.5 + game.Rng.NextDouble()) * 10 / (10 + armor) * shooter.HpFraction;
    }

    /// <summary>Chooses what a ship shoots at: screens shield capital ships while they last.</summary>
    public override Ship? PickTarget(Game game, Ship shooter, List<Ship> enemies)
    {
        var alive = enemies.Where(s => s.Hp > 0).ToList();
        if (alive.Count == 0) return null;
        var screens = alive.Where(s => s.Type.Category.Equals("screen", StringComparison.OrdinalIgnoreCase)).ToList();
        bool heavy = shooter.Type.Stat("heavy_attack") > shooter.Type.Stat("light_attack");
        var pool = heavy ? alive.Where(s => s.Type.Category.Equals("capital", StringComparison.OrdinalIgnoreCase)).ToList() : screens;
        // capital ships can only be reached once most screens are gone (screening)
        if (!heavy && pool.Count == 0) pool = alive;
        if (heavy && pool.Count == 0) pool = alive;
        if (!heavy && screens.Count > 0 && game.Rng.NextDouble() < 0.85) pool = screens;
        return pool[game.Rng.Next(pool.Count)];
    }

    /// <summary>A side whose ships have less than this share of their hit points left retreats.</summary>
    public override double RetreatBelow(Game game) => game.Data.Defines.Get("naval_retreat", 0.4);

    /// <summary>Share of hit points repaired per day in port.</summary>
    public override double RepairPerDay(Game game, Fleet f)
    {
        double bases = f.Location.State?.Buildings.GetValueOrDefault("naval_base") ?? 0;
        return 0.03 + 0.02 * bases;
    }

    /// <summary>Divisions a country can carry over sea at once (transport ships).</summary>
    public override double TransportCapacity(Game game, Country c)
        => game.Navy.FleetsOf(c).SelectMany(f => f.Ships).Sum(s => s.Type.Stat("transport"));

    /// <summary>Hours an army needs to cross one sea step.</summary>
    public override double SeaStepHours(Game game, Adjacency adj) => Math.Max(6, adj.DistanceKm / game.Data.Defines.Get("transport_speed", 12));

    /// <summary>Combat multiplier of divisions that attack from the sea (naval invasion).</summary>
    public override double InvasionFactor(Game game) => game.Data.Defines.Get("naval_invasion_penalty", 0.5);

    /// <summary>Share of strength unescorted armies at sea lose per hour when an enemy fleet is in the same sea.</summary>
    public override double ConvoyLosses(Game game) => 0.15;
}

/// <summary>Installs the naval rules.</summary>
public sealed class NavySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Navy = new GameNavyRules();
    }
}
