using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Fonts, flags and generated textures.</summary>
public static unsafe class Assets
{
    /// <summary>Body text font.</summary>
    public static Font Font;
    /// <summary>Buttons and numbers.</summary>
    public static Font BoldFont;
    /// <summary>Panel titles and names on the map.</summary>
    public static Font HeaderFont;
    public static bool CustomFont;
    private static readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Country, (int Version, Texture2D Tex)> _flags = new();
    private static readonly List<Font> _loadedFonts = new();
    private static string _root = "";
    private static string _fontKey = "";

    private static readonly int[] Codepoints = Enumerable.Range(32, 95).Concat(Enumerable.Range(160, 224))
        .Concat(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2026, 0x20AC, 0x2191, 0x2193, 0x2192, 0x2190 }).ToArray();

    public static void Init(string gameRoot, Burger.Core.Mechanics.ThemeDef? theme = null)
    {
        _root = gameRoot;
        foreach (var t in _textures.Values) if (t.Id != 0) Raylib.UnloadTexture(t);
        _textures.Clear();
        _flags.Clear();
        _fontKey = "";
        LoadFonts(theme);
    }

    /// <summary>Loads the theme fonts (reloads only when the font files changed).</summary>
    public static void LoadFonts(Burger.Core.Mechanics.ThemeDef? theme)
    {
        string key = $"{theme?.Font}|{theme?.BoldFont}|{theme?.HeaderFont}";
        if (key == _fontKey) return;
        _fontKey = key;
        foreach (var f in _loadedFonts) Raylib.UnloadFont(f);
        _loadedFonts.Clear();

        // candidates: the theme's fonts, then gfx/fonts, then the engine fonts, then system fonts
        var dirFonts = new List<string>();
        foreach (var dir in new[] { Path.Combine(_root, "gfx", "fonts"), Path.Combine(AppContext.BaseDirectory, "fonts") })
            if (Directory.Exists(dir))
                dirFonts.AddRange(Directory.GetFiles(dir, "*.*").Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f));
        bool IsBold(string f) => Path.GetFileName(f).Contains("bold", StringComparison.OrdinalIgnoreCase);
        bool IsDisplay(string f) => Path.GetFileNameWithoutExtension(f).EndsWith("SC", StringComparison.Ordinal) || Path.GetFileName(f).Contains("display", StringComparison.OrdinalIgnoreCase);
        string? Themed(string? rel) => string.IsNullOrWhiteSpace(rel) ? null : Path.Combine(_root, rel.Replace('\\', '/'));
        var system = new List<string>();
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(win)) system.AddRange(new[] { "segoeui.ttf", "arial.ttf", "tahoma.ttf" }.Select(f => Path.Combine(win, f)));
        system.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        var systemBold = new List<string>();
        if (!string.IsNullOrEmpty(win)) systemBold.AddRange(new[] { "segoeuib.ttf", "arialbd.ttf", "tahomabd.ttf" }.Select(f => Path.Combine(win, f)));
        systemBold.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");

        var body = new List<string?> { Themed(theme?.Font) };
        body.AddRange(dirFonts.Where(f => !IsBold(f) && !IsDisplay(f)));
        body.AddRange(system);
        CustomFont = TryLoad(body, 36, out Font);
        if (!CustomFont) Font = Raylib.GetFontDefault();

        var bold = new List<string?> { Themed(theme?.BoldFont) };
        bold.AddRange(dirFonts.Where(f => IsBold(f) && !IsDisplay(f)));
        bold.AddRange(systemBold);
        if (!TryLoad(bold, 36, out BoldFont)) BoldFont = Font;

        var header = new List<string?> { Themed(theme?.HeaderFont) };
        header.AddRange(dirFonts.Where(IsDisplay));
        if (!TryLoad(header, 72, out HeaderFont)) HeaderFont = BoldFont;
        if (theme != null && !string.IsNullOrWhiteSpace(theme.Font) && !File.Exists(Themed(theme.Font)))
            Log.Warn($"Theme font not found: {theme.Font}", "interface/theme.txt");
    }

    private static bool TryLoad(List<string?> candidates, int size, out Font font)
    {
        foreach (var c in candidates)
        {
            if (c == null || !File.Exists(c)) continue;
            var f = Raylib.LoadFontEx(c, size, Codepoints, Codepoints.Length);
            if (f.Texture.Id == 0 || f.GlyphCount == 0) continue;
            // mipmaps keep small text sharp instead of thin and broken
            var tex = f.Texture;
            Raylib.GenTextureMipmaps(ref tex);
            f.Texture = tex;
            Raylib.SetTextureFilter(f.Texture, TextureFilter.Trilinear);
            _loadedFonts.Add(f);
            font = f;
            return true;
        }
        font = default;
        return false;
    }

    /// <summary>Loads (and caches) a texture from a path relative to the game folder. Returns null if missing.</summary>
    public static Texture2D? Texture(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        if (_textures.TryGetValue(relative, out var t)) return t.Id == 0 ? null : t;
        string path = Path.Combine(_root, relative.Replace('\\', '/'));
        Texture2D tex = default;
        if (File.Exists(path))
        {
            tex = Raylib.LoadTexture(path);
            if (tex.Id != 0)
            {
                Raylib.GenTextureMipmaps(ref tex);
                Raylib.SetTextureFilter(tex, TextureFilter.Trilinear);
            }
        }
        _textures[relative] = tex;
        return tex.Id == 0 ? null : tex;
    }

    /// <summary>The flag of a country; reloads automatically after <c>set_flag_image</c>. Missing files get a generated flag.</summary>
    public static Texture2D Flag(Country c)
    {
        if (_flags.TryGetValue(c, out var f) && f.Version == c.FlagVersion) return f.Tex;
        Texture2D tex;
        var loaded = Texture(c.FlagPath);
        if (loaded != null) tex = loaded.Value;
        else
        {
            var img = GeneratedFlag(c);
            tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
        }
        _flags[c] = (c.FlagVersion, tex);
        return tex;
    }

    public static Image GeneratedFlag(Country c)
    {
        var col = new Color(c.Color.R, c.Color.G, c.Color.B, (byte)255);
        var light = Raylib.ColorBrightness(col, 0.45f);
        var dark = Raylib.ColorBrightness(col, -0.45f);
        var img = Raylib.GenImageColor(96, 64, col);
        int style = c.Tag.Sum(ch => ch) % 3;
        if (style == 0)
        {
            Raylib.ImageDrawRectangle(ref img, 0, 0, 32, 64, dark);
            Raylib.ImageDrawRectangle(ref img, 64, 0, 32, 64, light);
        }
        else if (style == 1)
        {
            Raylib.ImageDrawRectangle(ref img, 0, 0, 96, 21, light);
            Raylib.ImageDrawRectangle(ref img, 0, 43, 96, 21, dark);
        }
        else
        {
            Raylib.ImageDrawRectangle(ref img, 0, 26, 96, 12, light);
            Raylib.ImageDrawRectangle(ref img, 30, 0, 12, 64, light);
        }
        return img;
    }

    /// <summary>A procedural texture tile tinted with a color (used when a terrain has no texture file).</summary>
    public static Image NoiseTile(int size, Rgb tint, int seed)
    {
        var img = Raylib.GenImageColor(size, size, Color.White);
        byte* px = (byte*)img.Data;
        var rnd = new Random(seed);
        // value noise from a few octaves of a random grid
        float[,] grid = new float[17, 17];
        for (int y = 0; y < 17; y++) for (int x = 0; x < 17; x++) grid[x, y] = (float)rnd.NextDouble();
        for (int x = 0; x < 17; x++) grid[x, 16] = grid[x, 0];
        for (int y = 0; y < 17; y++) grid[16, y] = grid[0, y];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float v = 0, amp = 0.5f, total = 0;
                for (int o = 0; o < 3; o++)
                {
                    int cells = 4 << o;
                    float fx = x * cells / (float)size, fy = y * cells / (float)size;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = fx - x0, ty = fy - y0;
                    tx = tx * tx * (3 - 2 * tx);
                    ty = ty * ty * (3 - 2 * ty);
                    int sx = 16 / cells;
                    float a = grid[(x0 * sx) % 16, (y0 * sx) % 16], b = grid[((x0 + 1) * sx) % 16, (y0 * sx) % 16];
                    float c = grid[(x0 * sx) % 16, ((y0 + 1) * sx) % 16], d = grid[((x0 + 1) * sx) % 16, ((y0 + 1) * sx) % 16];
                    v += amp * ((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty);
                    total += amp;
                    amp *= 0.5f;
                }
                v /= total;
                float grain = (float)rnd.NextDouble() * 0.08f;
                float k = 0.75f + 0.45f * v + grain;
                int i = (y * size + x) * 4;
                px[i] = (byte)Math.Clamp(tint.R * k, 0, 255);
                px[i + 1] = (byte)Math.Clamp(tint.G * k, 0, 255);
                px[i + 2] = (byte)Math.Clamp(tint.B * k, 0, 255);
                px[i + 3] = 255;
            }
        }
        return img;
    }
}
