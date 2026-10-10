using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>
/// A plane type from <c>common/planes/*.txt</c> (<c>plane_types = { fighter = { ... } }</c>, air feature).
/// Stats are free-form; the engine uses air_attack, air_defense, agility (air superiority), ground_attack (close air
/// support) and strategic_attack (bombing). <c>range</c> is in km: a wing only flies over regions within range of its base.
/// </summary>
public sealed class PlaneType
{
    public string Key = "";
    public string Name = "";
    public string Category = "fighter";
    /// <summary>Km a wing can fly from its air base.</summary>
    public double Range = 600;
    /// <summary>Gold cost of a full wing (bought without the production feature).</summary>
    public double Cost = 60;
    /// <summary>Production cost of one plane (production feature: military factories build planes).</summary>
    public double ProductionCost = 20;
    /// <summary>Manpower of a full wing.</summary>
    public double Manpower = 200;
    public int BuildDays = 90;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");

    public double Stat(string key, double def = 0) => Stats.TryGetValue(key, out var v) ? v : def;
}

/// <summary>What an air wing does in its region.</summary>
public enum AirMission { None, Superiority, CloseSupport, Bombing }

/// <summary>
/// An air region (HOI4 strategic region): a group of states. Air wings are assigned to regions, and air superiority
/// is fought per region. Defined in <c>map/air_regions.txt</c>; states not listed there are grouped automatically.
/// </summary>
public sealed class AirRegion
{
    public int Index;
    public string Key = "";
    public string? NameOverride;
    public List<State> States = new();
    /// <summary>Center in map pixels (average of its states' locations).</summary>
    public float CenterX, CenterY;
    public string DisplayName => NameOverride != null ? Localisation.Get(NameOverride) : Localisation.GetOrPretty(Key);
    public override string ToString() => Key;
}

/// <summary>A group of planes of one type at an air base (a state with an air base).</summary>
public sealed class AirWing
{
    public int Id;
    public string Name = "";
    public Country Owner = null!;
    public PlaneType Type = null!;
    public double Planes;
    public State Base = null!;
    public AirRegion? Region;
    public AirMission Mission;
    /// <summary>Region the wing was in range of and flew over today (null = idle).</summary>
    public bool Active;
    public override string ToString() => $"{Name} ({Owner.Tag})";
}

/// <summary>A wing being built with gold (air feature without production).</summary>
public sealed class WingOrder
{
    public PlaneType Type = null!;
    public State Base = null!;
    public int HoursLeft;
    public int TotalHours;
}
