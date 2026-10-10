using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>A terrain type from <c>common/terrain.txt</c>, matched by color against <c>terrain.png</c>.</summary>
public sealed class TerrainType
{
    public string Key = "";
    public int Index;
    public string Name = "";
    /// <summary>Color of this terrain in terrain.png.</summary>
    public Rgb MapColor;
    /// <summary>Tint used in the terrain map mode and as fallback when no texture exists.</summary>
    public Rgb DisplayColor;
    /// <summary>Texture tiled over the map (relative to game folder).</summary>
    public string? Texture;
    public double MovementCost = 1.0;
    public int CombatWidth = 80;
    public double AttackModifier;
    public double DefenseModifier;
    public bool IsWater;
    public bool Impassable;
    /// <summary>Any other key/number pairs, readable by custom combat models and scripts.</summary>
    public Dictionary<string, double> Modifiers = new(StringComparer.OrdinalIgnoreCase);
    public Node Definition = Node.Root("");

    public double Mod(string key, double def = 0) => Modifiers.TryGetValue(key, out var v) ? v : def;
}

public enum AdjacencyKind { Land, Sea, Strait }

/// <summary>A connection between two locations (computed from the province map, plus straits from adjacencies.txt).</summary>
public sealed class Adjacency
{
    public Location From = null!;
    public Location To = null!;
    public AdjacencyKind Kind;
    /// <summary>True if a river runs along this border (attack penalty when crossing).</summary>
    public bool River;
    public int BorderPixels;
    /// <summary>Midpoint of the shared border, in map pixels.</summary>
    public float MidX, MidY;
    /// <summary>A few ordered points along the shared border (map pixels), used to draw frontlines.</summary>
    public List<(float X, float Y)> BorderPoints = new();
    /// <summary>Distance between location centers in km.</summary>
    public double DistanceKm;
}

/// <summary>
/// The smallest map unit: one color of <c>provinces.png</c>. Named in <c>map/locations.txt</c>.
/// Locations are grouped into <see cref="State"/>s, which are owned and cored by countries.
/// </summary>
public sealed class Location : IScope
{
    public int Index;
    public string Key = "";
    public Rgb MapColor;
    public string? NameOverride;
    public TerrainType Terrain = null!;
    public bool ForcedWater;
    public bool IsLake;
    public bool Impassable;
    public State? State;

    // geometry (map pixels)
    public int PixelCount;
    public float CenterX, CenterY;
    public int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1;
    public List<Adjacency> Neighbors = new();
    public bool IsCoastal;

    // runtime
    /// <summary>Country that militarily controls the location (differs from the owner when occupied).</summary>
    public Country? Controller;
    public int Fort;
    public double SiegeProgress;
    public Country? Sieger;
    public VarStore Vars { get; } = new();

    public bool IsWater => ForcedWater || Terrain.IsWater;
    public bool IsLand => !IsWater;
    public bool IsPassable => !Impassable && !Terrain.Impassable && !IsWater;
    public Country? Owner => State?.Owner;
    public bool IsOccupied => Controller != null && Owner != null && Controller != Owner;

    public ScopeType ScopeType => ScopeType.Location;
    public string ScopeKey => Key;
    public string DisplayName => NameOverride != null ? Localisation.Get(NameOverride) : Localisation.GetOrPretty(Key);

    public Adjacency? AdjacencyTo(Location other)
    {
        foreach (var a in Neighbors)
            if (a.To == other) return a;
        return null;
    }

    public override string ToString() => Key;
}

/// <summary>A group of locations owned by one country. Defined in <c>map/states/*.txt</c>.</summary>
public sealed class State : IScope
{
    public int Index;
    public string Key = "";
    public string? NameOverride;
    public List<Location> Locations = new();
    public Country? Owner;
    public HashSet<Country> Cores = new();
    public HashSet<Country> Claims = new();
    /// <summary>Manpower added to the owner's pool each month.</summary>
    public double Manpower;
    /// <summary>Gold added to the owner's treasury each month.</summary>
    public double Tax;
    public string Category = "";
    public Dictionary<string, int> Buildings = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Building levels out of action (strategic bombing); repaired over time.</summary>
    public Dictionary<string, double> Damaged = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Usable levels of a building (built minus damaged).</summary>
    public double Working(string building) => Math.Max(0, Buildings.GetValueOrDefault(building) - Damaged.GetValueOrDefault(building));
    /// <summary>File this state was loaded from (the map editor writes it back there).</summary>
    public string SourceFile = "";
    public VarStore Vars { get; } = new();

    public ScopeType ScopeType => ScopeType.State;
    public string ScopeKey => Key;
    public string DisplayName => NameOverride != null ? Localisation.Get(NameOverride) : Localisation.GetOrPretty(Key);

    /// <summary>Share (0-1) of this state's locations controlled by the given country.</summary>
    public double ControlledShare(Country c)
    {
        if (Locations.Count == 0) return 0;
        int n = 0;
        foreach (var l in Locations) if (l.Controller == c) n++;
        return n / (double)Locations.Count;
    }

    public override string ToString() => Key;
}
