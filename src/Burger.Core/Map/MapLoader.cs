using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>Thrown when the game cannot be loaded at all (missing province map, etc.).</summary>
public sealed class GameLoadException : Exception
{
    public GameLoadException(string message) : base(message) { }
}

/// <summary>
/// Builds the map from the input images:
/// <list type="bullet">
/// <item><b>provinces.png</b> - one unique color per location (required)</item>
/// <item><b>locations.txt</b> - <c>HEX = location_name</c> for every color</item>
/// <item><b>heightmap.png</b> - greyscale elevation</item>
/// <item><b>terrain.png</b> - terrain type colors from common/terrain.txt</item>
/// <item><b>trees.png</b> - greyscale tree density</item>
/// <item><b>rivers.png</b> - any non-background pixel is a river</item>
/// </list>
/// Images may have different resolutions; everything is resampled to the province map size.
/// </summary>
public static class MapLoader
{
    private sealed class LocationDef
    {
        public Rgb Color;
        public string Key = "";
        public string? Name;
        public string? Terrain;
        public string? Type;
        public int Fort;
        public string Where = "";
    }

    private sealed class BorderAccum
    {
        public int A, B;
        public int Count;
        public double SumX, SumY;
        public bool River;
        public List<int> Points = new();
    }

    public static void Load(GameData data, World world)
    {
        string mapDir = data.PathOf("map");
        string mapFile = Path.Combine(mapDir, "default.map");
        var settings = File.Exists(mapFile)
            ? MapSettings.FromNode(ScriptParser.ParseFile(mapFile, "map/default.map"))
            : new MapSettings();
        if (!File.Exists(mapFile)) Log.Warn("map/default.map not found - using default file names");

        var map = new MapData { Settings = settings };
        world.Map = map;

        // ---------------------------------------------------------------- provinces
        string provPath = Path.Combine(mapDir, settings.Provinces);
        var prov = ImageIO.Load(provPath, false, "map/" + settings.Provinces)
            ?? throw new GameLoadException($"Province map not found or unreadable: map/{settings.Provinces}");
        int w = prov.Width, h = prov.Height;
        map.Width = w;
        map.Height = h;
        Log.Info($"Province map {w}x{h}");

        // ---------------------------------------------------------------- location definitions
        var defs = ParseLocationFile(Path.Combine(mapDir, settings.Locations), "map/" + settings.Locations);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
        {
            if (world.LocationByColor.ContainsKey(d.Color.Packed))
            {
                Log.Error($"Color {d.Color.ToHex()} is assigned to more than one location ('{d.Key}' ignored)", d.Where);
                continue;
            }
            if (!seenKeys.Add(d.Key))
            {
                Log.Error($"Duplicate location name '{d.Key}' - every location needs a unique name", d.Where);
                continue;
            }
            var loc = new Location { Index = world.Locations.Count, Key = d.Key, MapColor = d.Color, NameOverride = d.Name, Fort = d.Fort };
            world.Locations.Add(loc);
            world.LocationByKey[loc.Key] = loc;
            world.LocationByColor[d.Color.Packed] = loc;
            if (d.Type != null)
            {
                switch (d.Type.ToLowerInvariant())
                {
                    case "sea": case "ocean": loc.ForcedWater = true; break;
                    case "lake": loc.ForcedWater = true; loc.IsLake = true; break;
                    case "impassable": case "wasteland": loc.Impassable = true; break;
                    case "land": break;
                    default: Log.Warn($"Unknown location type '{d.Type}' (use land, sea, lake or impassable)", d.Where); break;
                }
            }
        }

        // ---------------------------------------------------------------- pixel → location
        map.PixelLocation = new int[w * h];
        var unknownColors = new Dictionary<int, int>();
        int lastColor = -1, lastIdx = -1;
        for (int i = 0, p = 0; i < w * h; i++, p += 4)
        {
            int a = prov.Data[p + 3];
            if (a == 0) { map.PixelLocation[i] = -1; continue; }
            int col = (prov.Data[p] << 16) | (prov.Data[p + 1] << 8) | prov.Data[p + 2];
            if (col != lastColor)
            {
                lastColor = col;
                if (world.LocationByColor.TryGetValue(col, out var l)) lastIdx = l.Index;
                else
                {
                    // auto-create so the map is still usable; tell the modder to name it
                    var c = Rgb.FromPacked(col);
                    string key = "loc_" + c.ToHex().ToLowerInvariant();
                    var loc = new Location { Index = world.Locations.Count, Key = key, MapColor = c };
                    world.Locations.Add(loc);
                    world.LocationByKey[key] = loc;
                    world.LocationByColor[col] = loc;
                    unknownColors[col] = 0;
                    lastIdx = loc.Index;
                }
            }
            map.PixelLocation[i] = lastIdx;
        }
        if (unknownColors.Count > 0)
            Log.Warn($"{unknownColors.Count} province colors have no entry in {settings.Locations}. They were named loc_<hex>. " +
                     "Run 'burger generate-locations' (or add lines like  FF0000 = berlin) to name them.", "map/" + settings.Locations);
        if (world.Locations.Count > 65000)
            throw new GameLoadException($"Too many locations ({world.Locations.Count}); the maximum is 65000.");

        // ---------------------------------------------------------------- terrain
        map.PixelTerrain = new byte[w * h];
        var terrainImg = ImageIO.Load(Path.Combine(mapDir, settings.Terrain), false, "map/" + settings.Terrain);
        if (data.Terrains.Count == 0) throw new GameLoadException("common/terrain.txt defines no terrain types.");
        if (terrainImg == null)
        {
            Log.Warn($"map/{settings.Terrain} not found - every location uses '{data.DefaultTerrain.Key}' terrain");
            Array.Fill(map.PixelTerrain, (byte)data.DefaultTerrain.Index);
        }
        else
        {
            var cache = new Dictionary<int, byte>();
            foreach (var t in data.Terrains) cache[t.MapColor.Packed] = (byte)t.Index;
            int tw = terrainImg.Width, th = terrainImg.Height;
            var unknownTerrain = new HashSet<int>();
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(th - 1, (int)((y + 0.5) * th / h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(tw - 1, (int)((x + 0.5) * tw / w));
                    int col = terrainImg.RgbAt(sx, sy);
                    if (!cache.TryGetValue(col, out byte ti))
                    {
                        ti = (byte)NearestTerrain(data, Rgb.FromPacked(col)).Index;
                        cache[col] = ti;
                        unknownTerrain.Add(col);
                    }
                    map.PixelTerrain[y * w + x] = ti;
                }
            }
            if (unknownTerrain.Count > 0)
                Log.Warn($"terrain.png contains {unknownTerrain.Count} colors not defined in common/terrain.txt (nearest terrain used), e.g. {Rgb.FromPacked(unknownTerrain.First()).ToHex()}",
                    "map/" + settings.Terrain);
        }

        // ---------------------------------------------------------------- height, trees, rivers
        var heightImg = ImageIO.Load(Path.Combine(mapDir, settings.Heightmap), true, "map/" + settings.Heightmap);
        if (heightImg != null) map.HeightMap = ImageIO.ResampleGrey(heightImg, w, h);
        else
        {
            Log.Warn($"map/{settings.Heightmap} not found - the map will be flat");
            map.HeightMap = new byte[w * h];
            Array.Fill(map.HeightMap, (byte)Math.Min(255, settings.SeaLevel + 20));
        }

        var treeImg = ImageIO.Load(Path.Combine(mapDir, settings.Trees), true, "map/" + settings.Trees);
        map.TreeMap = treeImg != null ? ImageIO.ResampleGrey(treeImg, w, h) : new byte[w * h];

        map.RiverMap = new byte[w * h];
        var riverImg = ImageIO.Load(Path.Combine(mapDir, settings.Rivers), false, "map/" + settings.Rivers);
        if (riverImg != null)
        {
            int bg = settings.RiverBackground.Packed;
            const int paradoxPink = 0xFF0080;
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(riverImg.Height - 1, (int)((y + 0.5) * riverImg.Height / h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(riverImg.Width - 1, (int)((x + 0.5) * riverImg.Width / w));
                    int pi = (sy * riverImg.Width + sx) * 4;
                    if (riverImg.Data[pi + 3] < 128) continue;
                    int col = riverImg.RgbAt(sx, sy);
                    if (col != bg && col != paradoxPink) map.RiverMap[y * w + x] = 255;
                }
            }
        }

        ComputeLocationGeometry(data, world, defs);
        ComputeAdjacency(data, world);
        LoadAdjacencyFile(data, world, Path.Combine(mapDir, settings.Adjacencies), "map/" + settings.Adjacencies);

        foreach (var loc in world.Locations)
            loc.IsCoastal = loc.IsLand && loc.Neighbors.Any(n => n.To.IsWater && !n.To.IsLake);

        Log.Info($"Map loaded: {world.Locations.Count} locations, {world.Locations.Sum(l => l.Neighbors.Count) / 2} borders");
    }

    private static TerrainType NearestTerrain(GameData data, Rgb c)
    {
        TerrainType best = data.Terrains[0];
        int bestD = int.MaxValue;
        foreach (var t in data.Terrains)
        {
            int dr = t.MapColor.R - c.R, dg = t.MapColor.G - c.G, db = t.MapColor.B - c.B;
            int d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static List<LocationDef> ParseLocationFile(string path, string display)
    {
        var list = new List<LocationDef>();
        if (!File.Exists(path))
        {
            Log.Warn($"{display} not found - all locations will get automatic names");
            return list;
        }
        var root = ScriptParser.ParseFile(path, display);
        foreach (var n in root.All)
        {
            if (n.Op == Op.None) { Log.Error($"Expected  HEX = name  but found '{n.Key}'", n.Location); continue; }
            string? hex = null;
            string? key = null;
            Node? block = null;
            if (n.Children != null)
            {
                // FF0000 = { name = berlin terrain = hills }    or    berlin = { color = FF0000 }
                if (Rgb.TryParseHex(n.Key, out _)) { hex = n.Key; key = n.GetString("id") ?? n.GetString("name"); }
                else { key = n.Key; hex = n.GetString("color"); if (hex == null && Rgb.TryParse(n.Get("color"), out var cc)) hex = cc.ToHex(); }
                block = n;
            }
            else if (n.Value != null)
            {
                bool leftHex = Rgb.TryParseHex(n.Key, out _);
                bool rightHex = Rgb.TryParseHex(n.Value, out _);
                if (leftHex) { hex = n.Key; key = n.Value; }
                else if (rightHex) { hex = n.Value; key = n.Key; }
            }
            if (hex == null || !Rgb.TryParseHex(hex, out var color))
            {
                Log.Error($"Could not read a hex color in '{n.Key}' (expected something like  4A7F1E = berlin)", n.Location);
                continue;
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "loc_" + color.ToHex().ToLowerInvariant();
            }
            var d = new LocationDef { Color = color, Where = n.Location };
            // keys must be single words; turn "New York" into new_york and keep the nice name
            if (key.Any(char.IsWhiteSpace))
            {
                d.Name = key;
                key = string.Join("_", key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            }
            d.Key = key;
            if (block != null)
            {
                d.Name = block.GetString("display_name", d.Name);
                d.Terrain = block.GetString("terrain");
                d.Type = block.GetString("type");
                d.Fort = block.GetInt("fort", 0);
            }
            list.Add(d);
        }
        return list;
    }

    private static void ComputeLocationGeometry(GameData data, World world, List<LocationDef> defs)
    {
        var map = world.Map;
        int w = map.Width, h = map.Height, n = world.Locations.Count;
        var sumX = new double[n];
        var sumY = new double[n];
        var terrainHist = new int[n * 256];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int li = map.PixelLocation[i];
                if (li < 0) continue;
                var loc = world.Locations[li];
                loc.PixelCount++;
                sumX[li] += x + 0.5;
                sumY[li] += y + 0.5;
                if (x < loc.MinX) loc.MinX = x;
                if (x > loc.MaxX) loc.MaxX = x;
                if (y < loc.MinY) loc.MinY = y;
                if (y > loc.MaxY) loc.MaxY = y;
                terrainHist[li * 256 + map.PixelTerrain[i]]++;
            }
        }

        var defByKey = defs.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var loc in world.Locations)
        {
            int li = loc.Index;
            if (loc.PixelCount == 0)
            {
                Log.Warn($"Location '{loc.Key}' ({loc.MapColor.ToHex()}) does not appear on the province map");
                loc.MinX = loc.MinY = loc.MaxX = loc.MaxY = 0;
                loc.Terrain = data.DefaultTerrain;
                continue;
            }
            loc.CenterX = (float)(sumX[li] / loc.PixelCount);
            loc.CenterY = (float)(sumY[li] / loc.PixelCount);
            // make sure the label/unit position lies inside the location (centroid can be outside for C-shapes)
            int cx = Math.Clamp((int)loc.CenterX, 0, w - 1), cy = Math.Clamp((int)loc.CenterY, 0, h - 1);
            if (map.PixelLocation[cy * w + cx] != li)
            {
                double best = double.MaxValue;
                int bx = cx, by = cy;
                int step = Math.Max(1, (int)Math.Sqrt((loc.MaxX - loc.MinX + 1) * (loc.MaxY - loc.MinY + 1) / 40000.0));
                for (int y = loc.MinY; y <= loc.MaxY; y += step)
                    for (int x = loc.MinX; x <= loc.MaxX; x += step)
                    {
                        if (map.PixelLocation[y * w + x] != li) continue;
                        double d = (x + 0.5 - loc.CenterX) * (x + 0.5 - loc.CenterX) + (y + 0.5 - loc.CenterY) * (y + 0.5 - loc.CenterY);
                        if (d < best) { best = d; bx = x; by = y; }
                    }
                loc.CenterX = bx + 0.5f;
                loc.CenterY = by + 0.5f;
            }

            // terrain: explicit override or most common terrain pixel
            TerrainType? terrain = null;
            if (defByKey.TryGetValue(loc.Key, out var def) && def.Terrain != null)
            {
                if (!data.TerrainByKey.TryGetValue(def.Terrain, out terrain))
                    Log.Error($"Unknown terrain '{def.Terrain}' for location '{loc.Key}'", def.Where);
            }
            if (terrain == null)
            {
                int bestT = 0, bestC = -1;
                for (int t = 0; t < data.Terrains.Count && t < 256; t++)
                {
                    int c = terrainHist[li * 256 + t];
                    if (c > bestC) { bestC = c; bestT = t; }
                }
                terrain = data.Terrains[bestT];
                // a location that is marked as land but whose pixels are water-terrain should still be land
                if (terrain.IsWater && !loc.ForcedWater && def != null && string.Equals(def.Type, "land", StringComparison.OrdinalIgnoreCase))
                    terrain = data.DefaultTerrain;
            }
            if (loc.ForcedWater && !terrain.IsWater && data.WaterTerrain != null) terrain = data.WaterTerrain;
            loc.Terrain = terrain;
        }
    }

    private static void ComputeAdjacency(GameData data, World world)
    {
        var map = world.Map;
        int w = map.Width, h = map.Height;
        var acc = new Dictionary<long, BorderAccum>();

        void Add(int a, int b, int x2, int y2, bool river)
        {
            if (a < 0 || b < 0 || a == b) return;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            long key = ((long)lo << 32) | (uint)hi;
            if (!acc.TryGetValue(key, out var ba))
            {
                ba = new BorderAccum { A = lo, B = hi };
                acc[key] = ba;
            }
            ba.Count++;
            ba.SumX += x2 / 2.0;
            ba.SumY += y2 / 2.0;
            if (river) ba.River = true;
            ba.Points.Add((int)(((uint)x2 << 16) | ((uint)y2 & 0xFFFF)));
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int a = map.PixelLocation[i];
                if (x + 1 < w)
                {
                    int b = map.PixelLocation[i + 1];
                    if (a != b) Add(a, b, (x + 1) * 2, y * 2 + 1, map.RiverMap[i] != 0 || map.RiverMap[i + 1] != 0);
                }
                if (y + 1 < h)
                {
                    int b = map.PixelLocation[i + w];
                    if (a != b) Add(a, b, x * 2 + 1, (y + 1) * 2, map.RiverMap[i] != 0 || map.RiverMap[i + w] != 0);
                }
            }
        }

        double kmPerPixel = data.Defines.KmPerPixel;
        foreach (var ba in acc.Values)
        {
            var la = world.Locations[ba.A];
            var lb = world.Locations[ba.B];
            var kind = la.IsWater || lb.IsWater ? AdjacencyKind.Sea : AdjacencyKind.Land;
            float midX = (float)(ba.SumX / ba.Count), midY = (float)(ba.SumY / ba.Count);
            var points = OrderBorderPoints(ba.Points, la, lb);
            double dist = Math.Sqrt((la.CenterX - lb.CenterX) * (la.CenterX - lb.CenterX) + (la.CenterY - lb.CenterY) * (la.CenterY - lb.CenterY)) * kmPerPixel;
            dist = Math.Max(dist, 1);
            la.Neighbors.Add(new Adjacency { From = la, To = lb, Kind = kind, River = ba.River, BorderPixels = ba.Count, MidX = midX, MidY = midY, BorderPoints = points, DistanceKm = dist });
            lb.Neighbors.Add(new Adjacency { From = lb, To = la, Kind = kind, River = ba.River, BorderPixels = ba.Count, MidX = midX, MidY = midY, BorderPoints = points, DistanceKm = dist });
        }
    }

    /// <summary>Orders border points along the border and keeps at most 12 evenly spaced ones.</summary>
    private static List<(float X, float Y)> OrderBorderPoints(List<int> packed, Location a, Location b)
    {
        var pts = new List<(float X, float Y)>(packed.Count);
        foreach (int p in packed) pts.Add(((int)((uint)p >> 16) / 2f, (p & 0xFFFF) / 2f));
        // project on the direction perpendicular to the line between the two centers
        float dx = b.CenterX - a.CenterX, dy = b.CenterY - a.CenterY;
        float px = -dy, py = dx;
        float len = MathF.Sqrt(px * px + py * py);
        if (len < 1e-3f) { px = 1; py = 0; } else { px /= len; py /= len; }
        pts.Sort((p1, p2) => (p1.X * px + p1.Y * py).CompareTo(p2.X * px + p2.Y * py));
        const int max = 12;
        if (pts.Count <= max) return pts;
        var result = new List<(float X, float Y)>(max);
        for (int i = 0; i < max; i++) result.Add(pts[(int)((i + 0.5) * pts.Count / max)]);
        return result;
    }

    private static void LoadAdjacencyFile(GameData data, World world, string path, string display)
    {
        if (!File.Exists(path)) return;
        var root = ScriptParser.ParseFile(path, display);
        foreach (var n in root.GetAll("adjacency"))
        {
            var from = world.GetLocation(n.GetString("from"));
            var to = world.GetLocation(n.GetString("to"));
            if (from == null || to == null)
            {
                Log.Error($"adjacency: unknown location '{(from == null ? n.GetString("from") : n.GetString("to"))}'", n.Location);
                continue;
            }
            string type = (n.GetString("type") ?? "strait").ToLowerInvariant();
            if (type == "impassable")
            {
                from.Neighbors.RemoveAll(a => a.To == to);
                to.Neighbors.RemoveAll(a => a.To == from);
                continue;
            }
            if (from.AdjacencyTo(to) != null) continue;
            double dist = Math.Sqrt((from.CenterX - to.CenterX) * (from.CenterX - to.CenterX) + (from.CenterY - to.CenterY) * (from.CenterY - to.CenterY)) * data.Defines.KmPerPixel;
            var pts = new List<(float X, float Y)> { (from.CenterX, from.CenterY), (to.CenterX, to.CenterY) };
            float mx = (from.CenterX + to.CenterX) / 2, my = (from.CenterY + to.CenterY) / 2;
            bool river = n.GetBool("river", false);
            from.Neighbors.Add(new Adjacency { From = from, To = to, Kind = AdjacencyKind.Strait, MidX = mx, MidY = my, DistanceKm = dist, BorderPoints = pts, River = river });
            to.Neighbors.Add(new Adjacency { From = to, To = from, Kind = AdjacencyKind.Strait, MidX = mx, MidY = my, DistanceKm = dist, BorderPoints = pts, River = river });
        }
    }
}
