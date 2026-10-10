// Imperator: Rome style combat model. Active while common/defines.txt says combat_model = imperator.
// Helpers shared by all models (frontline, combat width, damage) are in CombatMath.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Imperator: Rome style: daily rounds with a d6 roll modified by general martial difference, morale and
/// strength damage, flanking width from terrain. Uses stats: offense, defensive, morale (falls back to HOI4 stats).
/// </summary>
public sealed class ImperatorCombat : ICombatModel
{
    public string Id => "imperator";
    public string Name => "Imperator: Rome";

    public void OnBattleStart(Battle battle, Game game) { }

    public void Tick(Battle b, Game game)
    {
        if (b.Hours % 24 != 0) return;
        var def = game.Data.Defines;
        double width = CombatMath.CombatWidth(game, b);
        var frontA = CombatMath.Frontline(b.Attackers, width);
        var frontD = CombatMath.Frontline(b.Defenders, width);
        if (frontA.Count == 0 || frontD.Count == 0) return;

        double martialA = CombatMath.GeneralSkill(game, b.Attackers), martialD = CombatMath.GeneralSkill(game, b.Defenders);
        int rollA = 1 + game.Rng.Next(6), rollD = 1 + game.Rng.Next(6);
        double tacticA = 1 + 0.1 * (rollA - rollD) + 0.05 * (martialA - martialD) - (CombatMath.RiverCrossing(b) ? def.RiverAttackPenalty : 0);
        double tacticD = 1 + 0.1 * (rollD - rollA) + 0.05 * (martialD - martialA) + b.Location.Terrain.DefenseModifier + b.Location.Fort * def.FortDefenseBonus;

        double offA = frontA.Sum(x => Offense(x) * x.StrengthFraction) * Math.Max(0.2, tacticA) * CombatMath.CountryMultiplier(game, b.Attackers, "attack");
        double offD = frontD.Sum(x => Offense(x) * x.StrengthFraction) * Math.Max(0.2, tacticD) * CombatMath.CountryMultiplier(game, b.Defenders, "defense");
        foreach (var t in frontD) Hit(b, t, offA / frontD.Count);
        foreach (var t in frontA) Hit(b, t, offD / frontA.Count);
        CombatMath.UpdateProgress(b);
    }

    private static double Offense(Division d) => d.Stat("offense", (d.Stat("soft_attack") + d.Stat("hard_attack")) / 10.0 + 1);

    private static void Hit(Battle b, Division t, double offense)
    {
        double defensive = t.Stat("defensive", t.Stat("defense") / 40.0 + 1);
        double frac = 0.02 * offense / Math.Max(0.5, defensive);
        double morale = t.Stat("morale", t.MaxOrg / 20.0);
        CombatMath.Damage(b, t, frac * t.MaxStrength, frac * t.MaxOrg * 4 / Math.Max(0.5, morale / 3));
    }
}

/// <summary>Registers the combat model (same id as the engine one, so this copy replaces it).</summary>
public sealed class ImperatorCombatSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Combat.Register(new ImperatorCombat());
    }
}
