using Burger.Core.Script;

namespace Burger.Core.Modding;

/// <summary>
/// The features (mechanics) a game uses, from <c>game/features.txt</c>:
/// <code>features = { military combat_hoi4 diplomacy characters economy ai }</code>
/// The engine only runs the systems of installed features: no <c>military</c> = no armies, battles or recruit menu;
/// no <c>diplomacy</c> = no wars, peace deals or diplomatic actions; and so on. C# scripts can check
/// <c>Game.Features.Has("my_feature")</c> and scripts can use the trigger <c>has_feature = my_feature</c>.
/// Custom features (your own ids) work the same way - the engine just records them.
/// A game without features.txt (made before features existed) gets every built-in feature.
/// </summary>
public sealed class FeatureSet
{
    // built-in feature ids the engine itself checks
    public const string Military = "military";
    public const string Diplomacy = "diplomacy";
    public const string Characters = "characters";
    public const string CharacterInteractions = "character_interactions";
    public const string Economy = "economy";
    public const string FogOfWar = "fog_of_war";
    public const string Ai = "ai";

    public const string Production = "production";
    public const string SupplyRange = "supply_range";
    public const string SupplyHubs = "supply_hubs";
    public const string DivisionDesigner = "division_designer";
    public const string Navy = "navy";
    public const string Air = "air";
    public const string BattlePlans = "battle_plans";
    public const string LendLease = "lend_lease";

    /// <summary>The features games had before features existed (all on for a game without features.txt).</summary>
    public static readonly string[] BuiltIn = { Military, Diplomacy, Characters, CharacterInteractions, Economy, FogOfWar, Ai };

    /// <summary>Either supply feature.</summary>
    public bool HasSupply => Has(SupplyRange) || Has(SupplyHubs);

    private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the game has no features.txt: every built-in system runs (older projects).</summary>
    public bool Legacy { get; private set; }

    public IReadOnlyCollection<string> Ids => _ids;

    public bool Has(string id) => Legacy && BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase) || _ids.Contains(id);

    /// <summary>Turns a feature on or off while the game runs (e.g. from a C# script or the console).</summary>
    public void Set(string id, bool on)
    {
        if (Legacy && !on) { Legacy = false; foreach (var b in BuiltIn) _ids.Add(b); }
        if (on) _ids.Add(id); else _ids.Remove(id);
    }

    public static string FilePath(string gameRoot) => Path.Combine(gameRoot, "features.txt");

    public static FeatureSet Load(string gameRoot)
    {
        var set = new FeatureSet();
        string path = FilePath(gameRoot);
        if (!File.Exists(path)) { set.Legacy = true; return set; }
        var root = ScriptParser.ParseFile(path);
        foreach (var id in ReadIds(root)) set._ids.Add(id);
        return set;
    }

    public static List<string> ReadIds(Node root) => root.Get("features")?.Items().ToList() ?? new List<string>();

    /// <summary>Writes the features file (keeps the comment at the top).</summary>
    public static void Save(string gameRoot, IEnumerable<string> ids)
    {
        var list = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string text = "# Features (mechanics) this game uses. Manage them in Burger Studio: Project > Features,\n" +
                      "# or with: burger features <project> add|remove <id>\n" +
                      "features = {\n" + string.Concat(list.Select(i => "\t" + i + "\n")) + "}\n";
        File.WriteAllText(FilePath(gameRoot), text);
    }
}
