using Burger.Core.Script;

namespace Burger.Core.Modding;

/// <summary>
/// A feature package from the engine's <c>assets/features/&lt;id&gt;/</c> folder: a <c>feature.txt</c> description plus
/// the files it adds to a project (mirroring the project folder: <c>backend/...</c>, <c>game/...</c>).
/// Installing copies the files into the project, where they are the project's own and fully editable.
/// </summary>
public sealed class FeaturePackage
{
    public string Id = "";
    public string Name = "";
    public string Category = "Other";
    public string Description = "";
    public List<string> Requires = new();
    public List<string> Conflicts = new();
    /// <summary>Alternatives share a group (e.g. "combat"): the UI offers them as a choice.</summary>
    public string? Group;
    /// <summary>Defines written into common/defines.txt when installed (e.g. combat_model = hoi4).</summary>
    public List<(string Key, string Value)> Defines = new();
    /// <summary>Install steps that create content for the project's own map (e.g. starting_armies).</summary>
    public List<string> Generate = new();
    /// <summary>Folder of the package in the engine.</summary>
    public string Dir = "";
    /// <summary>Files the package adds, relative to the project folder (backend/X.cs, game/common/...).</summary>
    public List<string> Files = new();
    public int Order;
}

/// <summary>A starting set of features (and rules) for a new project, from <c>assets/presets/*.txt</c>.</summary>
public sealed class FeaturePreset
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public List<string> Features = new();
    public List<(string Key, string Value)> Defines = new();
    public int Order;
}

/// <summary>Finds, installs and removes feature packages.</summary>
public static class FeaturePackages
{
    /// <summary>Generated files of each install step (removed with the feature).</summary>
    private static readonly Dictionary<string, string> GeneratedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["starting_armies"] = "game/history/units/00_starting_armies.txt",
        ["starting_fleets"] = "game/history/fleets/00_starting_fleets.txt",
        ["starting_air_regions"] = "game/map/air_regions.txt",
        ["starting_air"] = "game/history/air/00_starting_air.txt",
    };

    public static List<FeaturePackage> All()
    {
        var list = new List<FeaturePackage>();
        var root = EngineAssets.Find("features");
        if (root == null) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            string manifest = Path.Combine(dir, "feature.txt");
            if (!File.Exists(manifest)) continue;
            var n = ScriptParser.ParseFile(manifest);
            var p = new FeaturePackage
            {
                Id = Path.GetFileName(dir),
                Name = n.GetString("name") ?? Path.GetFileName(dir),
                Category = n.GetString("category") ?? "Other",
                Description = n.GetString("description") ?? "",
                Requires = n.Get("requires")?.Items().ToList() ?? new List<string>(),
                Conflicts = n.Get("conflicts")?.Items().ToList() ?? new List<string>(),
                Group = n.GetString("group"),
                Generate = n.Get("generate")?.Items().ToList() ?? new List<string>(),
                Order = n.GetInt("order", 100),
                Dir = dir,
            };
            if (n.Get("defines") is { } d)
                foreach (var c in d.All) p.Defines.Add((c.Key, c.Value ?? "{ " + string.Join(" ", c.Items()) + " }"));
            p.Files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .Where(f => !f.Equals("feature.txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            list.Add(p);
        }
        return list.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
    }

    public static FeaturePackage? Get(string id) => All().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static List<FeaturePreset> Presets()
    {
        var list = new List<FeaturePreset>();
        var root = EngineAssets.Find("presets");
        if (root == null) return list;
        foreach (var file in Directory.GetFiles(root, "*.txt"))
        {
            var n = ScriptParser.ParseFile(file);
            var p = new FeaturePreset
            {
                Id = Path.GetFileNameWithoutExtension(file),
                Name = n.GetString("name") ?? Path.GetFileNameWithoutExtension(file),
                Description = n.GetString("description") ?? "",
                Features = n.Get("features")?.Items().ToList() ?? new List<string>(),
                Order = n.GetInt("order", 100),
            };
            if (n.Get("defines") is { } d)
                foreach (var c in d.All) p.Defines.Add((c.Key, c.Value ?? "{ " + string.Join(" ", c.Items()) + " }"));
            list.Add(p);
        }
        return list.OrderBy(p => p.Order).ThenBy(p => p.Name).ToList();
    }

    /// <summary>Base maps a new project can start from (assets/bases/&lt;id&gt;/game).</summary>
    public static List<(string Id, string Name, string Description, string Dir)> Bases()
    {
        var list = new List<(string, string, string, string)>();
        var root = EngineAssets.Find("bases");
        if (root == null) return list;
        foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            string info = Path.Combine(dir, "base.txt");
            var n = File.Exists(info) ? ScriptParser.ParseFile(info) : Node.Root("base");
            list.Add((Path.GetFileName(dir), n.GetString("name") ?? Path.GetFileName(dir), n.GetString("description") ?? "", dir));
        }
        return list;
    }

    /// <summary>Features installed in a project (from game/features.txt; null for a project made before features existed).</summary>
    public static List<string>? Installed(string projectDir)
    {
        string path = FeatureSet.FilePath(Path.Combine(projectDir, "game"));
        if (!File.Exists(path)) return null;
        return FeatureSet.ReadIds(ScriptParser.ParseFile(path));
    }

    /// <summary>The ids plus everything they require (in install order).</summary>
    public static List<string> WithRequirements(IEnumerable<string> ids)
    {
        var all = All().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        void Add(string id, int depth)
        {
            if (depth > 20 || result.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            if (all.TryGetValue(id, out var p)) foreach (var r in p.Requires) Add(r, depth + 1);
            result.Add(id);
        }
        foreach (var id in ids) Add(id, 0);
        return result;
    }

    /// <summary>Installed features that need this one.</summary>
    public static List<string> Dependents(string projectDir, string id)
    {
        var installed = Installed(projectDir) ?? new List<string>();
        return All().Where(p => installed.Contains(p.Id, StringComparer.OrdinalIgnoreCase) && p.Requires.Contains(id, StringComparer.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
    }

    /// <summary>
    /// Installs features (and what they require) into a project: copies their files (existing files are kept),
    /// sets their defines, runs their install steps and records them in game/features.txt. Returns messages.
    /// </summary>
    public static List<string> Install(string projectDir, IEnumerable<string> ids, IEnumerable<(string Key, string Value)>? extraDefines = null)
    {
        var log = new List<string>();
        var all = All().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        string game = Path.Combine(projectDir, "game");
        var installed = Installed(projectDir) ?? new List<string>();
        foreach (var id in WithRequirements(ids))
        {
            if (!all.TryGetValue(id, out var p))
            {
                // a custom feature id without a package: just recorded (scripts can check has_feature)
                if (!installed.Contains(id, StringComparer.OrdinalIgnoreCase)) { installed.Add(id); log.Add($"Added custom feature '{id}' (no engine package)"); }
                continue;
            }
            var conflict = p.Conflicts.FirstOrDefault(c => installed.Contains(c, StringComparer.OrdinalIgnoreCase));
            if (conflict != null) { log.Add($"Skipped {p.Name}: conflicts with installed feature '{conflict}'"); continue; }
            int copied = 0, kept = 0;
            foreach (var rel in p.Files)
            {
                string dst = Path.Combine(projectDir, rel);
                if (File.Exists(dst)) { kept++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(Path.Combine(p.Dir, rel), dst);
                copied++;
            }
            foreach (var (k, v) in p.Defines) SetDefine(game, k, v);
            foreach (var step in p.Generate) log.Add(RunStep(step, projectDir));
            if (!installed.Contains(p.Id, StringComparer.OrdinalIgnoreCase)) installed.Add(p.Id);
            log.Add($"Installed {p.Name}: {copied} file(s) added" + (kept > 0 ? $", {kept} already existed and were kept" : ""));
        }
        if (extraDefines != null) foreach (var (k, v) in extraDefines) SetDefine(game, k, v);
        FeatureSet.Save(game, installed);
        return log;
    }

    /// <summary>
    /// Removes a feature: deletes its files that were not changed, moves changed ones to removed_features/&lt;id&gt;/
    /// (so no work is lost) and takes it out of game/features.txt. Does not remove features that need it - check
    /// <see cref="Dependents"/> first.
    /// </summary>
    public static List<string> Uninstall(string projectDir, string id)
    {
        var log = new List<string>();
        var p = Get(id);
        var installed = Installed(projectDir) ?? FeatureSet.BuiltIn.ToList();
        if (p != null)
        {
            var files = p.Files.Select(f => (Rel: f, Src: Path.Combine(p.Dir, f))).ToList();
            foreach (var step in p.Generate)
                if (GeneratedFiles.TryGetValue(step, out var gen)) files.Add((gen, ""));
            int deleted = 0, moved = 0;
            foreach (var (rel, src) in files)
            {
                string path = Path.Combine(projectDir, rel);
                if (!File.Exists(path)) continue;
                bool unchanged = src.Length > 0 && File.Exists(src) && File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(path));
                if (unchanged) { File.Delete(path); deleted++; } // generated files may have been edited: they are moved too
                else
                {
                    string dst = Path.Combine(projectDir, "removed_features", p.Id, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(path, dst);
                    moved++;
                }
            }
            log.Add($"Removed {p.Name}: {deleted} file(s) deleted" + (moved > 0 ? $", {moved} changed file(s) moved to removed_features/{p.Id}/" : ""));
        }
        installed.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        FeatureSet.Save(Path.Combine(projectDir, "game"), installed);
        if (p == null) log.Add($"Removed custom feature '{id}'");
        return log;
    }

    /// <summary>Sets a define in the project's common/defines.txt (keeps comments and other values).</summary>
    public static void SetDefine(string gameDir, string key, string value)
    {
        string file = Path.Combine(gameDir, "common", "defines.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string text = File.Exists(file) ? File.ReadAllText(file) : "# Game rules. Every value has a default in the engine.\n";
        text = ScriptPatch.SetValue(text, "common/defines.txt", new[] { key }, value);
        File.WriteAllText(file, text);
    }

    // ------------------------------------------------------------------------------------- install steps
    private static string RunStep(string step, string projectDir)
    {
        try
        {
            return step.ToLowerInvariant() switch
            {
                "starting_armies" => StartingArmies(projectDir),
                "starting_treasury" => StartingTreasury(projectDir),
                "starting_factories" => StateBuildings(projectDir, "factories", (st, i, n) =>
                {
                    double tax = st.GetDouble("tax", 2);
                    return new[] { ("civilian_factory", (int)Math.Clamp(Math.Round(tax * 0.7), 0, 8)), ("military_factory", (int)Math.Clamp(Math.Round(tax * 0.45), 0, 6)) };
                }),
                "starting_infrastructure" => StateBuildings(projectDir, "infrastructure", (st, i, n) => new[] { ("infrastructure", 2) }),
                "starting_railways" => StateBuildings(projectDir, "railways and supply hubs", (st, i, n) =>
                {
                    int locs = st.GetList("locations").Count;
                    var list = new List<(string, int)> { ("railway", 1) };
                    if (locs >= 4 || i % 3 == 0) list.Add(("supply_hub", 1));
                    return list.ToArray();
                }),
                "starting_fleets" => StartingFleets(projectDir),
                "starting_naval_bases" => NavalBuildings(projectDir),
                "starting_air_regions" => AirRegions(projectDir),
                "starting_air_bases" => AirBases(projectDir),
                "starting_air" => StartingAir(projectDir),
                _ => $"Unknown install step '{step}'",
            };
        }
        catch (Exception ex) { return $"Install step '{step}' failed: {ex.Message}"; }
    }

    private static IEnumerable<string> TxtFiles(string dir)
        => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories).OrderBy(f => f) : Enumerable.Empty<string>();

    private static IEnumerable<string> CountryFiles(string game)
    {
        var list = TxtFiles(Path.Combine(game, "common", "countries")).ToList();
        string single = Path.Combine(game, "common", "countries.txt");
        if (File.Exists(single)) list.Add(single);
        return list;
    }

    /// <summary>Every country with a capital gets an army of two divisions of the first division template.</summary>
    private static string StartingArmies(string projectDir)
    {
        string game = Path.Combine(projectDir, "game");
        string? template = null;
        foreach (var f in TxtFiles(Path.Combine(game, "common", "units")))
        {
            template = ScriptParser.ParseFile(f).Get("division_templates")?.All.FirstOrDefault(n => n.Children != null)?.Key;
            if (template != null) break;
        }
        if (template == null) return "Starting armies: no division template found";
        var hasUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in TxtFiles(Path.Combine(game, "history", "units")))
            foreach (var n in ScriptParser.ParseFile(f).All) hasUnits.Add(n.Key);
        // bigger countries start with bigger armies (about one division per three states, 2-24)
        var statesOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in TxtFiles(Path.Combine(game, "map", "states")))
            foreach (var st in ScriptParser.ParseFile(f).All)
                if (st.GetString("owner") is { } ow) statesOf[ow] = statesOf.GetValueOrDefault(ow) + 1;
        var sb = new System.Text.StringBuilder("# Starting armies, created when the military feature was added. Edit freely.\n# TAG = { army = { name general division = { template location count } } }\n\n");
        int n2 = 0;
        foreach (var f in CountryFiles(game))
            foreach (var c in ScriptParser.ParseFile(f).All)
            {
                if (c.Children == null || hasUnits.Contains(c.Key)) continue;
                string? capital = c.GetString("capital");
                if (capital == null) continue;
                sb.Append($"{c.Key} = {{\n\tarmy = {{\n\t\tname = \"1st Army\"\n\t\tdivision = {{ template = {template} location = {capital} count = {Math.Clamp(statesOf.GetValueOrDefault(c.Key) / 3, 2, 24)} }}\n\t}}\n}}\n");
                n2++;
            }
        if (n2 == 0) return "Starting armies: every country already has units";
        string path = Path.Combine(projectDir, GeneratedFiles["starting_armies"]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        return $"Starting armies for {n2} countries ({GeneratedFiles["starting_armies"]})";
    }

    /// <summary>Loads the project's map to find coasts (install steps that need geography).</summary>
    private static Game? LoadMap(string projectDir)
    {
        try { return Game.Load(Path.Combine(projectDir, "game"), compileScripts: false, seed: 1); }
        catch (Exception ex) { Log.Warn($"Could not load the map: {ex.Message}"); return null; }
    }

    /// <summary>Every country with a coast gets a small fleet in its main port (the capital if it is coastal).</summary>
    private static string StartingFleets(string projectDir)
    {
        var g = LoadMap(projectDir);
        if (g == null) return "Starting fleets: the map could not be loaded";
        string game = Path.Combine(projectDir, "game");
        var hasFleets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in TxtFiles(Path.Combine(game, "history", "fleets")))
            foreach (var n in ScriptParser.ParseFile(f).All) hasFleets.Add(n.Key);
        var sb = new System.Text.StringBuilder("# Starting fleets, created when the navy feature was added. Edit freely.\n# TAG = { fleet = { name location ships = { ship_type = count } } }\n# 'location' must be a coastal land location (a port) or a sea location.\n\n");
        int count = 0;
        foreach (var c in g.World.Countries.Where(c => c.Exists && !hasFleets.Contains(c.Tag)))
        {
            var port = g.Navy.MainPort(c); // the capital, or the home port nearest to it
            if (port == null) continue;
            bool big = c.OwnedStates.Count >= 5;
            sb.Append($"{c.Tag} = {{\n\tfleet = {{\n\t\tname = \"Home Fleet\"\n\t\tlocation = {port.Key}\n\t\tships = {{ destroyer = {(big ? 4 : 2)} light_cruiser = 1{(big ? " battleship = 1" : "")} transport = {(big ? 2 : 1)} }}\n\t}}\n}}\n");
            count++;
        }
        if (count == 0) return "Starting fleets: no country without a fleet has a coast";
        string path = Path.Combine(projectDir, GeneratedFiles["starting_fleets"]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        return $"Starting fleets for {count} countries ({GeneratedFiles["starting_fleets"]})";
    }

    /// <summary>Coastal states get a naval base (repairs ships) and a dockyard (builds ships with the production feature).</summary>
    private static string NavalBuildings(string projectDir)
    {
        var g = LoadMap(projectDir);
        if (g == null) return "Naval bases: the map could not be loaded";
        var coastal = g.World.States.Where(s => s.Locations.Any(l => l.IsCoastal)).Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return StateBuildings(projectDir, "naval bases and dockyards", (st, i, n) =>
            coastal.Contains(st.Key) ? new[] { ("naval_base", 1), ("dockyard", st.GetDouble("tax", 2) >= 5 ? 2 : st.GetDouble("tax", 2) >= 3 ? 1 : 0) } : Array.Empty<(string, int)>());
    }

    /// <summary>Groups the map's states into air regions (map/air_regions.txt), unless the file exists.</summary>
    private static string AirRegions(string projectDir)
    {
        string path = Path.Combine(projectDir, GeneratedFiles["starting_air_regions"]);
        if (File.Exists(path)) return "Air regions: map/air_regions.txt already exists";
        var g = LoadMap(projectDir);
        if (g == null) return "Air regions: the map could not be loaded";
        var groups = Burger.Core.Air.AirSystem.Cluster(g, g.World.States.Where(s => s.Locations.Count > 0).ToList());
        var sb = new System.Text.StringBuilder("# Air regions (air feature): air wings are assigned to regions, and air superiority is fought per region.\n# region_key = { name = \"Shown name\" states = { state_keys } }\n# States that are in no region are grouped automatically when the game loads.\n\n");
        int i = 1;
        foreach (var grp in groups)
        {
            var main = grp.OrderByDescending(s => s.Locations.Count).First();
            sb.Append($"air_region_{i} = {{\n\tname = \"{main.DisplayName.Replace("\"", "")} Region\"\n\tstates = {{ {string.Join(' ', grp.Select(s => s.Key))} }}\n}}\n");
            i++;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        return $"{groups.Count} air regions (map/air_regions.txt)";
    }

    /// <summary>Air bases: two levels in every capital's state, one in larger states.</summary>
    private static string AirBases(string projectDir)
    {
        var g = LoadMap(projectDir);
        if (g == null) return "Air bases: the map could not be loaded";
        var capitals = g.World.Countries.Where(c => c.Capital?.State != null).Select(c => c.Capital!.State!.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return StateBuildings(projectDir, "air bases", (st, i, n) =>
            capitals.Contains(st.Key) ? new[] { ("air_base", 2) } : st.GetList("locations").Count >= 6 || st.GetDouble("tax", 2) >= 5 ? new[] { ("air_base", 1) } : Array.Empty<(string, int)>());
    }

    /// <summary>Every country gets a fighter wing at its capital's air base (and a close air support wing if it is large).</summary>
    private static string StartingAir(string projectDir)
    {
        var g = LoadMap(projectDir);
        if (g == null) return "Starting air wings: the map could not be loaded";
        string game = Path.Combine(projectDir, "game");
        var has = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in TxtFiles(Path.Combine(game, "history", "air")))
            foreach (var n in ScriptParser.ParseFile(f).All) has.Add(n.Key);
        var sb = new System.Text.StringBuilder("# Starting air wings, created when the air feature was added. Edit freely.\n# TAG = { wing = { type = plane_type base = state_with_air_base planes = 50 name = \"...\" } }\n\n");
        int count = 0;
        foreach (var c in g.World.Countries.Where(c => c.Exists && c.Capital?.State != null && !has.Contains(c.Tag)))
        {
            string b = c.Capital!.State!.Key;
            sb.Append($"{c.Tag} = {{\n\twing = {{ type = fighter base = {b} planes = 50 }}\n");
            if (c.OwnedStates.Count >= 5) sb.Append($"\twing = {{ type = close_air_support base = {b} planes = 50 }}\n");
            sb.Append("}\n");
            count++;
        }
        if (count == 0) return "Starting air wings: every country already has wings";
        string path = Path.Combine(projectDir, GeneratedFiles["starting_air"]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        return $"Starting air wings for {count} countries ({GeneratedFiles["starting_air"]})";
    }

    /// <summary>Adds buildings to every state in map/states (only buildings the state does not have yet).</summary>
    private static string StateBuildings(string projectDir, string what, Func<Node, int, int, (string Building, int Level)[]> pick)
    {
        string game = Path.Combine(projectDir, "game");
        int states = 0, index = 0;
        foreach (var f in TxtFiles(Path.Combine(game, "map", "states")))
        {
            string text = File.ReadAllText(f);
            string rel = Path.GetRelativePath(game, f).Replace('\\', '/');
            bool changed = false;
            foreach (var st in ScriptParser.ParseFile(f).All)
            {
                if (st.Children == null) continue;
                var have = st.Get("buildings");
                bool any = false;
                foreach (var (b, level) in pick(st, index++, states))
                {
                    if (level <= 0 || have?.Get(b) != null) continue;
                    text = ScriptPatch.SetValue(text, rel, new[] { st.Key, "buildings", b }, level.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    any = changed = true;
                }
                if (any) states++;
            }
            if (changed) File.WriteAllText(f, text);
        }
        return $"Starting {what} in {states} states";
    }

    /// <summary>Countries without gold/manpower get a starting treasury.</summary>
    private static string StartingTreasury(string projectDir)
    {
        string game = Path.Combine(projectDir, "game");
        int n = 0;
        foreach (var f in CountryFiles(game))
        {
            string text = File.ReadAllText(f);
            string rel = Path.GetRelativePath(game, f).Replace('\\', '/');
            foreach (var c in ScriptParser.ParseFile(f).All)
            {
                if (c.Children == null) continue;
                bool changed = false;
                if (c.Get("gold") == null) { text = ScriptPatch.SetValue(text, rel, new[] { c.Key, "gold" }, "100"); changed = true; }
                if (c.Get("manpower") == null) { text = ScriptPatch.SetValue(text, rel, new[] { c.Key, "manpower" }, "10000"); changed = true; }
                if (changed) n++;
            }
            File.WriteAllText(f, text);
        }
        return n == 0 ? "Starting treasury: every country already has gold and manpower" : $"Starting gold and manpower for {n} countries";
    }
}
