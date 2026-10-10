using System.Globalization;
using Burger.Core.Script;

namespace Burger.Core;

/// <summary>
/// Text lookup. Files in <c>localisation/</c> use the normal script format:
/// <code>berlin = "Berlin"
/// rome.1.title = "The Ides of March"</code>
/// Any string in data files is first looked up here; if no entry exists the string itself is shown,
/// so modders can also write text directly into event files.
/// </summary>
public static class Localisation
{
    private static readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);

    public static int Count => _texts.Count;

    public static void Clear() => _texts.Clear();

    public static void LoadDirectory(string dir, string gameRoot)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var root = ScriptParser.ParseFile(file, Path.GetRelativePath(gameRoot, file));
            foreach (var n in root.All)
            {
                if (n.Value != null) _texts[n.Key] = n.Value;
                else if (n.Children != null)
                {
                    // allow language blocks:  english = { key = "text" }
                    foreach (var c in n.All)
                        if (c.Value != null) _texts[c.Key] = c.Value;
                }
            }
        }
    }

    public static void Set(string key, string text) => _texts[key] = text;

    public static bool Has(string key) => _texts.ContainsKey(key);

    /// <summary>Returns the localised text, or the key itself if it is not a localisation key.</summary>
    public static string Get(string? keyOrText)
    {
        if (string.IsNullOrEmpty(keyOrText)) return "";
        return _texts.TryGetValue(keyOrText, out var t) ? t : keyOrText;
    }

    /// <summary>Localised text, or a prettified version of the key (<c>new_york</c> → <c>New York</c>).</summary>
    public static string GetOrPretty(string key)
    {
        if (_texts.TryGetValue(key, out var t)) return t;
        return Prettify(key);
    }

    public static string Prettify(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        var s = key.Replace('_', ' ').Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
    }
}
