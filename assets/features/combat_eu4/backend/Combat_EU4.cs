// Europa Universalis style combat model. Active while common/defines.txt says combat_model = eu4.
// Helpers shared by all models (frontline, combat width, damage) are in CombatMath.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Europa Universalis IV style: daily dice rolls, alternating fire and shock phases (3 days each), morale.
/// Uses stats: fire, shock, morale (falls back to soft_attack/hard_attack/max_organisation), discipline.
/// </summary>
public sealed class Eu4Combat : ICombatModel
{
    public string Id => "eu4";
    public string Name => "Europa Universalis IV";

    public void OnBattleStart(Battle battle, Game game)
    {
        battle.Data["day"] = 0;
    }

    public void Tick(Battle b, Game game)
    {
        if (b.Hours % 24 != 0) return; // daily resolution
        var def = game.Data.Defines;
        int day = (int)b.Data.GetValueOrDefault("day");
        b.Data["day"] = day + 1;
        bool fire = (day / def.Eu4PhaseDays) % 2 == 0;

        double width = CombatMath.CombatWidth(game, b);
        var frontA = CombatMath.Frontline(b.Attackers, width);
        var frontD = CombatMath.Frontline(b.Defenders, width);
        if (frontA.Count == 0 || frontD.Count == 0) return;

        int rollA = game.Rng.Next(10), rollD = game.Rng.Next(10);
        double skillA = CombatMath.GeneralSkill(game, b.Attackers), skillD = CombatMath.GeneralSkill(game, b.Defenders);
        // general pips: difference in skill, halved (EU4 generals have 0-6 pips)
        double pipsA = Math.Max(0, (skillA - skillD) / 2), pipsD = Math.Max(0, (skillD - skillA) / 2);
        double multA = CombatMath.CountryMultiplier(game, b.Attackers, "attack"), multD = CombatMath.CountryMultiplier(game, b.Defenders, "defense");
        double terrainPenalty = (b.Location.Terrain.DefenseModifier > 0.1 ? 1 : 0) + (CombatMath.RiverCrossing(b) ? 1 : 0);

        double dmgToD = SideDamage(frontA, fire, rollA + pipsA - terrainPenalty, def) * multA;
        double dmgToA = SideDamage(frontD, fire, rollD + pipsD + b.Location.Fort * 0.5, def) * multD;
        Apply(game, b, frontD, dmgToD, def);
        Apply(game, b, frontA, dmgToA, def);
        // daily morale drain for everybody in battle
        foreach (var x in frontA.Concat(frontD)) x.Org = Math.Max(0, x.Org - x.MaxOrg * 0.01);
        CombatMath.UpdateProgress(b);
    }

    private static double PhaseStat(Division d, bool fire)
        => fire ? d.Stat("fire", d.Stat("soft_attack") / 20.0) : d.Stat("shock", (d.Stat("soft_attack") + d.Stat("hard_attack")) / 20.0);

    private static double SideDamage(List<Division> side, bool fire, double roll, Defines def)
    {
        double total = 0;
        foreach (var d in side)
        {
            double basic = Math.Max(0, def.Eu4BaseCasualties + 5 * roll);
            total += basic * (0.5 + PhaseStat(d, fire) * 0.25) * d.StrengthFraction * (1 + d.Stat("discipline"));
        }
        return total;
    }

    private static void Apply(Game game, Battle b, List<Division> targets, double damage, Defines def)
    {
        if (targets.Count == 0) return;
        double per = damage / targets.Count;
        foreach (var t in targets)
        {
            double frac = per / 1000.0 / (1 + t.Stat("discipline") * 0.5);
            double morale = t.Stat("morale", t.MaxOrg / 20.0);
            CombatMath.Damage(b, t, frac * t.MaxStrength, frac * t.MaxOrg * def.Eu4MoraleDamage * 50 / Math.Max(0.5, morale / 3));
        }
    }
}

/// <summary>Registers the combat model (same id as the engine one, so this copy replaces it).</summary>
public sealed class Eu4CombatSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Combat.Register(new Eu4Combat());
    }
}
