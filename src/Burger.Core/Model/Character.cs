using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>A character trait from <c>common/traits.txt</c>.</summary>
public sealed class Trait
{
    public string Key = "";
    public string Name = "";
    public string Description = "";
    public string Category = "personality";
    /// <summary>Stat changes (martial = 1) and other modifiers (general_attack = 0.1, health = -10 ...).</summary>
    public Dictionary<string, double> Modifiers = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Opposites = new();
    /// <summary>Relative chance to be given to randomly generated characters (0 = never).</summary>
    public double RandomWeight;
    /// <summary>Chance (0-1) a child inherits this trait from a parent.</summary>
    public double InheritChance;
    public Node Definition = Node.Root("");

    public double Mod(string key) => Modifiers.TryGetValue(key, out var v) ? v : 0;
}

/// <summary>
/// An Imperator-style character: rulers, heirs, generals, family members. Defined in
/// <c>history/characters/*.txt</c> or created at runtime with the <c>create_character</c> effect.
/// </summary>
public sealed class Character : IScope
{
    public int Id;
    /// <summary>Script id, e.g. <c>caesar</c>. Referenced as <c>character:caesar</c>.</summary>
    public string Key = "";
    public string FirstName = "";
    public string Family = "";
    public bool Female;
    public GameDate BirthDate;
    public GameDate? DeathDate;
    public string DeathReason = "";
    public Country? Country;
    public List<Trait> Traits = new();
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public double Loyalty = 50;
    public double Health = 100;
    public Character? Father, Mother, Spouse;
    public List<Character> Children = new();
    public string? Portrait;
    public bool IsGeneral;
    public VarStore Vars { get; } = new();

    public bool Alive => DeathDate == null;
    public bool IsRuler => Country != null && Country.Ruler == this;
    public bool IsHeir => Country != null && Country.Heir == this;

    public ScopeType ScopeType => ScopeType.Character;
    public string ScopeKey => Key;
    public string DisplayName => string.IsNullOrEmpty(Family) ? Localisation.Get(FirstName) : $"{Localisation.Get(FirstName)} {Localisation.Get(Family)}";

    public int Age(GameDate now) => GameDate.YearsBetween(BirthDate, DeathDate ?? now);

    /// <summary>Base stat plus all trait modifiers for that stat.</summary>
    public double GetStat(string stat)
    {
        double v = Stats.TryGetValue(stat, out var b) ? b : 0;
        foreach (var t in Traits) v += t.Mod(stat);
        return v;
    }

    /// <summary>Sum of a modifier across all traits (e.g. general_attack).</summary>
    public double TraitModifier(string key)
    {
        double v = 0;
        foreach (var t in Traits) v += t.Mod(key);
        return v;
    }

    public bool HasTrait(string key) => Traits.Any(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Key} ({DisplayName})";
}
