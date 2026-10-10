// Air rules: wing size, air base capacity, which bases a country may use, ranges, air superiority and its combat bonus,
// losses, close air support and bombing damage, bombing targets and repairs.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable air rules (HOI4 style). Subclass and assign to <c>Game.Rules.Air</c> (the air feature puts a copy in backend/Air.cs).
/// </summary>
public class GameAirRules : AirRules
{
    /// <summary>Planes in a full wing.</summary>
    public override double WingSize(Game game) => Math.Max(1, game.Data.Defines.Get("air_wing_size", 50));

    /// <summary>Planes a state's air base can hold (0 = no air base).</summary>
    public override double BaseCapacity(Game game, State s) => s.Working("air_base") * game.Data.Defines.Get("air_base_capacity", 100);

    /// <summary>Can wings of <paramref name="c"/> be based in this state? (own or allied state with an air base, controlled by a friend)</summary>
    public override bool CanUseBase(Game game, Country c, State s)
    {
        if (s.Buildings.GetValueOrDefault("air_base") <= 0 || s.Owner == null) return false;
        var ctrl = s.Locations.Count > 0 ? s.Locations[0].Controller ?? s.Owner : s.Owner;
        bool friend(Country x) => x == c || game.World.Relations.AreAllied(c, x);
        return friend(s.Owner) && friend(ctrl);
    }

    /// <summary>Can this plane type fly the mission? (needs the matching attack stat)</summary>
    public override bool CanFly(Game game, PlaneType t, AirMission m) => m switch
    {
        AirMission.Superiority => t.Stat("air_attack") > 0,
        AirMission.CloseSupport => t.Stat("ground_attack") > 0,
        AirMission.Bombing => t.Stat("strategic_attack") > 0 && game.Features.Has(FeatureSet.Production),
        _ => true,
    };

    /// <summary>Km from the wing's base to the center of the region.</summary>
    public override double DistanceKm(Game game, State from, AirRegion to)
    {
        if (from.Locations.Count == 0) return double.MaxValue;
        float x = from.Locations.Average(l => l.CenterX), y = from.Locations.Average(l => l.CenterY);
        double dx = x - to.CenterX, dy = y - to.CenterY;
        return Math.Sqrt(dx * dx + dy * dy) * game.Data.Defines.KmPerPixel;
    }

    public override bool InRange(Game game, AirWing w, AirRegion r) => r.States.Contains(w.Base) || DistanceKm(game, w.Base, r) <= w.Type.Range;

    /// <summary>Air superiority strength of a wing (superiority mission).</summary>
    public override double AirPower(Game game, AirWing w)
        => w.Planes * (w.Type.Stat("air_attack") + 0.5 * w.Type.Stat("air_defense") + 0.25 * w.Type.Stat("agility"));

    /// <summary>
    /// Combat multiplier of a division from air superiority over its region: share 0..1 of the air power of its side.
    /// 0.5 (or no planes) = no effect; full superiority gives +air_superiority_bonus, none gives -air_superiority_bonus.
    /// </summary>
    public override double SuperiorityFactor(Game game, Division d, double share, bool contested)
        => contested ? 1 + game.Data.Defines.Get("air_superiority_bonus", 0.2) * (share * 2 - 1) : 1;

    /// <summary>Share of planes a superiority wing loses per day against enemy fighters (0.5 = even fight).</summary>
    public override double DogfightLosses(Game game, AirWing w, double enemyShare)
        => game.Data.Defines.Get("air_loss_rate", 0.03) * enemyShare * 10 / (10 + w.Type.Stat("air_defense"));

    /// <summary>Share of planes a CAS or bomber wing loses per day to enemy fighters (scaled by their air superiority).</summary>
    public override double InterceptLosses(Game game, AirWing w, double enemyShare)
        => game.Data.Defines.Get("air_loss_rate", 0.03) * 1.5 * enemyShare * 10 / (10 + w.Type.Stat("air_defense"));

    /// <summary>Daily damage (organisation points per division in a battle) of a close air support wing.</summary>
    public override double CasDamage(Game game, AirWing w, double friendlyShare)
        => w.Planes * w.Type.Stat("ground_attack") * game.Data.Defines.Get("cas_damage", 0.02) * (0.3 + 0.7 * friendlyShare);

    /// <summary>Building levels a bomber wing knocks out per day.</summary>
    public override double BombingDamage(Game game, AirWing w, double friendlyShare)
        => w.Planes * w.Type.Stat("strategic_attack") * game.Data.Defines.Get("bombing_damage", 0.0005) * (0.2 + 0.8 * friendlyShare);

    /// <summary>Buildings bombers aim at, most important first.</summary>
    public override IReadOnlyList<string> BombingTargets(Game game) => new[] { "military_factory", "civilian_factory", "dockyard", "supply_hub", "railway", "infrastructure" };

    /// <summary>Damaged building levels repaired per day.</summary>
    public override double RepairPerDay(Game game, State s, string building) => game.Data.Defines.Get("bomb_repair", 0.05);
}

/// <summary>Installs the air rules.</summary>
public sealed class AirSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Air = new GameAirRules();
    }
}
