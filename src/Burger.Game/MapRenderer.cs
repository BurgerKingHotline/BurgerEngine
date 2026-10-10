using System.Numerics;
using Burger.Core;
using Burger.Core.Map;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public enum MapMode { Political, Terrain, States, Diplomacy, Locations, Supply, Air, Ideology, Population, Culture, Religion }

/// <summary>
/// Builds and draws the 3D map from the province map, heightmap, terrain, tree and river inputs.
/// Province borders, country borders, map mode colors and occupation stripes are computed in a shader
/// from two textures: a per-pixel "map data" texture (location index, terrain, river) and a small
/// per-location lookup texture that the CPU updates when ownership changes.
/// </summary>
public sealed unsafe class MapRenderer : IDisposable
{
    private const int LutWidth = 256;
    private const int TileSize = 512;

    private readonly Game _game;
    private readonly MapData _map;
    private Mesh _terrainMesh;
    private Mesh _seaMesh;
    private Mesh _treeMesh;
    private bool _hasTrees;
    private Material _treeMaterial;
    private Material _material;
    private Shader _shader;
    private Texture2D _mapDataTex;
    private Texture2D _lutTex;
    private Texture2D _atlasTex;
    private Color[] _lut = Array.Empty<Color>();
    private int _blockRows;
    private int _atlasGrid;

    // CPU copy of the mesh heights for picking and placing things on the ground
    private float[] _heights = Array.Empty<float>();
    private int _hw, _hh;

    private int _locMapData, _locLut, _locLutSize, _locBlockRows, _locMapSize, _locWorldSize, _locAtlasGrid, _locTileScale,
        _locWater, _locRiver, _locTime, _locBorderScale, _locStateBorders, _locShowBorders, _locTint, _locViewPos, _locFog;
    private int _visibilityVersion = -1;

    public float WorldWidth { get; }
    public float WorldHeight { get; }
    public MapMode Mode { get; set; } = MapMode.Political;
    /// <summary>A data/C# defined map mode (overrides <see cref="Mode"/> when set).</summary>
    public Burger.Core.Mechanics.MapModeDef? CustomMode { get; set; }
    public Location? Selected { get; set; }
    public Location? Hovered { get; set; }
    public State? HighlightState { get; set; }
    /// <summary>Country whose relations the diplomacy map mode shows (defaults to the player).</summary>
    public Country? DiplomacyFocus { get; set; }
    public bool Dirty { get; set; } = true;

    public MapRenderer(Game game)
    {
        _game = game;
        _map = game.World.Map;
        WorldWidth = _map.WorldWidth;
        WorldHeight = _map.WorldHeight;
        BuildTerrainMesh();
        BuildMapDataTexture();
        BuildLut();
        BuildAtlas();
        BuildShader();
        BuildTrees();
        _seaMesh = Raylib.GenMeshPlane(WorldWidth * 7, WorldHeight * 7, 8, 8);
        game.MapChanged += () => Dirty = true;
    }

    // ===============================================================================================
    // Construction
    // ===============================================================================================

    private void BuildTerrainMesh()
    {
        int maxDim = Math.Max(_map.Width, _map.Height) > 4096 ? 1024 : 512; // big maps (Earth) get a finer terrain mesh
        float aspect = _map.Width / (float)_map.Height;
        _hw = aspect >= 1 ? Math.Min(maxDim, _map.Width) : Math.Max(2, (int)(Math.Min(maxDim, _map.Height) * aspect));
        _hh = aspect >= 1 ? Math.Max(2, (int)(_hw / aspect)) : Math.Min(maxDim, _map.Height);
        _hw = Math.Max(2, _hw);

        var s = _map.Settings;
        int sea = s.SeaLevel;
        Image img = Raylib.GenImageColor(_hw, _hh, Color.Black);
        byte* px = (byte*)img.Data;
        _heights = new float[_hw * _hh];
        for (int y = 0; y < _hh; y++)
        {
            int my = Math.Min(_map.Height - 1, (int)((y + 0.5f) * _map.Height / _hh));
            for (int x = 0; x < _hw; x++)
            {
                int mx = Math.Min(_map.Width - 1, (int)((x + 0.5f) * _map.Width / _hw));
                int i = my * _map.Width + mx;
                int li = _map.PixelLocation[i];
                bool water = li < 0 || _game.World.Locations[li].IsWater;
                int h = _map.HeightMap[i];
                h = water ? sea : Math.Max(h, sea + 2);
                int o = (y * _hw + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = (byte)h;
                px[o + 3] = 255;
                _heights[y * _hw + x] = h / 255f * s.HeightScale;
            }
        }
        // soften the steps between flattened water and land so coasts slope gently
        var smooth = new byte[_hw * _hh];
        for (int y = 0; y < _hh; y++)
            for (int x = 0; x < _hw; x++)
            {
                int sum = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = Math.Clamp(x + dx, 0, _hw - 1), yy = Math.Clamp(y + dy, 0, _hh - 1);
                        sum += px[(yy * _hw + xx) * 4];
                        n++;
                    }
                smooth[y * _hw + x] = (byte)(sum / n);
            }
        for (int i = 0; i < _hw * _hh; i++)
        {
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = smooth[i];
            _heights[i] = smooth[i] / 255f * s.HeightScale;
        }
        _terrainMesh = Raylib.GenMeshHeightmap(img, new Vector3(WorldWidth, s.HeightScale, WorldHeight));
        Raylib.UnloadImage(img);
    }

    private void BuildMapDataTexture()
    {
        int w = _map.Width, h = _map.Height;
        var locs = _game.World.Locations;
        // distance (in pixels, up to CoastRange) from every water pixel to the nearest land: shallow water near coasts
        const int CoastRange = 24;
        var dist = new byte[w * h];
        var queue = new Queue<int>();
        bool IsWaterPx(int i) { int li = _map.PixelLocation[i]; return li < 0 || locs[li].IsWater; }
        for (int i = 0; i < w * h; i++)
            if (!IsWaterPx(i)) { dist[i] = 0; queue.Enqueue(i); }
            else dist[i] = 255;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int d = dist[i];
            if (d >= CoastRange) continue;
            int x = i % w, y = i / w;
            void Visit(int j) { if (dist[j] == 255) { dist[j] = (byte)(d + 1); queue.Enqueue(j); } }
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (y > 0) Visit(i - w);
            if (y < h - 1) Visit(i + w);
        }
        Image img = Raylib.GenImageColor(w, h, Color.Blank);
        byte* px = (byte*)img.Data;
        for (int i = 0; i < w * h; i++)
        {
            int li = _map.PixelLocation[i] + 1; // 0 = none
            int o = i * 4;
            px[o] = (byte)(li & 0xFF);
            px[o + 1] = (byte)((li >> 8) & 0xFF);
            px[o + 2] = _map.PixelTerrain[i];
            // land: river flag. water: closeness to the coast (240 next to land, 0 far out)
            px[o + 3] = IsWaterPx(i) ? (byte)(dist[i] == 255 ? 0 : Math.Max(0, CoastRange - dist[i]) * 10) : _map.RiverMap[i];
        }
        _mapDataTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_mapDataTex, TextureFilter.Point);
        Raylib.SetTextureWrap(_mapDataTex, TextureWrap.Clamp);
    }

    private void BuildLut()
    {
        int n = _game.World.Locations.Count;
        _blockRows = Math.Max(1, (n + LutWidth - 1) / LutWidth);
        _lut = new Color[LutWidth * _blockRows * 3];
        Image img = Raylib.GenImageColor(LutWidth, _blockRows * 3, Color.Blank);
        _lutTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_lutTex, TextureFilter.Point);
    }

    private void BuildAtlas()
    {
        var terrains = _game.Data.Terrains;
        _atlasGrid = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(terrains.Count)));
        int size = _atlasGrid * TileSize;
        Image atlas = Raylib.GenImageColor(size, size, Color.Gray);
        var rnd = new Random(1234);
        for (int t = 0; t < terrains.Count; t++)
        {
            var terr = terrains[t];
            int gx = t % _atlasGrid, gy = t / _atlasGrid;
            var dst = new Rectangle(gx * TileSize, gy * TileSize, TileSize, TileSize);
            Image tile;
            string? path = terr.Texture != null ? _game.Data.PathOf(terr.Texture) : null;
            if (path != null && File.Exists(path))
            {
                tile = Raylib.LoadImage(path);
                Raylib.ImageFormat(ref tile, PixelFormat.UncompressedR8G8B8A8);
            }
            else
            {
                if (terr.Texture != null) Log.Warn($"Terrain texture not found: {terr.Texture}", "common/terrain.txt");
                tile = Assets.NoiseTile(TileSize, terr.DisplayColor, rnd.Next());
            }
            Raylib.ImageDraw(ref atlas, tile, new Rectangle(0, 0, tile.Width, tile.Height), dst, Color.White);
            Raylib.UnloadImage(tile);
        }
        _atlasTex = Raylib.LoadTextureFromImage(atlas);
        Raylib.UnloadImage(atlas);
        Raylib.GenTextureMipmaps(ref _atlasTex);
        Raylib.SetTextureFilter(_atlasTex, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(_atlasTex, TextureWrap.Clamp);
    }

    private void BuildShader()
    {
        _shader = Raylib.LoadShaderFromMemory(Shaders.TerrainVertex, Shaders.TerrainFragment);
        if (_shader.Id == 0) Log.Error("Map shader failed to compile - see console output");
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Albedo, _mapDataTex);
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Metalness, _lutTex);
        Raylib.SetMaterialTexture(ref _material, MaterialMapIndex.Normal, _atlasTex);

        _locMapSize = Raylib.GetShaderLocation(_shader, "mapSize");
        _locWorldSize = Raylib.GetShaderLocation(_shader, "worldSize");
        _locLutSize = Raylib.GetShaderLocation(_shader, "lutSize");
        _locBlockRows = Raylib.GetShaderLocation(_shader, "blockRows");
        _locAtlasGrid = Raylib.GetShaderLocation(_shader, "atlasGrid");
        _locTileScale = Raylib.GetShaderLocation(_shader, "tileScale");
        _locWater = Raylib.GetShaderLocation(_shader, "waterColor");
        _locRiver = Raylib.GetShaderLocation(_shader, "riverColor");
        _locTime = Raylib.GetShaderLocation(_shader, "time");
        _locBorderScale = Raylib.GetShaderLocation(_shader, "borderScale");
        _locStateBorders = Raylib.GetShaderLocation(_shader, "stateBorders");
        _locShowBorders = Raylib.GetShaderLocation(_shader, "showProvinceBorders");
        _locTint = Raylib.GetShaderLocation(_shader, "tintStrength");
        _locViewPos = Raylib.GetShaderLocation(_shader, "viewPos");
        _locFog = Raylib.GetShaderLocation(_shader, "fogStrength");
        _locMapData = _locLut = 0;

        Raylib.SetShaderValue(_shader, _locMapSize, new Vector2(_map.Width, _map.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locWorldSize, new Vector2(WorldWidth, WorldHeight), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locLutSize, new Vector2(LutWidth, _blockRows * 3), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locBlockRows, (float)_blockRows, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locAtlasGrid, (float)_atlasGrid, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locTileScale, 0.35f, ShaderUniformDataType.Float);
        var wc = _map.Settings.WaterColor;
        var rc = _map.Settings.RiverColor;
        Raylib.SetShaderValue(_shader, _locWater, new Vector3(wc.R / 255f, wc.G / 255f, wc.B / 255f), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locRiver, new Vector3(rc.R / 255f, rc.G / 255f, rc.B / 255f), ShaderUniformDataType.Vec3);
    }

    private void BuildTrees()
    {
        var s = _map.Settings;
        int step = s.TreeSpacing;
        var rnd = new Random(42);
        var positions = new List<(Vector3 Pos, float Size, int Shade)>();
        const int maxTrees = 60000;
        for (int y = step / 2; y < _map.Height; y += step)
        {
            for (int x = step / 2; x < _map.Width; x += step)
            {
                int jx = Math.Clamp(x + rnd.Next(-step / 2, step / 2 + 1), 0, _map.Width - 1);
                int jy = Math.Clamp(y + rnd.Next(-step / 2, step / 2 + 1), 0, _map.Height - 1);
                int i = jy * _map.Width + jx;
                float density = _map.TreeMap[i] / 255f * s.TreeDensity;
                if (density <= 0 || rnd.NextDouble() > density) continue;
                int li = _map.PixelLocation[i];
                if (li < 0 || _game.World.Locations[li].IsWater || _map.RiverMap[i] != 0) continue;
                float wx = (jx + 0.5f) / s.PixelsPerUnit, wz = (jy + 0.5f) / s.PixelsPerUnit;
                positions.Add((new Vector3(wx, HeightAt(wx, wz), wz), 0.7f + (float)rnd.NextDouble() * 0.6f, rnd.Next(40)));
            }
        }
        if (positions.Count == 0) return;
        if (positions.Count > maxTrees)
        {
            // too many for one map: keep an even random sample (not just the northern rows)
            for (int i = positions.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (positions[i], positions[j]) = (positions[j], positions[i]); }
            positions.RemoveRange(maxTrees, positions.Count - maxTrees);
        }
        // low-poly conifers: two stacked cones per tree, lit from the same sun as the terrain
        const int sides = 5, tiers = 2;
        int tris = positions.Count * sides * tiers;
        _treeMesh = new Mesh(tris * 3, tris);
        _treeMesh.AllocVertices();
        _treeMesh.AllocColors();
        var verts = _treeMesh.VerticesAs<Vector3>();
        var cols = _treeMesh.ColorsAs<Color>();
        float baseR = 0.6f / s.PixelsPerUnit * step * 0.3f, height = baseR * 3.6f;
        int v = 0;
        foreach (var (p, size, shade) in positions)
        {
            float r = baseR * size, hgt = height * size;
            // a few trees are lighter or more olive so forests don't look like one flat color
            int kind = shade % 7;
            (int R, int G, int B) baseCol = kind switch { 0 => (52, 86, 34), 1 => (70, 92, 40), _ => (26 + shade / 3, 62 + shade / 2, 30 + shade / 4) };
            for (int tier = 0; tier < tiers; tier++)
            {
                float y0 = hgt * (tier == 0 ? 0.08f : 0.42f);
                float y1 = hgt * (tier == 0 ? 0.68f : 1.0f);
                float rr = r * (tier == 0 ? 1.0f : 0.7f);
                var top = p + new Vector3(0, y1, 0);
                for (int k = 0; k < sides; k++)
                {
                    float a0 = MathF.PI * 2 * k / sides + p.X * 7, a1 = MathF.PI * 2 * (k + 1) / sides + p.X * 7;
                    var b0 = p + new Vector3(MathF.Cos(a0) * rr, y0, MathF.Sin(a0) * rr);
                    var b1 = p + new Vector3(MathF.Cos(a1) * rr, y0, MathF.Sin(a1) * rr);
                    // counter-clockwise when seen from outside
                    verts[v] = b0; verts[v + 1] = top; verts[v + 2] = b1;
                    float light = 0.5f + 0.5f * MathF.Max(0, MathF.Cos((a0 + a1) / 2 - 2.3f));
                    var c = new Color((int)(baseCol.R * light), (int)(baseCol.G * light), (int)(baseCol.B * light), 255);
                    cols[v] = c; cols[v + 1] = new Color((int)Math.Min(255, c.R * 1.35f), (int)Math.Min(255, c.G * 1.35f), (int)Math.Min(255, c.B * 1.35f), 255); cols[v + 2] = c;
                    v += 3;
                }
            }
        }
        Raylib.UploadMesh(ref _treeMesh, new CBool(false));
        _treeMaterial = Raylib.LoadMaterialDefault();
        _hasTrees = true;
    }

    // ===============================================================================================
    // Queries
    // ===============================================================================================

    /// <summary>Ground height at a world position (bilinear on the mesh grid).</summary>
    public float HeightAt(float wx, float wz)
    {
        float gx = wx / WorldWidth * (_hw - 1), gz = wz / WorldHeight * (_hh - 1);
        int x0 = Math.Clamp((int)gx, 0, _hw - 1), z0 = Math.Clamp((int)gz, 0, _hh - 1);
        int x1 = Math.Min(x0 + 1, _hw - 1), z1 = Math.Min(z0 + 1, _hh - 1);
        float fx = Math.Clamp(gx - x0, 0, 1), fz = Math.Clamp(gz - z0, 0, 1);
        float a = _heights[z0 * _hw + x0], b = _heights[z0 * _hw + x1], c = _heights[z1 * _hw + x0], d = _heights[z1 * _hw + x1];
        return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
    }

    public Vector3 WorldPos(float pixelX, float pixelY, float lift = 0)
    {
        float wx = pixelX / _map.Settings.PixelsPerUnit, wz = pixelY / _map.Settings.PixelsPerUnit;
        return new Vector3(wx, HeightAt(wx, wz) + lift, wz);
    }

    public Vector3 LocationPos(Location l, float lift = 0) => WorldPos(l.CenterX, l.CenterY, lift);

    /// <summary>Finds the location under a screen position (ray-marched against the heightfield).</summary>
    public Location? Pick(Vector2 screen, Camera3D cam)
    {
        var ray = Viewport.Ray(screen, cam);
        if (ray.Direction.Y >= 0) return null;
        float maxH = _map.Settings.HeightScale;
        // start where the ray crosses the top of the terrain
        float t = (ray.Position.Y - maxH) / -ray.Direction.Y;
        if (t < 0) t = 0;
        float step = 0.05f * Math.Max(1, cam.Position.Y / 20);
        Vector3 prev = ray.Position + ray.Direction * t;
        for (int i = 0; i < 4000; i++)
        {
            var p = ray.Position + ray.Direction * t;
            if (p.Y <= HeightAt(p.X, p.Z))
            {
                // refine between prev and p
                Vector3 lo = prev, hi = p;
                for (int k = 0; k < 12; k++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (mid.Y <= HeightAt(mid.X, mid.Z)) hi = mid; else lo = mid;
                }
                int px = (int)(hi.X * _map.Settings.PixelsPerUnit), py = (int)(hi.Z * _map.Settings.PixelsPerUnit);
                return _game.World.LocationAtPixel(px, py);
            }
            if (p.Y < -1) break;
            prev = p;
            t += step;
        }
        return null;
    }

    /// <summary>World position under the mouse on the ground plane (for camera dragging).</summary>
    public Vector3? GroundPoint(Vector2 screen, Camera3D cam)
    {
        var ray = Viewport.Ray(screen, cam);
        if (Math.Abs(ray.Direction.Y) < 1e-4) return null;
        float planeY = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale;
        float t = (planeY - ray.Position.Y) / ray.Direction.Y;
        if (t < 0) return null;
        return ray.Position + ray.Direction * t;
    }

    // ===============================================================================================
    // Map modes
    // ===============================================================================================

    private static Color C(Rgb c, int a = 255) => new(c.R, c.G, c.B, (byte)a);

    public static Color HashColor(int i)
    {
        var c = Rgb.FromHsv((i * 0.61803398875) % 1.0, 0.45 + (i % 3) * 0.15, 0.75 + (i % 2) * 0.15);
        return new Color(c.R, c.G, c.B, (byte)200);
    }

    /// <summary>Rebuilds the per-location lookup texture (colors, owners, states, flags).</summary>
    public void UpdateLut()
    {
        var w = _game.World;
        var player = DiplomacyFocus ?? w.Player;
        int block = LutWidth * _blockRows;
        _visibilityVersion = _game.VisibilityVersion;
        Array.Clear(_lut);
        foreach (var l in w.Locations)
        {
            int i = l.Index;
            Color col = new(0, 0, 0, 0);
            var owner = l.Owner;
            if (CustomMode != null)
            {
                var cc = _game.Interface.MapModeColor(_game, CustomMode, l);
                if (cc != null) col = C(cc.Value, 200);
            }
            else switch (Mode)
            {
                case MapMode.Political:
                    if (owner != null && l.IsLand) col = C(owner.Color, 230);
                    break;
                case MapMode.States:
                    if (l.State != null) col = HashColor(l.State.Index + 7);
                    break;
                case MapMode.Locations:
                    col = C(l.MapColor, 230);
                    break;
                case MapMode.Diplomacy:
                    if (owner != null && l.IsLand && player != null) col = DiplomacyColor(player, owner);
                    break;
                case MapMode.Ideology:
                    if (owner != null && l.IsLand && _game.Politics.RulingIdeology(owner) is { } ide) col = C(ide.Color, 225);
                    break;
                case MapMode.Population:
                    if (l.IsLand && l.State != null && _game.Population.HasPops(l.State))
                    {
                        // people per location of the state, on a log scale from sparse (dark green) to crowded (red)
                        double per = _game.Population.TotalOf(l.State) / Math.Max(1, l.State.Locations.Count(x => x.IsLand));
                        float t = (float)Math.Clamp((Math.Log10(Math.Max(1, per)) - 3) / 3.5, 0, 1);
                        col = t < 0.5f ? new Color((int)(40 + 400 * t), (int)(90 + 200 * t), 50, 220) : new Color(240, (int)(190 - 300 * (t - 0.5f)), 50, 220);
                    }
                    break;
                case MapMode.Culture:
                    if (l.IsLand && l.State != null && _game.Population.Culture(_game.Population.MajorityCulture(l.State)) is { } cul) col = C(cul.Color, 225);
                    break;
                case MapMode.Religion:
                    if (l.IsLand && l.State != null && _game.Population.Religion(_game.Population.MajorityReligion(l.State)) is { } rel) col = C(rel.Color, 225);
                    break;
                case MapMode.Terrain:
                    break;
                case MapMode.Supply:
                    if (player != null && l.IsLand && _game.Features.HasSupply)
                    {
                        double v = _game.Supply.SupplyAt(player, l);
                        if (v > 0.05)
                        {
                            float t = (float)Math.Clamp(v / 6.0, 0, 1);
                            col = t < 0.5f ? new Color(210, (int)(60 + 300 * t), 50, 215) : new Color((int)(210 - 300 * (t - 0.5f)), 210, 60, 215);
                        }
                        else if (l.Owner != null) col = new Color(60, 60, 60, 180);
                    }
                    break;
                case MapMode.Air:
                    if (l.IsLand && _game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && _game.Air.RegionOf(l) is { } region)
                    {
                        var (share, contested) = player != null ? _game.Air.Superiority(region, player) : (0.5, false);
                        if (contested)
                        {
                            float t = (float)share;
                            col = t < 0.5f ? new Color(215, (int)(60 + 300 * t), 50, 215) : new Color((int)(215 - 300 * (t - 0.5f)), 205, 70, 215);
                        }
                        else
                        {
                            var hc = HashColor(region.Index * 3 + 5);
                            col = new Color((byte)(hc.R * 0.55 + 40), (byte)(hc.G * 0.55 + 40), (byte)(hc.B * 0.55 + 50), (byte)190);
                        }
                    }
                    break;
            }
            _lut[i] = col;

            int ownerId = owner != null ? owner.Index + 1 : 0;
            int stateId = l.State != null ? l.State.Index + 1 : 0;
            // the air map mode draws region borders instead of state borders
            if (Mode == MapMode.Air && CustomMode == null && _game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && _game.Air.RegionOf(l) is { } ar) stateId = ar.Index + 1;
            _lut[block + i] = new Color(ownerId & 0xFF, (ownerId >> 8) & 0xFF, stateId & 0xFF, (stateId >> 8) & 0xFF);

            int flags = 0;
            if (l.IsWater) flags |= 1;
            bool occupied = l.IsOccupied && Mode is MapMode.Political or MapMode.Diplomacy;
            if (occupied) flags |= 2;
            if (l == Selected) flags |= 4;
            if (l == Hovered) flags |= 8;
            if (l.IsLake) flags |= 16;
            if (!_game.IsVisible(l)) flags |= 32;
            if (HighlightState != null && l.State == HighlightState) flags |= 64;
            var stripe = occupied && l.Controller != null ? l.Controller.Color : Rgb.Black;
            _lut[block * 2 + i] = new Color(stripe.R, stripe.G, stripe.B, (byte)flags);
        }
        Raylib.UpdateTexture(_lutTex, _lut);
        Dirty = false;
    }

    private Color DiplomacyColor(Country me, Country other)
    {
        var w = _game.World;
        var R = w.Relations;
        if (other == me) return new Color(70, 200, 70, 225);
        if (w.AtWar(me, other)) return new Color(200, 40, 40, 210);
        if (R.AreAllied(me, other)) return new Color(50, 110, 220, 200);
        if (R.OverlordOf(other) == me || R.OverlordOf(me) == other) return new Color(110, 190, 230, 200);
        if (R.HasTruce(me, other, w.Date)) return new Color(150, 150, 150, 200);
        double op = _game.Diplomacy.Opinion(other, me);
        // neutrals stay muted so the viewed country (green), allies (blue) and enemies (red) stand out;
        // a faint tint still shows opinion (brownish = dislikes, grey-green = likes)
        float t = (float)Math.Clamp((op + 100) / 200, 0, 1);
        return new Color((int)(150 - 50 * t), (int)(112 + 30 * t), (int)(100 + 12 * t), 120);
    }

    // ===============================================================================================
    // Drawing
    // ===============================================================================================

    /// <summary>Camera height above which the map is a strategic overview: no fog of war, no unit counters.</summary>
    public float StrategicZoom => Math.Max(140f, Math.Max(WorldWidth, WorldHeight) * 0.33f);

    public void Draw(Camera3D cam, float time)
    {
        if (_game.VisibilityVersion != _visibilityVersion) Dirty = true;
        if (Dirty) UpdateLut();
        Raylib.SetShaderValue(_shader, _locTime, time, ShaderUniformDataType.Float);
        float camH = cam.Position.Y;
        // terrain shows through the map colors up close; far out the map reads like a political map
        float tint = Math.Clamp(0.55f + (camH - 25f) / 160f, 0.55f, 0.95f);
        Raylib.SetShaderValue(_shader, _locTint, tint, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locViewPos, cam.Position, ShaderUniformDataType.Vec3);
        // far out (strategic view) the fog of war fades away
        float fog = Math.Clamp((StrategicZoom - camH) / (StrategicZoom * 0.2f), 0f, 1f);
        Raylib.SetShaderValue(_shader, _locFog, fog, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locBorderScale, Math.Clamp(camH / 40f, 0.6f, 4f), ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locShowBorders, camH < 120 ? 1.0f : 0.0f, ShaderUniformDataType.Float);
        // state borders fade out when zoomed out (country borders stay)
        float stateFade = Math.Clamp((StrategicZoom * 0.6f - camH) / (StrategicZoom * 0.25f), 0f, 1f);
        if (HighlightState != null) stateFade = Math.Max(stateFade, 0.6f);
        Raylib.SetShaderValue(_shader, _locStateBorders, stateFade, ShaderUniformDataType.Float);
        // endless sea around the map, drawn with the map shader so it matches the water at the edges
        float seaY = _map.Settings.SeaLevel / 255f * _map.Settings.HeightScale - 0.03f;
        Raylib.DrawMesh(_seaMesh, _material, Raymath.MatrixTranslate(WorldWidth / 2, seaY, WorldHeight / 2));
        Raylib.DrawMesh(_terrainMesh, _material, Matrix4x4.Identity);
        if (_hasTrees && (camH < 40 || (camH < 70 && Mode == MapMode.Terrain)))
            Raylib.DrawMesh(_treeMesh, _treeMaterial, Matrix4x4.Identity);
    }

    public void Dispose()
    {
        Raylib.UnloadTexture(_mapDataTex);
        Raylib.UnloadTexture(_lutTex);
        Raylib.UnloadTexture(_atlasTex);
        Raylib.UnloadShader(_shader);
        Raylib.UnloadMesh(_terrainMesh);
        Raylib.UnloadMesh(_seaMesh);
        if (_hasTrees) Raylib.UnloadMesh(_treeMesh);
    }
}

