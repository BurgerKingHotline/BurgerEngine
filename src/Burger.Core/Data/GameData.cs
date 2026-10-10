using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Data;

/// <summary>Information from <c>game.txt</c> at the root of the game folder.</summary>
public sealed class GameInfo
{
    public string Name = "Untitled Game";
    public string Version = "1.0";
    public string Author = "";
    public string Description = "";
    /// <summary>Tag selected by default on the country selection screen.</summary>
    public string DefaultTag = "";
    public string Icon = "";
    public Node Raw = Node.Root("game.txt");
}

/// <summary>Static definitions loaded from the game folder (everything that does not change while playing).</summary>
public sealed class GameData
{
    public string Root = "";
    public GameInfo Info = new();
    public Defines Defines = new();

    public readonly List<TerrainType> Terrains = new();
    public readonly Dictionary<string, TerrainType> TerrainByKey = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, UnitType> UnitTypes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, DivisionTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Equipment from common/equipment (production feature).</summary>
    public readonly Dictionary<string, EquipmentType> Equipment = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>State buildings from common/buildings (factories, infrastructure, railways, supply hubs...).</summary>
    public readonly Dictionary<string, BuildingType> BuildingTypes = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Ship classes from common/ships (navy feature).</summary>
    public readonly Dictionary<string, ShipType> ShipTypes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, PlaneType> PlaneTypes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, StatAggregation> StatAggregation = new(StringComparer.OrdinalIgnoreCase);

    public readonly Dictionary<string, Trait> Traits = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, EventDef> Events = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, ActionDef> DiplomaticActions = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, ActionDef> CharacterInteractions = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, CasusBelli> CasusBelli = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, SubjectType> SubjectTypes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, OnAction> OnActions = new(StringComparer.OrdinalIgnoreCase);
    public NameList DefaultNames = new();

    /// <summary>Absolute path of a file inside the game folder.</summary>
    public string PathOf(string relative) => Path.GetFullPath(Path.Combine(Root, relative.Replace('\\', '/').TrimStart('/')));

    public TerrainType DefaultTerrain => Terrains.FirstOrDefault(t => !t.IsWater) ?? Terrains[0];
    public TerrainType? WaterTerrain => Terrains.FirstOrDefault(t => t.IsWater);
}
