using Burger.Core.Events;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Data;

/// <summary>Loads all static definitions (common/, events/, localisation/) into <see cref="GameData"/>.</summary>
public static class DataLoader
{
    private static readonly HashSet<string> TerrainKnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "color", "display_color", "texture", "movement_cost", "combat_width", "attack", "defense",
        "attack_modifier", "defense_modifier", "is_water", "water", "impassable",
    };

    private static readonly HashSet<string> UnitKnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "category", "cost", "manpower", "build_days", "type",
    };

    /// <summary>Enumerates *.txt files of a folder inside the game root, sorted by name (like Paradox games).</summary>
    public static IEnumerable<(string Path, string Display)> Files(GameData data, string folder, string pattern = "*.txt")
    {
        string dir = data.PathOf(folder);
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            yield return (f, Path.GetRelativePath(data.Root, f).Replace('\\', '/'));
    }

    /// <summary>Parses a single file if it exists, or all files of the folder with the same name.</summary>
    public static IEnumerable<Node> ParseFileOrFolder(GameData data, string fileOrFolderWithoutExt)
    {
        string file = data.PathOf(fileOrFolderWithoutExt + ".txt");
        if (File.Exists(file)) yield return ScriptParser.ParseFile(file, fileOrFolderWithoutExt + ".txt");
        foreach (var (path, display) in Files(data, fileOrFolderWithoutExt))
            yield return ScriptParser.ParseFile(path, display);
    }

    public static void LoadDefinitions(Game game)
    {
        var data = game.Data;
        Localisation.Clear();
        Localisation.LoadDirectory(data.PathOf("localisation"), data.Root);

        // game.txt
        string gameFile = data.PathOf("game.txt");
        if (File.Exists(gameFile))
        {
            var g = ScriptParser.ParseFile(gameFile, "game.txt");
            data.Info = new GameInfo
            {
                Raw = g,
                Name = g.GetString("name", data.Info.Name)!,
                Version = g.GetString("version", data.Info.Version)!,
                Author = g.GetString("author", "")!,
                Description = g.GetString("description", "")!,
                DefaultTag = g.GetString("default_country", "")!,
                Icon = g.GetString("icon", "")!,
            };
        }
        else Log.Warn("game.txt not found - using default game name");

        // defines
        var defineRoot = Node.Root("common/defines.txt");
        foreach (var root in ParseFileOrFolder(data, "common/defines"))
            defineRoot.Children!.AddRange(root.All);
        data.Defines.Load(defineRoot);
        Burger.Core.Scripting.Builtins.RegisterStatValues(game);

        LoadTerrain(data);
        LoadEquipment(data);
        LoadBuildings(data);
        LoadShips(data, game.Features.Has(Burger.Core.Modding.FeatureSet.Production));
        LoadPlanes(data, game.Features.Has(Burger.Core.Modding.FeatureSet.Production));
        LoadUnits(data, game.Features.Has(Burger.Core.Modding.FeatureSet.Military));
        LoadTraits(data);
        LoadCasusBelli(data);
        LoadSubjectTypes(data);
        LoadActions(data, "common/diplomatic_actions", ActionTarget.Country, data.DiplomaticActions);
        LoadActions(data, "common/character_interactions", ActionTarget.Character, data.CharacterInteractions);
        LoadOnActions(data);
        LoadNameLists(data);
        EventLoader.LoadEvents(data);
        game.ScriptValues.Load(data);
        game.Modifiers.Load(data);
        game.Resources.Load(data);
        game.Decisions.Load(data);
        game.Interface.Load(data);
    }

    // -----------------------------------------------------------------------------------------------
    private static void LoadTerrain(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/terrain"))
        {
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                if (data.TerrainByKey.ContainsKey(n.Key))
                {
                    Log.Error($"Terrain '{n.Key}' defined twice", n.Location);
                    continue;
                }
                if (data.Terrains.Count >= 255)
                {
                    Log.Error("Too many terrain types (max 255)", n.Location);
                    break;
                }
                var t = new TerrainType
                {
                    Key = n.Key,
                    Index = data.Terrains.Count,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Texture = n.GetString("texture"),
                    MovementCost = n.GetDouble("movement_cost", 1),
                    CombatWidth = n.GetInt("combat_width", 80),
                    AttackModifier = n.GetDouble("attack_modifier", n.GetDouble("attack", 0)),
                    DefenseModifier = n.GetDouble("defense_modifier", n.GetDouble("defense", 0)),
                    IsWater = n.GetBool("is_water", n.GetBool("water", false)),
                    Impassable = n.GetBool("impassable", false),
                    Definition = n,
                };
                if (!Rgb.TryParse(n.Get("color"), out t.MapColor))
                    Log.Error($"Terrain '{n.Key}' needs a color (the color used in terrain.png), e.g. color = {{ 86 124 27 }}", n.Location);
                t.DisplayColor = Rgb.TryParse(n.Get("display_color"), out var dc) ? dc : t.MapColor;
                foreach (var c in n.All)
                    if (c.Value != null && !TerrainKnownKeys.Contains(c.Key) && Node.TryParseDouble(c.Value, out var v))
                        t.Modifiers[c.Key] = v;
                data.Terrains.Add(t);
                data.TerrainByKey[t.Key] = t;
            }
        }
        if (data.Terrains.Count == 0)
        {
            Log.Error("No terrain types defined in common/terrain.txt - adding default 'plains' and 'ocean'");
            var plains = new TerrainType { Key = "plains", Name = "Plains", MapColor = new Rgb(86, 124, 27), DisplayColor = new Rgb(120, 160, 70) };
            var ocean = new TerrainType { Key = "ocean", Index = 1, Name = "Ocean", MapColor = new Rgb(8, 31, 130), DisplayColor = new Rgb(30, 60, 120), IsWater = true };
            data.Terrains.Add(plains);
            data.Terrains.Add(ocean);
            data.TerrainByKey["plains"] = plains;
            data.TerrainByKey["ocean"] = ocean;
        }
    }

    // -----------------------------------------------------------------------------------------------
    private static readonly Dictionary<string, StatAggregation> DefaultAggregation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["max_organisation"] = StatAggregation.Average,
        ["hardness"] = StatAggregation.Average,
        ["morale"] = StatAggregation.Average,
        ["discipline"] = StatAggregation.Average,
        ["piercing"] = StatAggregation.Average,
        ["speed"] = StatAggregation.Min,
        ["armor"] = StatAggregation.Max,
    };

    private static void LoadUnits(GameData data, bool military = true)
    {
        foreach (var kv in DefaultAggregation) data.StatAggregation[kv.Key] = kv.Value;
        var templateNodes = new List<Node>();
        foreach (var root in ParseFileOrFolder(data, "common/units"))
        {
            foreach (var agg in root.GetAll("stat_aggregation"))
                foreach (var c in agg.All)
                {
                    if (c.Value == null) continue;
                    if (Enum.TryParse<StatAggregation>(c.Value, true, out var a)) data.StatAggregation[c.Key] = a;
                    else Log.Error($"Unknown aggregation '{c.Value}' (use sum, average, min or max)", c.Location);
                }
            foreach (var block in root.GetAll("unit_types"))
                foreach (var n in block.All)
                {
                    if (n.Children == null) continue;
                    var u = new UnitType
                    {
                        Key = n.Key,
                        Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                        Category = n.GetString("category", "infantry")!,
                        Cost = n.GetDouble("cost", 10),
                        Manpower = n.GetDouble("manpower", 1000),
                        BuildDays = n.GetInt("build_days", 30),
                        Definition = n,
                    };
                    foreach (var c in n.All)
                        if (c.Value != null && !UnitKnownKeys.Contains(c.Key) && Node.TryParseDouble(c.Value, out var v))
                            u.Stats[c.Key] = v;
                    if (n.Get("equipment") is { } eq)
                        foreach (var e in eq.All)
                            if (e.Value != null && Node.TryParseDouble(e.Value, out var amount)) u.Equipment[e.Key] = amount;
                    if (data.UnitTypes.ContainsKey(u.Key)) Log.Warn($"Unit type '{u.Key}' redefined", n.Location);
                    data.UnitTypes[u.Key] = u;
                }
            foreach (var block in root.GetAll("division_templates"))
                templateNodes.AddRange(block.All.Where(n => n.Children != null));
        }

        foreach (var n in templateNodes)
        {
            var t = new DivisionTemplate
            {
                Key = n.Key,
                Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                OwnerTag = n.GetString("owner", "")!,
            };
            var units = n.Get("units") ?? n.Get("regiments") ?? n.Get("battalions");
            if (units == null)
            {
                Log.Error($"Division template '{n.Key}' needs a units block, e.g. units = {{ infantry = 9 }}", n.Location);
                continue;
            }
            foreach (var u in units.All)
            {
                if (!data.UnitTypes.TryGetValue(u.Key, out var ut))
                {
                    Log.Error($"Unknown unit type '{u.Key}' in template '{n.Key}'", u.Location);
                    continue;
                }
                int count = u.Value != null ? (int)u.AsDouble(1) : 1;
                if (count > 0) t.Units.Add((ut, count));
            }
            if (t.Units.Count == 0)
            {
                Log.Error($"Division template '{n.Key}' has no valid units", n.Location);
                continue;
            }
            AggregateStats(data, t);
            foreach (var c in n.All) // explicit overrides in the template itself
                if (c.Value != null && Node.TryParseDouble(c.Value, out var v) && c.Key is not "name" and not "owner")
                    t.Stats[c.Key] = v;
            data.Templates[t.Key] = t;
        }
        if (data.Templates.Count == 0 && military) Log.Warn("No division templates defined (common/units: division_templates = { ... })");
    }

    public static void AggregateStats(GameData data, DivisionTemplate t)
    {
        t.Stats.Clear();
        var keys = t.Units.SelectMany(u => u.Type.Stats.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int totalCount = t.Units.Sum(u => u.Count);
        foreach (var key in keys)
        {
            var mode = data.StatAggregation.TryGetValue(key, out var m) ? m : StatAggregation.Sum;
            double result = mode switch
            {
                StatAggregation.Average => t.Units.Sum(u => u.Type.Stats.GetValueOrDefault(key) * u.Count) / Math.Max(1, totalCount),
                StatAggregation.Min => t.Units.Where(u => u.Type.Stats.ContainsKey(key)).Select(u => u.Type.Stats[key]).DefaultIfEmpty(0).Min(),
                StatAggregation.Max => t.Units.Select(u => u.Type.Stats.GetValueOrDefault(key)).DefaultIfEmpty(0).Max(),
                _ => t.Units.Sum(u => u.Type.Stats.GetValueOrDefault(key) * u.Count),
            };
            t.Stats[key] = result;
        }
        // equipment: what each unit lists, or the defaults of equipment types for its category
        t.Equipment.Clear();
        foreach (var (type, count) in t.Units)
        {
            if (type.Equipment.Count > 0)
            {
                foreach (var (k, v) in type.Equipment) t.Equipment[k] = t.Equipment.GetValueOrDefault(k) + v * count;
                continue;
            }
            foreach (var eq in data.Equipment.Values)
                if (eq.DefaultFor.Contains(type.Category, StringComparer.OrdinalIgnoreCase))
                    t.Equipment[eq.Key] = t.Equipment.GetValueOrDefault(eq.Key) + eq.DefaultAmount * count;
        }
        t.Cost = t.Units.Sum(u => u.Type.Cost * u.Count);
        t.Manpower = t.Units.Sum(u => u.Type.Manpower * u.Count);
        t.BuildDays = t.Units.Max(u => u.Type.BuildDays);
        if (!t.Stats.ContainsKey("max_strength")) t.Stats["max_strength"] = Math.Max(1, t.Manpower / 100.0);
        if (!t.Stats.ContainsKey("max_organisation")) t.Stats["max_organisation"] = 50;
        if (!t.Stats.ContainsKey("speed")) t.Stats["speed"] = 4;
        if (!t.Stats.ContainsKey("combat_width")) t.Stats["combat_width"] = totalCount * 2;
    }

    // -----------------------------------------------------------------------------------------------
    private static void LoadEquipment(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/equipment"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var e = new EquipmentType
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Cost = Math.Max(0.01, n.GetDouble("cost", 1)),
                    Category = n.GetString("category", "army")!,
                    DefaultFor = n.Get("default_for")?.Items().ToList() ?? new List<string>(),
                    DefaultAmount = n.GetDouble("default_amount", 100),
                    Definition = n,
                };
                foreach (var c in n.All)
                    if (c.Value != null && c.Key is not ("name" or "cost" or "category" or "default_amount") && Node.TryParseDouble(c.Value, out var v))
                        e.Stats[c.Key] = v;
                if (data.Equipment.ContainsKey(e.Key)) Log.Warn($"Equipment '{e.Key}' redefined", n.Location);
                data.Equipment[e.Key] = e;
            }
    }

    /// <summary>Ship classes (navy feature). With production, every ship class is also naval equipment built in dockyards.</summary>
    private static void LoadShips(GameData data, bool production)
    {
        foreach (var root in ParseFileOrFolder(data, "common/ships"))
            foreach (var block in root.GetAll("ship_types"))
                foreach (var n in block.All)
                {
                    if (n.Children == null) continue;
                    var t = new ShipType
                    {
                        Key = n.Key,
                        Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                        Category = n.GetString("category", "screen")!,
                        Hp = Math.Max(1, n.GetDouble("hp", 20)),
                        Speed = Math.Max(1, n.GetDouble("speed", 25)),
                        Cost = n.GetDouble("cost", 50),
                        ProductionCost = Math.Max(1, n.GetDouble("production_cost", 500)),
                        Manpower = n.GetDouble("manpower", 300),
                        BuildDays = n.GetInt("build_days", 180),
                        Definition = n,
                    };
                    foreach (var c in n.All)
                        if (c.Value != null && Node.TryParseDouble(c.Value, out var v) && c.Key is not ("name" or "category" or "cost" or "production_cost" or "manpower" or "build_days"))
                            t.Stats[c.Key] = v;
                    if (data.ShipTypes.ContainsKey(t.Key)) Log.Warn($"Ship type '{t.Key}' redefined", n.Location);
                    data.ShipTypes[t.Key] = t;
                    if (production && !data.Equipment.ContainsKey(t.Key))
                        data.Equipment[t.Key] = new EquipmentType { Key = t.Key, Name = t.Name, Cost = t.ProductionCost, Category = "naval", Definition = n };
                }
    }

    private static void LoadPlanes(GameData data, bool production)
    {
        foreach (var root in ParseFileOrFolder(data, "common/planes"))
            foreach (var block in root.GetAll("plane_types"))
                foreach (var n in block.All)
                {
                    if (n.Children == null) continue;
                    var t = new PlaneType
                    {
                        Key = n.Key,
                        Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                        Category = n.GetString("category", "fighter")!,
                        Range = Math.Max(1, n.GetDouble("range", 600)),
                        Cost = n.GetDouble("cost", 60),
                        ProductionCost = Math.Max(0.01, n.GetDouble("production_cost", 20)),
                        Manpower = n.GetDouble("manpower", 200),
                        BuildDays = n.GetInt("build_days", 90),
                        Definition = n,
                    };
                    foreach (var c in n.All)
                        if (c.Value != null && Node.TryParseDouble(c.Value, out var v) && c.Key is not ("name" or "category" or "cost" or "production_cost" or "manpower" or "build_days" or "range"))
                            t.Stats[c.Key] = v;
                    if (data.PlaneTypes.ContainsKey(t.Key)) Log.Warn($"Plane type '{t.Key}' redefined", n.Location);
                    data.PlaneTypes[t.Key] = t;
                    if (production && !data.Equipment.ContainsKey(t.Key))
                        data.Equipment[t.Key] = new EquipmentType { Key = t.Key, Name = t.Name, Cost = t.ProductionCost, Category = "air", Definition = n };
                }
    }

    private static void LoadBuildings(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/buildings"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var b = new BuildingType
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Description = n.GetString("desc", "")!,
                    Cost = Math.Max(1, n.GetDouble("cost", 5000)),
                    MaxLevel = n.GetInt("max_level", 10),
                    Category = n.GetString("category", "other")!,
                    SharesSlots = n.GetBool("shares_slots"),
                    Potential = n.Get("potential"),
                    Definition = n,
                };
                foreach (var c in n.All)
                    if (c.Value != null && c.Key is not ("name" or "desc" or "cost" or "max_level" or "category" or "shares_slots") && Node.TryParseDouble(c.Value, out var v))
                        b.Stats[c.Key] = v;
                if (data.BuildingTypes.ContainsKey(b.Key)) Log.Warn($"Building '{b.Key}' redefined", n.Location);
                data.BuildingTypes[b.Key] = b;
            }
    }

    // -----------------------------------------------------------------------------------------------
    private static void LoadTraits(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/traits"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var t = new Trait
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Description = n.GetString("desc", "")!,
                    Category = n.GetString("category", "personality")!,
                    Opposites = n.GetList("opposites"),
                    RandomWeight = n.GetDouble("random_weight", 0),
                    InheritChance = n.GetDouble("inherit_chance", 0),
                    Definition = n,
                };
                foreach (var c in n.All)
                    if (c.Value != null && c.Key is not ("name" or "desc" or "category" or "random_weight" or "inherit_chance")
                        && Node.TryParseDouble(c.Value, out var v))
                        t.Modifiers[c.Key] = v;
                data.Traits[t.Key] = t;
            }
    }

    private static void LoadCasusBelli(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/casus_belli"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                data.CasusBelli[n.Key] = new CasusBelli
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Allow = n.Get("allow"),
                    PeaceCostMultiplier = n.GetDouble("peace_cost_multiplier", 1),
                    OthersOpinion = n.GetDouble("others_opinion", 0),
                    AiWeight = n.GetDouble("ai_weight", 1),
                    Definition = n,
                };
            }
        if (data.CasusBelli.Count == 0)
        {
            data.CasusBelli["no_cb"] = new CasusBelli { Key = "no_cb", Name = "No Casus Belli", OthersOpinion = -20 };
        }
    }

    private static void LoadSubjectTypes(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/subject_types"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                data.SubjectTypes[n.Key] = new SubjectType
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    IncomeShare = n.GetDouble("income_share", 0.1),
                    JoinsOverlordWars = n.GetBool("joins_overlord_wars", true),
                    CanDeclareWar = n.GetBool("can_declare_war", false),
                    Definition = n,
                };
            }
        if (data.SubjectTypes.Count == 0)
            data.SubjectTypes["vassal"] = new SubjectType { Key = "vassal", Name = "Vassal" };
    }

    private static void LoadActions(GameData data, string folder, ActionTarget target, Dictionary<string, ActionDef> into)
    {
        foreach (var root in ParseFileOrFolder(data, folder))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var a = new ActionDef
                {
                    Key = n.Key,
                    TargetType = target,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Category = n.GetString("category", "")!,
                    Potential = n.Get("potential"),
                    Allow = n.Get("allow"),
                    RequiresAcceptance = n.GetBool("requires_acceptance", false),
                    AiAcceptance = n.Get("ai_acceptance"),
                    OnAccept = n.Get("on_accept") ?? n.Get("effect"),
                    OnDecline = n.Get("on_decline"),
                    AiWillDo = n.Get("ai_will_do"),
                    CooldownDays = n.GetInt("cooldown_days", 0),
                    CostGold = n.GetDouble("cost", 0),
                    Dialog = n.GetString("dialog"),
                    File = n.File,
                    Line = n.Line,
                };
                if (into.ContainsKey(a.Key)) Log.Warn($"'{a.Key}' redefined (the later definition wins)", n.Location);
                into[a.Key] = a;
            }
    }

    private static void LoadOnActions(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/on_actions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                if (!data.OnActions.TryGetValue(n.Key, out var oa))
                {
                    oa = new OnAction { Key = n.Key };
                    data.OnActions[n.Key] = oa;
                }
                foreach (var e in n.GetAll("effect")) oa.Effects.Add(e);
                oa.Events.AddRange(n.GetList("events"));
                foreach (var re in n.GetAll("random_events"))
                    foreach (var c in re.All)
                    {
                        if (c.Value == null || !Node.TryParseDouble(c.Key, out var w))
                        {
                            Log.Error("random_events entries look like  50 = my_event.1  (use 0 for 'nothing')", c.Location);
                            continue;
                        }
                        oa.RandomEvents.Add((w, c.Value));
                    }
            }
    }

    private static void LoadNameLists(GameData data)
    {
        foreach (var root in ParseFileOrFolder(data, "common/name_lists"))
        {
            var d = root.Get("default") ?? root;
            data.DefaultNames.Male.AddRange(d.GetList("male"));
            data.DefaultNames.Female.AddRange(d.GetList("female"));
            data.DefaultNames.Family.AddRange(d.GetList("family"));
        }
        if (data.DefaultNames.Male.Count == 0) data.DefaultNames.Male.AddRange(new[] { "Marcus", "Lucius", "Gaius", "Titus", "Johann", "Friedrich", "Henri", "Louis", "William", "Karl" });
        if (data.DefaultNames.Female.Count == 0) data.DefaultNames.Female.AddRange(new[] { "Julia", "Claudia", "Anna", "Maria", "Sophie", "Elisabeth", "Marie", "Charlotte" });
        if (data.DefaultNames.Family.Count == 0) data.DefaultNames.Family.AddRange(new[] { "Cornelius", "Valerius", "Becker", "Schmidt", "Dubois", "Martin", "Smith", "Hoffmann" });
    }
}
