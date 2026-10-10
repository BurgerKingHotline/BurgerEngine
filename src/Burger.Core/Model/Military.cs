using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>
/// A battalion / regiment type from <c>common/units/*.txt</c> (<c>unit_types = { ... }</c>).
/// Stats are free-form so any combat model can define the numbers it needs.
/// </summary>
public sealed class UnitType
{
    public string Key = "";
    public string Name = "";
    public string Category = "infantry";
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public double Cost;
    public double Manpower;
    public int BuildDays = 30;
    /// <summary>Equipment one unit needs (production feature). Empty = the equipment types' defaults for its category.</summary>
    public Dictionary<string, double> Equipment = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");
}

/// <summary>How a stat is combined when unit types are put together in a division.</summary>
public enum StatAggregation { Sum, Average, Min, Max }

/// <summary>A division template (HOI4) / army composition (EU4 regiment) built from unit types.</summary>
public sealed class DivisionTemplate
{
    public string Key = "";
    public string Name = "";
    public List<(UnitType Type, int Count)> Units = new();
    /// <summary>Aggregated stats of the whole division.</summary>
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public double Cost;
    public double Manpower;
    public int BuildDays;
    /// <summary>Optional: only this country (tag) may build it. Empty = everyone.</summary>
    public string OwnerTag = "";
    /// <summary>Equipment the whole division needs (production feature).</summary>
    public Dictionary<string, double> Equipment = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Made in the division designer during the game (stored in the save, not in the game files).</summary>
    public bool Custom;
    /// <summary>Battalions in the division (number of units).</summary>
    public int Battalions => Units.Sum(u => u.Count);

    public double Stat(string key, double def = 0) => Stats.TryGetValue(key, out var v) ? v : def;
}

/// <summary>A division (HOI4) or regiment stack (EU4) on the map.</summary>
public sealed class Division
{
    public int Id;
    public string Name = "";
    public Country Owner = null!;
    public DivisionTemplate Template = null!;
    public Location Location = null!;
    public double Strength;
    public double Org;
    public double Experience;
    public Army? Army;

    // movement
    public List<Location> Path = new();
    public double MoveProgress;
    /// <summary>The location this division is currently attacking (it stays in <see cref="Location"/> while attacking).</summary>
    public Location? AttackTarget;
    public Battle? Battle;
    public bool Retreating;
    /// <summary>True while the player/AI has given a direct move order (front logic leaves the division alone until it arrives).</summary>
    public bool ManualOrder;

    /// <summary>Location assigned by the army's front plan.</summary>
    public Location? FrontPost;

    /// <summary>0-1: how well supplied the division is (supply features). Below 1: attrition, slower organisation, weaker in combat.</summary>
    public double Supply = 1;
    /// <summary>Multiplier on the division's combat power from supply, air superiority, landing from the sea... (set by the engine every hour).</summary>
    public double CombatFactor = 1;
    /// <summary>Set while the division fights for another country as a volunteer (lend_lease feature): its home country.</summary>
    public Country? VolunteerFrom;
    /// <summary>First day the fronts may re-plan this division's path (not saved).</summary>
    public int PathRetryDay;

    public double MaxStrength => Template.Stat("max_strength", 100);
    public double MaxOrg => Template.Stat("max_organisation", 50);
    public double StrengthFraction => MaxStrength <= 0 ? 0 : Strength / MaxStrength;
    public double OrgFraction => MaxOrg <= 0 ? 0 : Org / MaxOrg;
    public bool IsMoving => Path.Count > 0;
    public bool InBattle => Battle != null;
    public Location? NextLocation => Path.Count > 0 ? Path[0] : null;

    public double Stat(string key, double def = 0) => Template.Stat(key, def);

    public override string ToString() => $"{Name} ({Owner.Tag}) @ {Location.Key}";
}

/// <summary>Hold the front, attack along it (towards the objective if one is set), or hold the fallback line.</summary>
public enum ArmyMode { Hold, Offensive, Fallback }

/// <summary>
/// An army led by a general (a <see cref="Character"/>). Armies can be given a front against an enemy country
/// (HOI4 battle plan) or moved directly (EU4/Imperator style).
/// </summary>
public sealed class Army
{
    public int Id;
    public string Name = "";
    public Country Owner = null!;
    public Character? General;
    public List<Division> Divisions = new();
    /// <summary>Enemy country this army holds a front against (null = no front).</summary>
    public Country? FrontEnemy;
    public ArmyMode Mode = ArmyMode.Hold;
    /// <summary>Optional manual target (move the whole army here).</summary>
    public Location? MoveTarget;
    /// <summary>Battle plan: the part of the border the front covers (own or enemy border locations). Empty = the whole border with the enemy.</summary>
    public HashSet<Location> FrontLimit = new();
    /// <summary>Battle plan: offensive arrow. In offensive mode the army attacks towards this location.</summary>
    public Location? Objective;
    /// <summary>Battle plan: fallback line (own locations the army holds in fallback mode).</summary>
    public List<Location> Fallback = new();

    // cached front data
    public List<(Location Own, Location Enemy)> FrontPairs = new();
    public int FrontHash;

    public override string ToString() => $"{Name} ({Owner.Tag})";
}

/// <summary>A battle at a location. Attackers attack into <see cref="Location"/> from neighboring locations.</summary>
public sealed class Battle
{
    public int Id;
    public Location Location = null!;
    public List<Division> Attackers = new();
    public List<Division> Defenders = new();
    public Country AttackerCountry = null!;
    public Country DefenderCountry = null!;
    public GameDate StartDate;
    public int Hours;
    public double AttackerLosses, DefenderLosses;
    /// <summary>Free data storage for custom combat models (e.g. current EU4 phase).</summary>
    public Dictionary<string, double> Data = new();
    /// <summary>0..1 how well the attacker is doing (for UI).</summary>
    public double AttackerProgress = 0.5;
    public bool Finished;
    public bool AttackerWon;

    /// <summary>Number of different locations the attackers are coming from.</summary>
    public int AttackDirections => Attackers.Select(a => a.Location).Distinct().Count();
}
