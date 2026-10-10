using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Data;

/// <summary>Loads the starting world: countries, the map, states, characters, units and diplomacy.</summary>
public static class WorldLoader
{
    public static void LoadWorld(Game game)
    {
        var data = game.Data;
        var world = game.World;
        world.Date = data.Defines.StartDate;

        LoadCountries(game);
        MapLoader.Load(data, world);
        LoadStates(game);
        ResolveCapitals(game);
        LoadCharacters(game);
        LoadUnits(game);
        LoadDiplomacy(game);
        game.Politics.LoadHistory();
        game.Population.InitWorld();
        game.Warfare.RebuildIndex();
    }

    // ------------------------------------------------------------------------------------------------
    private static void LoadCountries(Game game)
    {
        var data = game.Data;
        var world = game.World;
        var roots = DataLoader.ParseFileOrFolder(data, "common/countries").ToList();
        roots.AddRange(DataLoader.ParseFileOrFolder(data, "common/country_tags"));
        foreach (var root in roots)
        {
            foreach (var n in root.All)
            {
                if (n.Children == null)
                {
                    Log.Error($"Expected  TAG = {{ name = ... color = ... }}  but found '{n.Key}'", n.Location);
                    continue;
                }
                string tag = n.Key.ToUpperInvariant();
                if (!Country.IsValidTag(tag))
                {
                    Log.Error($"'{n.Key}' is not a valid tag. Tags are exactly 3 letters/digits, e.g. GER", n.Location);
                    continue;
                }
                if (world.CountryByTag.ContainsKey(tag))
                {
                    Log.Error($"Tag {tag} is declared twice", n.Location);
                    continue;
                }
                var c = new Country
                {
                    Tag = tag,
                    Index = world.Countries.Count,
                    Definition = n,
                    Gold = n.GetDouble("gold", 100),
                    Manpower = n.GetDouble("manpower", 10000),
                    Playable = n.GetBool("playable", true),
                    Government = n.GetString("government", "")!,
                };
                c.DefaultName = n.GetString("name") ?? (Localisation.Has(tag) ? tag : tag);
                c.Name = c.DefaultName;
                if (!Rgb.TryParse(n.Get("color"), out var col))
                {
                    Log.Warn($"Country {tag} has no color - a random one was chosen", n.Location);
                    col = Rgb.FromHsv((world.Countries.Count * 0.618034) % 1.0, 0.6, 0.8);
                }
                c.Color = c.DefaultColor = col;
                c.DefaultFlagPath = n.GetString("flag") ?? $"gfx/flags/{tag}.png";
                c.FlagPath = c.DefaultFlagPath;
                c.FlagVersion = 0;
                var names = n.Get("names") ?? n.Get("name_list");
                if (names != null)
                {
                    c.Names.Male.AddRange(names.GetList("male"));
                    c.Names.Female.AddRange(names.GetList("female"));
                    c.Names.Family.AddRange(names.GetList("family"));
                }
                foreach (var f in n.GetList("flags")) c.Vars.SetFlag(f);
                world.Countries.Add(c);
                world.CountryByTag[tag] = c;
            }
        }
        if (world.Countries.Count == 0) Log.Warn("No countries defined (common/countries/*.txt)");
    }

    // ------------------------------------------------------------------------------------------------
    private static void LoadStates(Game game)
    {
        var data = game.Data;
        var world = game.World;
        string folder = "map/" + world.Map.Settings.StatesFolder;
        foreach (var (path, display) in DataLoader.Files(data, folder))
        {
            var root = ScriptParser.ParseFile(path, display);
            foreach (var n in root.All)
            {
                if (n.Children == null) { Log.Error($"Expected a state block, found '{n.Key}'", n.Location); continue; }
                string key = string.Equals(n.Key, "state", StringComparison.OrdinalIgnoreCase) ? n.GetString("id") ?? "" : n.Key;
                if (string.IsNullOrWhiteSpace(key)) { Log.Error("State without id (state = { id = my_state ... })", n.Location); continue; }
                if (world.StateByKey.ContainsKey(key)) { Log.Error($"State '{key}' is defined twice", n.Location); continue; }
                var st = new State
                {
                    Key = key,
                    Index = world.States.Count,
                    NameOverride = n.GetString("name"),
                    Manpower = n.GetDouble("manpower", 100),
                    Tax = n.GetDouble("tax", 1),
                    Category = n.GetString("category", "")!,
                    SourceFile = display,
                };
                foreach (var lk in n.GetList("locations").Concat(n.GetList("provinces")))
                {
                    var loc = world.GetLocation(lk);
                    if (loc == null)
                    {
                        // allow hex colors directly in the state file too
                        if (Rgb.TryParseHex(lk, out var col) && world.LocationByColor.TryGetValue(col.Packed, out var byColor)) loc = byColor;
                        else { Log.Error($"State '{key}': unknown location '{lk}'", n.Location); continue; }
                    }
                    if (loc.State != null) { Log.Error($"Location '{loc.Key}' is in both '{loc.State.Key}' and '{key}'", n.Location); continue; }
                    if (loc.IsWater) { Log.Warn($"State '{key}': '{loc.Key}' is water and cannot belong to a state", n.Location); continue; }
                    loc.State = st;
                    st.Locations.Add(loc);
                }
                if (st.Locations.Count == 0) Log.Warn($"State '{key}' has no locations", n.Location);
                var ownerTag = n.GetString("owner");
                if (ownerTag != null)
                {
                    st.Owner = world.GetCountry(ownerTag);
                    if (st.Owner == null) Log.Error($"State '{key}': unknown owner '{ownerTag}'", n.Location);
                }
                foreach (var t in n.GetList("cores").Concat(n.GetList("add_core")))
                {
                    var c = world.GetCountry(t);
                    if (c != null) st.Cores.Add(c); else Log.Error($"State '{key}': unknown core '{t}'", n.Location);
                }
                foreach (var t in n.GetList("claims").Concat(n.GetList("add_claim")))
                {
                    var c = world.GetCountry(t);
                    if (c != null) st.Claims.Add(c); else Log.Error($"State '{key}': unknown claim '{t}'", n.Location);
                }
                var buildings = n.Get("buildings");
                if (buildings != null)
                    foreach (var b in buildings.All)
                        if (b.Value != null) st.Buildings[b.Key] = (int)b.AsDouble(1);
                foreach (var f in n.GetList("flags")) st.Vars.SetFlag(f);
                Country? controller = st.Owner;
                var ctrlTag = n.GetString("controller");
                if (ctrlTag != null) controller = world.GetCountry(ctrlTag) ?? controller;
                foreach (var l in st.Locations) l.Controller = controller;
                world.States.Add(st);
                world.StateByKey[key] = st;
            }
        }
        int unassigned = world.Locations.Count(l => l.IsLand && l.State == null && l.PixelCount > 0);
        if (unassigned > 0)
            Log.Info($"{unassigned} land locations are not part of any state (they are unowned). Use the map editor (F10) to assign them.");
        world.RefreshOwnership();
    }

    private static void ResolveCapitals(Game game)
    {
        var world = game.World;
        foreach (var c in world.Countries)
        {
            var capKey = c.Definition.GetString("capital");
            Location? cap = null;
            if (capKey != null)
            {
                cap = world.GetLocation(capKey) ?? world.GetState(capKey)?.Locations.FirstOrDefault();
                if (cap == null) Log.Error($"Capital '{capKey}' of {c.Tag} is not a location", c.Definition.Location);
            }
            if (cap != null && cap.Owner != c && c.Exists) Log.Warn($"Capital of {c.Tag} ({cap.Key}) is not owned by {c.Tag}", c.Definition.Location);
            c.Capital = cap ?? c.OwnedStates.SelectMany(s => s.Locations).OrderByDescending(l => l.PixelCount).FirstOrDefault();
        }
    }

    // ------------------------------------------------------------------------------------------------
    private static void LoadCharacters(Game game)
    {
        var data = game.Data;
        foreach (var (path, display) in DataLoader.Files(data, "history/characters"))
        {
            var root = ScriptParser.ParseFile(path, display);
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Node def = n;
                if (!string.Equals(n.Key, "character", StringComparison.OrdinalIgnoreCase) && !n.Has("id"))
                {
                    // caesar = { ... }  → id = caesar
                    def = n.Clone();
                    def.Add("id", n.Key);
                }
                game.Characters.CreateFromNode(def, null);
            }
        }
        // rulers / heirs referenced from the country file
        foreach (var c in game.World.Countries)
        {
            var rk = c.Definition.GetString("ruler");
            if (rk != null)
            {
                var r = game.World.GetCharacter(rk);
                if (r == null) Log.Error($"Ruler '{rk}' of {c.Tag} is not a character", c.Definition.Location);
                else { r.Country = c; c.Ruler = r; }
            }
            var hk = c.Definition.GetString("heir");
            if (hk != null)
            {
                var h = game.World.GetCharacter(hk);
                if (h == null) Log.Error($"Heir '{hk}' of {c.Tag} is not a character", c.Definition.Location);
                else { h.Country = c; c.Heir = h; }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------
    private static void LoadUnits(Game game)
    {
        var data = game.Data;
        var world = game.World;
        foreach (var (path, display) in DataLoader.Files(data, "history/units"))
        {
            var root = ScriptParser.ParseFile(path, display);
            foreach (var cn in root.All)
            {
                if (cn.Children == null) continue;
                var country = world.GetCountry(cn.Key);
                if (country == null) { Log.Error($"Unknown country '{cn.Key}' in units file (use  TAG = {{ army = {{ ... }} }})", cn.Location); continue; }
                foreach (var n in cn.All)
                {
                    if (n.Children == null) continue;
                    if (string.Equals(n.Key, "army", StringComparison.OrdinalIgnoreCase))
                    {
                        Character? general = null;
                        var gk = n.GetString("general");
                        if (gk != null)
                        {
                            general = world.GetCharacter(gk);
                            if (general == null) Log.Error($"Unknown general '{gk}'", n.Location);
                        }
                        var army = game.Warfare.CreateArmy(country, n.GetString("name"), general);
                        foreach (var dn in n.GetAll("division"))
                            foreach (var d in CreateDivisions(game, country, dn)) game.Warfare.AssignToArmy(d, army);
                        var front = n.GetString("front");
                        if (front != null) army.FrontEnemy = world.GetCountry(front);
                        if (n.GetBool("offensive")) army.Mode = ArmyMode.Offensive;
                    }
                    else if (string.Equals(n.Key, "division", StringComparison.OrdinalIgnoreCase))
                    {
                        CreateDivisions(game, country, n);
                    }
                    else Log.Error($"Unknown entry '{n.Key}' (use army or division)", n.Location);
                }
            }
        }
    }

    private static List<Division> CreateDivisions(Game game, Country country, Node n)
    {
        var result = new List<Division>();
        string tk = n.GetString("template") ?? "";
        if (!game.Data.Templates.TryGetValue(tk, out var t)) { Log.Error($"Unknown division template '{tk}'", n.Location); return result; }
        var loc = game.World.GetLocation(n.GetString("location")) ?? country.Capital;
        if (loc == null) { Log.Error($"Division needs a valid location", n.Location); return result; }
        if (!loc.IsPassable) { Log.Error($"Location '{loc.Key}' cannot hold units (water/impassable)", n.Location); return result; }
        int count = Math.Max(1, n.GetInt("count", 1));
        for (int i = 0; i < count; i++)
            result.Add(game.Warfare.CreateDivision(country, t, loc, count == 1 ? n.GetString("name") : null));
        return result;
    }

    // ------------------------------------------------------------------------------------------------
    private static void LoadDiplomacy(Game game)
    {
        var w = game.World;
        var R = w.Relations;
        if (!game.Features.Has(Burger.Core.Modding.FeatureSet.Diplomacy)) return; // relations need the diplomacy feature
        foreach (var root in DataLoader.ParseFileOrFolder(game.Data, "history/diplomacy"))
        {
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Country? Get(string key)
                {
                    var tag = n.GetString(key);
                    var c = w.GetCountry(tag);
                    if (c == null) Log.Error($"{n.Key}: unknown country '{tag}' for '{key}'", n.Location);
                    return c;
                }
                switch (n.Key.ToLowerInvariant())
                {
                    case "alliance":
                        { var a = Get("first"); var b = Get("second"); if (a != null && b != null) R.SetAlliance(a, b, true); break; }
                    case "military_access":
                        { var a = Get("granter"); var b = Get("receiver"); if (a != null && b != null) R.SetAccess(a, b, true); break; }
                    case "guarantee":
                        { var a = Get("guarantor"); var b = Get("guaranteed"); if (a != null && b != null) R.SetGuarantee(a, b, true); break; }
                    case "subject":
                        {
                            var o = Get("overlord"); var s = Get("subject");
                            string type = n.GetString("type") ?? game.Data.SubjectTypes.Keys.FirstOrDefault() ?? "";
                            if (!game.Data.SubjectTypes.TryGetValue(type, out var st)) { Log.Error($"Unknown subject type '{type}'", n.Location); break; }
                            if (o != null && s != null) R.SetSubject(s, o, st);
                            break;
                        }
                    case "opinion":
                        {
                            var a = Get("from"); var b = Get("to");
                            if (a != null && b != null) R.AddOpinion(a, b, n.GetString("name") ?? "Historical", n.GetDouble("value"), n.GetDouble("decay", 0));
                            break;
                        }
                    case "truce":
                        {
                            var a = Get("first"); var b = Get("second");
                            if (a != null && b != null) R.AddTruce(a, b, GameDate.Parse(n.GetString("until") ?? "", w.Date.AddYears(5)));
                            break;
                        }
                    case "war":
                        {
                            var a = Get("attacker"); var d = Get("defender");
                            if (a == null || d == null) break;
                            game.Data.CasusBelli.TryGetValue(n.GetString("cb") ?? "", out var cb);
                            var war = game.Diplomacy.DeclareWar(a, d, cb, null, force: true);
                            if (war == null) break;
                            foreach (var t in n.GetList("attackers")) if (w.GetCountry(t) is { } x) game.Diplomacy.JoinWar(x, war, true);
                            foreach (var t in n.GetList("defenders")) if (w.GetCountry(t) is { } x) game.Diplomacy.JoinWar(x, war, false);
                            break;
                        }
                    default:
                        Log.Error($"Unknown diplomacy entry '{n.Key}' (alliance, military_access, guarantee, subject, opinion, truce, war)", n.Location);
                        break;
                }
            }
        }
        // Notifications from history wars are noise at game start
        game.Notifications.Clear();
    }
}
