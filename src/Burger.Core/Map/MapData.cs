using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>Settings from <c>map/default.map</c>.</summary>
public sealed class MapSettings
{
    public string Provinces = "provinces.png";
    public string Heightmap = "heightmap.png";
    public string Terrain = "terrain.png";
    public string Trees = "trees.png";
    public string Rivers = "rivers.png";
    public string Locations = "locations.txt";
    public string Adjacencies = "adjacencies.txt";
    public string StatesFolder = "states";
    /// <summary>Heightmap value (0-255) of the water surface.</summary>
    public int SeaLevel = 20;
    /// <summary>World-space height of heightmap value 255.</summary>
    public float HeightScale = 6f;
    /// <summary>Map pixels per world unit (bigger = smaller world).</summary>
    public float PixelsPerUnit = 10f;
    public float TreeDensity = 1f;
    public int TreeSpacing = 4;
    public Rgb RiverBackground = new(255, 255, 255);
    public Rgb WaterColor = new(28, 64, 110);
    public Rgb RiverColor = new(40, 90, 150);
    public Node Raw = Node.Root("default.map");

    public static MapSettings FromNode(Node n)
    {
        var s = new MapSettings { Raw = n };
        s.Provinces = n.GetString("provinces", s.Provinces)!;
        s.Heightmap = n.GetString("heightmap", s.Heightmap)!;
        s.Terrain = n.GetString("terrain", s.Terrain)!;
        s.Trees = n.GetString("trees", s.Trees)!;
        s.Rivers = n.GetString("rivers", s.Rivers)!;
        s.Locations = n.GetString("locations", s.Locations)!;
        s.Adjacencies = n.GetString("adjacencies", s.Adjacencies)!;
        s.StatesFolder = n.GetString("states", s.StatesFolder)!;
        s.SeaLevel = Math.Clamp(n.GetInt("sea_level", s.SeaLevel), 0, 254);
        s.HeightScale = (float)n.GetDouble("height_scale", s.HeightScale);
        s.PixelsPerUnit = Math.Max(0.1f, (float)n.GetDouble("pixels_per_unit", s.PixelsPerUnit));
        s.TreeDensity = (float)n.GetDouble("tree_density", s.TreeDensity);
        s.TreeSpacing = Math.Max(1, n.GetInt("tree_spacing", s.TreeSpacing));
        if (Rgb.TryParse(n.Get("river_background"), out var rb)) s.RiverBackground = rb;
        if (Rgb.TryParse(n.Get("water_color"), out var wc)) s.WaterColor = wc;
        if (Rgb.TryParse(n.Get("river_color"), out var rc)) s.RiverColor = rc;
        return s;
    }
}

/// <summary>Per-pixel map data at province-map resolution. All arrays are row-major, <c>index = y * Width + x</c>.</summary>
public sealed class MapData
{
    public int Width;
    public int Height;
    public MapSettings Settings = new();
    /// <summary>Location index for every pixel (-1 = no location).</summary>
    public int[] PixelLocation = Array.Empty<int>();
    /// <summary>Terrain index for every pixel (from terrain.png).</summary>
    public byte[] PixelTerrain = Array.Empty<byte>();
    /// <summary>Height (0-255) resampled to map resolution.</summary>
    public byte[] HeightMap = Array.Empty<byte>();
    /// <summary>Tree density (0-255) resampled to map resolution.</summary>
    public byte[] TreeMap = Array.Empty<byte>();
    /// <summary>255 where a river is drawn, else 0.</summary>
    public byte[] RiverMap = Array.Empty<byte>();

    public float WorldWidth => Width / Settings.PixelsPerUnit;
    public float WorldHeight => Height / Settings.PixelsPerUnit;
}
