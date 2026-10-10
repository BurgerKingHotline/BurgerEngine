using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>
/// Equipment from <c>common/equipment/*.txt</c> (production feature): rifles, artillery pieces, tanks, ships' guns...
/// Factories produce it into a country's stockpile; training and reinforcing divisions use it.
/// </summary>
public sealed class EquipmentType
{
    public string Key = "";
    public string Name = "";
    /// <summary>Production cost of one unit (factory output per day is compared against it).</summary>
    public double Cost = 1;
    public string Category = "army";
    /// <summary>Unit categories (infantry, support...) that use this equipment when a unit type lists none.</summary>
    public List<string> DefaultFor = new();
    /// <summary>How many a unit of those categories needs by default.</summary>
    public double DefaultAmount = 100;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");
}

/// <summary>
/// A state building from <c>common/buildings/*.txt</c>: factories, dockyards, infrastructure, railways, supply hubs,
/// air bases... Levels are stored per state (<see cref="State.Buildings"/>).
/// </summary>
public sealed class BuildingType
{
    public string Key = "";
    public string Name = "";
    public string Description = "";
    /// <summary>Construction cost of one level (civilian factories add construction points per day).</summary>
    public double Cost = 5000;
    public int MaxLevel = 10;
    /// <summary>factory, infrastructure, military... (free text, used by the interface to group buildings).</summary>
    public string Category = "other";
    /// <summary>True if levels are shared with other buildings of the same category in a state (HOI4 building slots).</summary>
    public bool SharesSlots;
    public Dictionary<string, double> Stats = new(StringComparer.OrdinalIgnoreCase);
    public Node? Potential;
    public Node Definition = Node.Root("");
}

/// <summary>Factories assigned to producing one kind of equipment.</summary>
public sealed class ProductionLine
{
    public EquipmentType Equipment = null!;
    /// <summary>Military factories (or dockyards for naval equipment) the player assigned.</summary>
    public int Factories = 1;
    /// <summary>0-1: grows while the line keeps producing the same thing.</summary>
    public double Efficiency = 0.1;
    /// <summary>Production points carried over to the next unit.</summary>
    public double Progress;
    /// <summary>Units produced yesterday (for the interface).</summary>
    public double LastOutput;
}

/// <summary>A building being constructed by civilian factories.</summary>
public sealed class ConstructionProject
{
    public BuildingType Building = null!;
    public State State = null!;
    public double Progress;
    /// <summary>Civilian factories working on it yesterday (for the interface).</summary>
    public int Factories;
}
