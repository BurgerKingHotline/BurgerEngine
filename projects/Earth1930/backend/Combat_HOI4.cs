// Hearts of Iron IV style combat model. Active while common/defines.txt says combat_model = hoi4.
// Helpers shared by all models (frontline, combat width, damage) are in CombatMath.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Hearts of Iron IV style: hourly rounds, combat width, soft/hard attack vs hardness, defense/breakthrough
/// absorbing hits, armor vs piercing, organisation and strength damage.
/// Uses stats: soft_attack, hard_attack, defense, breakthrough, hardness, armor, piercing, combat_width.
/// </summary>
public sealed class Hoi4Combat : ICombatModel
{
    public string Id => "hoi4";
    public string Name => "Hearts of Iron IV";

    public void OnBattleStart(Battle battle, Game game) { }

    public void Tick(Battle b, Game game)
    {
        var d = game.Data.Defines;
        var terrain = b.Location.Terrain;
        double width = CombatMath.CombatWidth(game, b);
        var frontA = CombatMath.Frontline(b.Attackers, width);
        var frontD = CombatMath.Frontline(b.Defenders, width);
        if (frontA.Count == 0 || frontD.Count == 0) return;

        double attMult = (1 + terrain.AttackModifier) * (CombatMath.RiverCrossing(b) ? 1 - d.RiverAttackPenalty : 1)
                         * CombatMath.GeneralMultiplier(game, b.Attackers, "general_attack");
        double defMult = (1 + terrain.DefenseModifier + b.Location.Fort * d.FortDefenseBonus)
                         * CombatMath.GeneralMultiplier(game, b.Defenders, "general_defense");

        foreach (var a in frontA)
            Attack(game, b, a, frontD[game.Rng.Next(frontD.Count)], attMult, "defense", defMult);
        foreach (var x in frontD)
            Attack(game, b, x, frontA[game.Rng.Next(frontA.Count)], defMult, "breakthrough", attMult);
        CombatMath.UpdateProgress(b);
    }

    private static void Attack(Game game, Battle b, Division src, Division tgt, double srcMult, string tgtDefStat, double tgtMult)
    {
        var d = game.Data.Defines;
        double hardness = Math.Clamp(tgt.Stat("hardness"), 0, 1);
        double attacks = (src.Stat("soft_attack") * (1 - hardness) + src.Stat("hard_attack") * hardness)
                         * srcMult * (0.4 + 0.6 * src.StrengthFraction) / 10.0;
        if (tgt.Stat("armor") > src.Stat("piercing")) attacks *= 0.5;
        double defense = tgt.Stat(tgtDefStat) * tgtMult * (0.4 + 0.6 * tgt.StrengthFraction) / 10.0;
        double blocked = Math.Min(attacks, defense);
        double hits = blocked * 0.1 + (attacks - blocked) * 0.4;
        double rnd = 0.75 + 0.5 * game.Rng.NextDouble();
        double orgMult = src.Stat("armor") > tgt.Stat("piercing") ? 1.5 : 1.0;
        CombatMath.Damage(b, tgt, hits * d.Hoi4StrengthDamage * rnd * 10, hits * d.Hoi4OrgDamage * rnd * 10 * orgMult);
    }
}

/// <summary>Registers the combat model (same id as the engine one, so this copy replaces it).</summary>
public sealed class Hoi4CombatSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Combat.Register(new Hoi4Combat());
    }
}
