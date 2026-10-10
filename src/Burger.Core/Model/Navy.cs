using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>
/// A ship class from <c>common/ships/*.txt</c> (<c>ship_types = { destroyer = { ... } }</c>, navy feature).
/// Categories: screen (protects capital ships), capital, transport (carries armies over sea), or your own.
/// Stats are free-form: light_attack, heavy_attack, armor, hp, speed, transport (divisions it can carry)...
/// </summary>
public sealed class ShipType
{
    public string Key = "";
    public string Name = "";
    public string Category = "screen";
    public double Hp = 20;
    /// <summary>Speed in km/h.</summary>
    public double Speed = 25;
    /// <summary>Gold cost when ships are bought (no production feature).</summary>
    public double Cost = 50;
    /// <summary>Production cost when dockyards build it (production feature).</summary>
    public double ProductionCost = 500;
    public double Manpower = 300;
    public int BuildDays = 180;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");

    public double Stat(string key, double def = 0) => Stats.TryGetValue(key, out var v) ? v : def;
}

public sealed class Ship
{
    public int Id;
    public string Name = "";
    public ShipType Type = null!;
    public double Hp;
    public double HpFraction => Type.Hp <= 0 ? 0 : Hp / Type.Hp;
}

public enum FleetMission { Hold, Patrol }

/// <summary>A group of ships that moves through sea locations (and ports: own coastal land locations).</summary>
public sealed class Fleet
{
    public int Id;
    public string Name = "";
    public Country Owner = null!;
    public Location Location = null!;
    public Location? HomePort;
    public List<Ship> Ships = new();
    public List<Location> Path = new();
    public double MoveProgress;
    public bool Retreating;
    public FleetMission Mission;
    public NavalBattle? Battle;

    public double Speed => Ships.Count == 0 ? 20 : Ships.Min(s => s.Type.Speed);
    public double HpFraction => Ships.Count == 0 ? 0 : Ships.Sum(s => s.Hp) / Math.Max(1, Ships.Sum(s => s.Type.Hp));
    public bool InPort => Location.IsLand;
    public bool IsMoving => Path.Count > 0;
    public override string ToString() => $"{Name} ({Owner.Tag})";
}

/// <summary>A sea battle in one sea location between the fleets of two sides at war.</summary>
public sealed class NavalBattle
{
    public int Id;
    public Location Location = null!;
    public List<Fleet> SideA = new();
    public List<Fleet> SideB = new();
    public Country CountryA = null!;
    public Country CountryB = null!;
    public int Hours;
    public int SunkA, SunkB;
    public bool Finished;
}

/// <summary>A ship being built with gold (navy feature without production).</summary>
public sealed class ShipOrder
{
    public ShipType Type = null!;
    public Location Port = null!;
    public int HoursLeft;
    public int TotalHours;
}
