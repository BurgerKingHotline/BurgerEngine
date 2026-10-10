// Your own combat model, starting from an almost empty template.
// It is active while common/defines.txt says: combat_model = custom
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
//
// How battles work in the engine:
// - The engine starts a battle when enemy divisions meet, calls OnBattleStart once, then Tick every in-game hour.
// - Your model only lowers Division.Strength (men) and Division.Org (organisation / morale).
// - The engine ends the battle: a side retreats when its organisation reaches 0 and is destroyed at 0 strength.
// - Useful helpers in CombatMath: Frontline (who fights, by combat width), CombatWidth, Damage (applies losses
//   and counts them for the battle window), GeneralMultiplier, CountryMultiplier, RiverCrossing, UpdateProgress.
// - Division stats come from common/units (any stat name you add there can be read with d.Stat("my_stat")).

public sealed class CustomCombat : ICombatModel
{
    public string Id => "custom";
    public string Name => "Custom combat";

    public void OnBattleStart(Battle battle, Game game)
    {
        // store anything the battle needs between ticks in battle.Data, e.g. battle.Data["phase"] = 0;
    }

    public void Tick(Battle b, Game game)
    {
        if (b.Hours % 24 != 0) return; // resolve once per day (remove this line for hourly rounds)

        double width = CombatMath.CombatWidth(game, b);
        var attackers = CombatMath.Frontline(b.Attackers, width);
        var defenders = CombatMath.Frontline(b.Defenders, width);
        if (attackers.Count == 0 || defenders.Count == 0) return;

        // 1. how hard each side hits
        double attackPower = attackers.Sum(d => d.Stat("soft_attack") * d.StrengthFraction) * CombatMath.GeneralMultiplier(game, b.Attackers, "general_attack");
        double defendPower = defenders.Sum(d => d.Stat("defense") * d.StrengthFraction) * CombatMath.GeneralMultiplier(game, b.Defenders, "general_defense");

        // 2. terrain and forts help the defender
        defendPower *= 1 + b.Location.Terrain.DefenseModifier + b.Location.Fort * game.Data.Defines.FortDefenseBonus;

        // 3. a bit of luck
        attackPower *= 0.8 + 0.4 * game.Rng.NextDouble();
        defendPower *= 0.8 + 0.4 * game.Rng.NextDouble();

        // 4. spread the damage over the enemy frontline: strength (men) and organisation
        foreach (var d in defenders) CombatMath.Damage(b, d, attackPower * 0.02 / defenders.Count, attackPower * 0.1 / defenders.Count);
        foreach (var a in attackers) CombatMath.Damage(b, a, defendPower * 0.02 / attackers.Count, defendPower * 0.1 / attackers.Count);

        CombatMath.UpdateProgress(b); // the attacker/defender bar in the battle window
    }
}

/// <summary>Registers the custom combat model.</summary>
public sealed class CustomCombatSetup : GameScript
{
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Combat.Register(new CustomCombat());
    }
}
