using Burger.Core.Model;

namespace Burger.Core.Warfare;

/// <summary>
/// A pluggable land combat resolver. The active model is chosen with <c>combat_model = hoi4</c> in
/// <c>common/defines.txt</c>. Built-in: <c>hoi4</c>, <c>eu4</c>, <c>imperator</c>. Register your own from a C# script:
/// <code>Game.Combat.Register(new MyCombatModel());</code>
/// <para>The engine handles battle creation, retreats (organisation/morale ≤ 0), destruction (strength ≤ 0) and
/// ending battles. A model only needs to change <see cref="Division.Strength"/> and <see cref="Division.Org"/>
/// (and optionally <see cref="Battle.AttackerLosses"/>/<see cref="Battle.DefenderLosses"/>).</para>
/// </summary>
public interface ICombatModel
{
    string Id { get; }
    string Name { get; }
    /// <summary>Called once when a battle starts.</summary>
    void OnBattleStart(Battle battle, Game game);
    /// <summary>Called every in-game hour for every active battle.</summary>
    void Tick(Battle battle, Game game);
}

public sealed class CombatRegistry
{
    private readonly Dictionary<string, ICombatModel> _models = new(StringComparer.OrdinalIgnoreCase);

    public CombatRegistry()
    {
        Register(new Hoi4CombatModel());
        Register(new Eu4CombatModel());
        Register(new ImperatorCombatModel());
    }

    public IEnumerable<ICombatModel> Models => _models.Values;

    public void Register(ICombatModel model) => _models[model.Id] = model;

    public ICombatModel Get(string id)
    {
        if (_models.TryGetValue(id, out var m)) return m;
        Log.ErrorOnce("combat:" + id, $"Unknown combat_model '{id}' - using hoi4. Available: {string.Join(", ", _models.Keys)}", "common/defines.txt");
        return _models["hoi4"];
    }

    public ICombatModel Current(Game game) => Get(game.Data.Defines.CombatModel);
}

/// <summary>Shared helpers for combat models.</summary>
public static class CombatMath
{
    /// <summary>Multiplier from the side's general: stat × general_bonus_per_point + trait modifier.</summary>
    public static double GeneralMultiplier(Game game, IEnumerable<Division> side, string traitModifier)
    {
        double mult = CountryMultiplier(game, side, traitModifier == "general_defense" ? "defense" : "attack");
        var general = side.Select(d => d.Army?.General).FirstOrDefault(g => g != null && g.Alive);
        if (general == null) return mult;
        var d = game.Data.Defines;
        return mult * (1 + general.GetStat(d.GeneralStat) * d.GeneralBonusPerPoint + general.TraitModifier(traitModifier));
    }

    /// <summary>Country-wide combat multiplier: modifiers (attack/defense) and the division_combat_power script value.</summary>
    public static double CountryMultiplier(Game game, IEnumerable<Division> side, string modifierKey)
    {
        var owner = side.Select(d => d.Owner).FirstOrDefault();
        if (owner == null) return 1;
        double m = game.Modifiers.Factor(owner, modifierKey);
        if (game.ScriptValues.TryEval("division_combat_power", owner, out var scripted)) m *= scripted;
        // supply, air superiority, landings... (Division.CombatFactor, set by the engine every hour)
        var list = side as ICollection<Division> ?? side.ToList();
        if (list.Count > 0) m *= list.Average(d => d.CombatFactor);
        return Math.Max(0.05, m);
    }

    public static double GeneralSkill(Game game, IEnumerable<Division> side)
    {
        var general = side.Select(d => d.Army?.General).FirstOrDefault(g => g != null && g.Alive);
        return general?.GetStat(game.Data.Defines.GeneralStat) ?? 0;
    }

    /// <summary>True if any attacker crosses a river into the battle location.</summary>
    public static bool RiverCrossing(Battle b) => b.Attackers.Any(a => a.Location.AdjacencyTo(b.Location)?.River == true);

    /// <summary>Picks divisions for the frontline up to a combat width (highest organisation first).</summary>
    public static List<Division> Frontline(IEnumerable<Division> divisions, double width)
    {
        var result = new List<Division>();
        double used = 0;
        foreach (var d in divisions.Where(x => x.Strength > 0 && x.Org > 0).OrderByDescending(x => x.Org))
        {
            double w = Math.Max(1, d.Stat("combat_width", 10));
            if (result.Count > 0 && used + w > width) continue;
            result.Add(d);
            used += w;
        }
        return result;
    }

    public static double CombatWidth(Game game, Battle b)
    {
        int dirs = Math.Max(1, b.AttackDirections);
        return b.Location.Terrain.CombatWidth * (1 + game.Data.Defines.ExtraWidthPerDirection * (dirs - 1));
    }

    public static void Damage(Battle b, Division target, double strength, double org)
    {
        strength = Math.Max(0, strength);
        org = Math.Max(0, org);
        double before = target.Strength;
        target.Strength = Math.Max(0, target.Strength - strength);
        target.Org = Math.Max(0, target.Org - org);
        double lost = before - target.Strength;
        if (b.Attackers.Contains(target)) b.AttackerLosses += lost; else b.DefenderLosses += lost;
    }

    /// <summary>Updates the UI progress bar from average organisation of both sides.</summary>
    public static void UpdateProgress(Battle b)
    {
        double a = b.Attackers.Count == 0 ? 0 : b.Attackers.Average(x => x.OrgFraction * (0.3 + 0.7 * x.StrengthFraction)) * b.Attackers.Count;
        double d = b.Defenders.Count == 0 ? 0 : b.Defenders.Average(x => x.OrgFraction * (0.3 + 0.7 * x.StrengthFraction)) * b.Defenders.Count;
        b.AttackerProgress = a + d <= 0 ? 0.5 : a / (a + d);
    }
}

/// <summary>
/// Hearts of Iron IV style: hourly rounds, combat width, soft/hard attack vs hardness, defense/breakthrough
/// absorbing hits, armor vs piercing, organisation and strength damage.
/// Uses stats: soft_attack, hard_attack, defense, breakthrough, hardness, armor, piercing, combat_width.
/// </summary>
public sealed class Hoi4CombatModel : ICombatModel
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

/// <summary>
/// Europa Universalis IV style: daily dice rolls, alternating fire and shock phases (3 days each), morale.
/// Uses stats: fire, shock, morale (falls back to soft_attack/hard_attack/max_organisation), discipline.
/// </summary>
public sealed class Eu4CombatModel : ICombatModel
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

    private static double SideDamage(List<Division> side, bool fire, double roll, Data.Defines def)
    {
        double total = 0;
        foreach (var d in side)
        {
            double basic = Math.Max(0, def.Eu4BaseCasualties + 5 * roll);
            total += basic * (0.5 + PhaseStat(d, fire) * 0.25) * d.StrengthFraction * (1 + d.Stat("discipline"));
        }
        return total;
    }

    private static void Apply(Game game, Battle b, List<Division> targets, double damage, Data.Defines def)
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

/// <summary>
/// Imperator: Rome style: daily rounds with a d6 roll modified by general martial difference, morale and
/// strength damage, flanking width from terrain. Uses stats: offense, defensive, morale (falls back to HOI4 stats).
/// </summary>
public sealed class ImperatorCombatModel : ICombatModel
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
