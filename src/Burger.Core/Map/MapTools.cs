using System.Text;
using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>Map utilities shared by the CLI and the editor.</summary>
public static class MapTools
{
    private static readonly string[] A = { "Ar", "Bel", "Cor", "Dra", "El", "Fen", "Gal", "Har", "Ist", "Kar", "Lor", "Mar", "Nor", "Os", "Pel", "Ros", "Sal", "Tor", "Ul", "Val", "Wes", "Zan" };
    private static readonly string[] B = { "a", "e", "i", "o", "an", "en", "ia", "or" };
    private static readonly string[] C = { "burg", "dale", "ford", "heim", "mont", "nar", "stad", "ton", "vik", "wick", "grad", "holm", "gard" };

    /// <summary>
    /// Appends a generated name to map/locations.txt for every color of the province map that has none.
    /// Returns (added, total colors).
    /// </summary>
    public static (int Added, int Total) GenerateLocationNames(string gameRoot)
    {
        string mapDir = Path.Combine(gameRoot, "map");
        var settingsFile = Path.Combine(mapDir, "default.map");
        var settings = File.Exists(settingsFile) ? MapSettings.FromNode(ScriptParser.ParseFile(settingsFile)) : new MapSettings();
        var img = ImageIO.Load(Path.Combine(mapDir, settings.Provinces), false, settings.Provinces)
                  ?? throw new FileNotFoundException($"map/{settings.Provinces} not found");
        string locFile = Path.Combine(mapDir, settings.Locations);
        var known = new HashSet<int>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(locFile))
        {
            foreach (var n in ScriptParser.ParseFile(locFile).All)
            {
                if (Rgb.TryParseHex(n.Key, out var c)) { known.Add(c.Packed); names.Add(n.Value ?? n.GetString("name") ?? ""); }
                else if (n.Value != null && Rgb.TryParseHex(n.Value, out var c2)) { known.Add(c2.Packed); names.Add(n.Key); }
            }
        }
        var colors = new HashSet<int>();
        for (int i = 0; i < img.Width * img.Height; i++)
        {
            int p = i * 4;
            if (img.Data[p + 3] == 0) continue;
            colors.Add((img.Data[p] << 16) | (img.Data[p + 1] << 8) | img.Data[p + 2]);
        }
        var missing = colors.Where(c => !known.Contains(c)).OrderBy(c => c).ToList();
        if (missing.Count == 0) return (0, colors.Count);
        var rnd = new Random(missing.Count);
        var sb = new StringBuilder();
        sb.Append($"\n# Added automatically on {DateTime.Now:yyyy-MM-dd} - rename freely (keep the hex color)\n");
        foreach (var col in missing)
        {
            string nm;
            int guard = 0;
            do nm = (A[rnd.Next(A.Length)] + B[rnd.Next(B.Length)] + C[rnd.Next(C.Length)]).ToLowerInvariant();
            while (!names.Add(nm) && ++guard < 50);
            if (guard >= 50) nm = "loc_" + Rgb.FromPacked(col).ToHex().ToLowerInvariant();
            sb.Append($"{Rgb.FromPacked(col).ToHex()} = {nm}\n");
        }
        Directory.CreateDirectory(mapDir);
        File.AppendAllText(locFile, sb.ToString());
        return (missing.Count, colors.Count);
    }
}
