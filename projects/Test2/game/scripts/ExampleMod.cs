// =====================================================================================================
// Example C# mod script.
//
// Most new mechanics need NO C#: see common/resources, common/modifiers, common/decisions,
// common/script_values and interface/ in this project. Use C# when you want to change how the engine
// itself works.
//
// Every .cs file in scripts/ is compiled when the game starts; every class deriving from GameScript is
// created automatically. Compile errors appear in the error log (F2) with file and line.
//
// From here you can:
//   * add triggers / effects / values for the .txt files      (Game.Script.RegisterTrigger/Effect/Value)
//   * add a combat model                                       (Game.Combat.Register)
//   * replace diplomacy, economy, character or movement rules  (Game.Rules.X = new ...)
//   * replace the AI                                           (Game.Ai = new MyAi(Game))
//   * add windows and map modes                                (Game.Interface.AddPanel / AddMapMode)
//   * react to anything that happens                           (override On... methods)
//   * add console commands                                     (Game.Commands.Register)
// =====================================================================================================

public class ExampleMod : GameScript
{
    public override void OnLoad()
    {
        // 1) A new trigger usable in any .txt file:   is_great_power = yes
        Game.Script.RegisterTrigger("is_great_power",
            (ctx, node) => (ctx.CountryOfThis?.OwnedStates.Count >= 8) == node.AsBool(true),
            (ctx, node) => node.AsBool(true) ? "Is a great power (8+ states)" : "Is not a great power");

        // 2) A custom combat model. Activate with  combat_model = lanchester  in common/defines.txt
        //    or type  combat lanchester  in the console (`).
        Game.Combat.Register(new LanchesterCombat());

        // 3) A console command
        Game.Commands.Register("prestige", "show the prestige of every country", args =>
            string.Join("\n", Game.World.Countries.Where(c => c.Exists)
                .OrderByDescending(c => c.Vars.Get("prestige"))
                .Select(c => $"{c.Tag} {c.Name}: {c.Vars.Get("prestige"):0}")));

        // 4) A map mode written in C# (data map modes live in interface/*.txt)
        Game.Interface.AddMapMode(new MapModeDef
        {
            Id = "army_presence",
            Name = "Armies",
            ColorFn = loc => Game.Warfare.DivisionsAt(loc).Count > 0 ? new Rgb(220, 60, 40) : null,
        });

        // 5) Rules can be swapped completely, e.g.:
        // Game.Rules.Diplomacy = new PeacefulDiplomacy();
    }

    public override void OnBattleEnded(Battle battle)
    {
        // winning battles gives prestige (prestige is a resource from common/resources)
        var winner = battle.AttackerWon ? battle.AttackerCountry : battle.DefenderCountry;
        winner.Vars.Set("prestige", Math.Min(100, winner.Vars.Get("prestige") + 1));
    }
}

/// <summary>Lanchester's square law: each side loses strength proportional to the enemy's total firepower.</summary>
public class LanchesterCombat : ICombatModel
{
    public string Id => "lanchester";
    public string Name => "Lanchester square law (example mod)";

    public void OnBattleStart(Battle battle, Game game) { }

    public void Tick(Battle b, Game game)
    {
        if (b.Hours % 6 != 0) return; // resolve every 6 hours
        double powerA = b.Attackers.Sum(d => (d.Stat("soft_attack") + d.Stat("hard_attack")) * d.StrengthFraction) * CombatMath.CountryMultiplier(game, b.Attackers, "attack");
        double powerD = b.Defenders.Sum(d => (d.Stat("soft_attack") + d.Stat("defense") * 0.2) * d.StrengthFraction)
                        * (1 + b.Location.Terrain.DefenseModifier) * CombatMath.CountryMultiplier(game, b.Defenders, "defense");
        foreach (var d in b.Defenders) CombatMath.Damage(b, d, powerA * 0.004 / b.Defenders.Count * d.MaxStrength / 10, powerA * 0.01 / b.Defenders.Count);
        foreach (var a in b.Attackers) CombatMath.Damage(b, a, powerD * 0.004 / b.Attackers.Count * a.MaxStrength / 10, powerD * 0.01 / b.Attackers.Count);
        CombatMath.UpdateProgress(b);
    }
}

/// <summary>Example of replacing a rule set (not active - see OnLoad above).</summary>
public class PeacefulDiplomacy : DiplomacyRules
{
    public override bool WillJoinWar(Game game, Country ally, War war, bool asAttacker, Country caller)
        => !asAttacker; // allies only ever help defensively
}
