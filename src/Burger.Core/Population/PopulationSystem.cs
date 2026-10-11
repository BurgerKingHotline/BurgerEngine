using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Population;

/// <summary>
/// Population (Imperator style): every state has pops of a culture (and, with their features, a religion and a social
/// class) that grow up to the state's capacity, assimilate, convert and migrate. People scale a state's tax and manpower.
/// Also cores and claims (cores_claims), colonization of unowned land (colonization) and buildings that change pops.
/// </summary>
public sealed partial class PopulationSystem
{
    private readonly Game _game;
    public readonly List<CultureGroup> CultureGroups = new();
    public readonly Dictionary<string, CultureDef> CultureByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<ReligionGroup> ReligionGroups = new();
    public readonly Dictionary<string, ReligionDef> ReligionByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<PopTypeDef> PopTypes = new();
    public readonly Dictionary<string, PopTypeDef> PopTypeByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<State, StatePopulation> _states = new();
    private readonly Dictionary<Country, CountryPopulation> _countries = new();
    public readonly List<GoldProject> GoldProjects = new();

    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public int CoresGained, CoresLost, ClaimsMade, ColoniesFounded, BuildingsBuilt;

    public PopulationSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "population", Write = Save, Read = LoadSave, Reset = ResetRuntime });
        RegisterScript();
    }

    private PopulationRules Rules => _game.Rules.Population;

    public bool Enabled => _game.Features.Has(FeatureSet.Population);
    public bool ReligionEnabled => Enabled && _game.Features.Has(FeatureSet.Religion) && ReligionByKey.Count > 0;
    public bool ClassesEnabled => Enabled && _game.Features.Has(FeatureSet.SocialClasses) && PopTypes.Count > 0;
    public bool CoresEnabled => Enabled && _game.Features.Has(FeatureSet.CoresClaims);
    public bool ColonizationEnabled => Enabled && _game.Features.Has(FeatureSet.Colonization);
    public bool IndependenceEnabled => Enabled && _game.Features.Has(FeatureSet.Independence);

    public CultureDef? Culture(string? key) => key != null && CultureByKey.TryGetValue(key, out var c) ? c : null;
    public ReligionDef? Religion(string? key) => key != null && ReligionByKey.TryGetValue(key, out var r) ? r : null;

    // =============================================================================================
    // Definitions
    // =============================================================================================

    public void Load(GameData data)
    {
        CultureGroups.Clear(); CultureByKey.Clear(); ReligionGroups.Clear(); ReligionByKey.Clear(); PopTypes.Clear(); PopTypeByKey.Clear();
        _buildingMods.Clear(); _buildingCountryMods.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/cultures"))
            foreach (var g in root.All)
            {
                if (g.Children == null) continue;
                var grp = new CultureGroup { Key = g.Key, Name = g.GetString("name") ?? Localisation.GetOrPretty(g.Key), Color = Color(g, g.Key) };
                foreach (var n in g.All)
                {
                    if (n.Children == null || n.Key is "color") continue;
                    var c = new CultureDef { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Group = grp, Religion = n.GetString("religion"), CountryName = n.GetString("country_name") };
                    c.Color = Rgb.TryParse(n.Get("color"), out var col) ? col : grp.Color.Lerp(HashColor(n.Key), 0.35);
                    if (CultureByKey.ContainsKey(c.Key)) { Log.Error($"Culture '{c.Key}' is defined twice", n.Location); continue; }
                    grp.Cultures.Add(c);
                    CultureByKey[c.Key] = c;
                }
                CultureGroups.Add(grp);
            }
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/religions"))
            foreach (var g in root.All)
            {
                if (g.Children == null) continue;
                var grp = new ReligionGroup { Key = g.Key, Name = g.GetString("name") ?? Localisation.GetOrPretty(g.Key), Color = Color(g, g.Key) };
                foreach (var n in g.All)
                {
                    if (n.Children == null || n.Key is "color") continue;
                    var r = new ReligionDef { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Group = grp };
                    r.Color = Rgb.TryParse(n.Get("color"), out var col) ? col : grp.Color.Lerp(HashColor(n.Key), 0.3);
                    if (ReligionByKey.ContainsKey(r.Key)) { Log.Error($"Religion '{r.Key}' is defined twice", n.Location); continue; }
                    grp.Religions.Add(r);
                    ReligionByKey[r.Key] = r;
                }
                ReligionGroups.Add(grp);
            }
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/pop_types"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var t = new PopTypeDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Share = n.GetDouble("share", 0),
                    Growth = n.GetDouble("growth", 0),
                    PromoteTo = n.GetString("promote_to"),
                    PromoteRate = n.GetDouble("promote_rate", 0),
                    Order = PopTypes.Count,
                };
                if (Rgb.TryParse(n.Get("color"), out var col)) t.Color = col;
                foreach (var m in n.Get("modifier")?.All ?? Enumerable.Empty<Node>())
                    if (m.Value != null && Node.TryParseDouble(m.Value, out var v)) t.Modifier[m.Key] = v;
                PopTypes.Add(t);
                PopTypeByKey[t.Key] = t;
            }
        foreach (var t in PopTypes)
            if (t.PromoteTo != null && !PopTypeByKey.ContainsKey(t.PromoteTo)) Log.Warn($"Social class '{t.Key}' promotes to unknown class '{t.PromoteTo}'");
        foreach (var c in CultureByKey.Values)
            if (c.Religion != null && ReligionByKey.Count > 0 && !ReligionByKey.ContainsKey(c.Religion)) Log.Warn($"Culture '{c.Key}' has unknown religion '{c.Religion}'");
        // building modifiers (any building may have modifier = { ... } for its state and country_modifier = { ... } for its owner, per level)
        foreach (var b in data.BuildingTypes.Values)
        {
            var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in b.Definition.Get("modifier")?.All ?? Enumerable.Empty<Node>()) if (x.Value != null && Node.TryParseDouble(x.Value, out var v)) m[x.Key] = v;
            if (m.Count > 0) _buildingMods[b.Key] = m;
            var cm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in b.Definition.Get("country_modifier")?.All ?? Enumerable.Empty<Node>()) if (x.Value != null && Node.TryParseDouble(x.Value, out var v)) cm[x.Key] = v;
            if (cm.Count > 0) _buildingCountryMods[b.Key] = cm;
        }
        foreach (var k in _buildingMods.Values.SelectMany(d => d.Keys).Concat(_buildingCountryMods.Values.SelectMany(d => d.Keys))) _modifierKeys.Add(k);
        _modifierKeys.Add("stability_target");
    }

    private static Rgb Color(Node n, string key) => Rgb.TryParse(n.Get("color"), out var c) ? c : HashColor(key);

    private static Rgb HashColor(string key)
    {
        int h = 17;
        foreach (char ch in key) h = h * 31 + ch;
        return Rgb.FromHsv((Math.Abs(h) % 1000) / 1000.0, 0.55, 0.8);
    }

    // =============================================================================================
    // World setup
    // =============================================================================================

    public StatePopulation Of(State s)
    {
        if (!_states.TryGetValue(s, out var sp)) _states[s] = sp = new StatePopulation { BaseTax = s.Tax, BaseManpower = s.Manpower, Owner = s.Owner, OwnedSinceDay = _game.World.Date.TotalDays };
        return sp;
    }

    public CountryPopulation Of(Country c)
    {
        if (!_countries.TryGetValue(c, out var cp))
        {
            _countries[c] = cp = new CountryPopulation();
            // countries made during the game (civil war rebels) share the culture of the country they split from
            if (c.Origin != null && _game.World.GetCountry(c.Origin) is { } o && o != c && _countries.TryGetValue(o, out var op))
            {
                cp.Culture = op.Culture;
                cp.Religion = op.Religion;
                cp.Accepted.UnionWith(op.Accepted);
            }
        }
        return cp;
    }

    public bool HasPops(State s) => _states.TryGetValue(s, out var sp) && sp.Pops.Count > 0;

    /// <summary>Sets up cultures, religions and pops of the world (after states, countries and history are loaded).</summary>
    public void InitWorld()
    {
        _states.Clear();
        _countries.Clear();
        GoldProjects.Clear();
        if (!Enabled) return;
        var w = _game.World;
        // a culture for every country that has none (games without common/cultures)
        foreach (var c in w.Countries)
        {
            var cp = Of(c);
            cp.Culture = c.Definition.GetString("culture");
            cp.Religion = c.Definition.GetString("religion");
            foreach (var a in c.Definition.GetList("accepted_cultures")) cp.Accepted.Add(a);
            if (cp.Culture != null && !CultureByKey.ContainsKey(cp.Culture)) { Log.Error($"{c.Tag}: unknown culture '{cp.Culture}' (common/cultures)", c.Definition.Location); cp.Culture = null; }
            if (cp.Religion != null && !ReligionByKey.ContainsKey(cp.Religion)) { if (ReligionEnabled) Log.Error($"{c.Tag}: unknown religion '{cp.Religion}' (common/religions)", c.Definition.Location); cp.Religion = null; }
        }
        // history/pops
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/pops"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var s = w.GetState(n.Key);
                if (s == null) { Log.Error($"history/pops: unknown state '{n.Key}'", n.Location); continue; }
                var sp = Of(s);
                foreach (var pn in n.GetAll("pop"))
                {
                    string culture = pn.GetString("culture") ?? "";
                    if (!CultureByKey.ContainsKey(culture)) { Log.ErrorOnce("popcult:" + culture, $"history/pops: unknown culture '{culture}'", pn.Location); continue; }
                    string? religion = pn.GetString("religion");
                    if (religion != null && !ReligionByKey.ContainsKey(religion)) { if (ReligionByKey.Count > 0) Log.ErrorOnce("poprel:" + religion, $"history/pops: unknown religion '{religion}'", pn.Location); religion = null; }
                    string? type = pn.GetString("type");
                    if (type != null && !PopTypeByKey.ContainsKey(type)) { if (PopTypes.Count > 0) Log.ErrorOnce("poptype:" + type, $"history/pops: unknown social class '{type}'", pn.Location); type = null; }
                    double size = pn.GetDouble("size");
                    if (size > 0) sp.Pops.Add(new Pop { Culture = culture, Religion = religion, Type = type, Size = size });
                }
                if (n.Has("owned_since") && GameDate.TryParse(n.GetString("owned_since"), out var since)) sp.OwnedSinceDay = since.TotalDays;
            }
        // countries without a culture: the most common culture of their states, else their own new culture
        foreach (var c in w.Countries)
        {
            var cp = Of(c);
            if (cp.Culture == null)
            {
                var major = c.OwnedStates.Where(HasPops).SelectMany(s => Of(s).Pops).GroupBy(p => p.Culture).MaxBy(g => g.Sum(p => p.Size))?.Key;
                cp.Culture = major ?? AutoCulture(c).Key;
            }
        }
        // states without pops: people of the owner's culture, as many as the state's manpower suggests
        foreach (var s in w.States)
        {
            var sp = Of(s);
            if (sp.Pops.Count > 0 || s.Locations.All(l => !l.IsLand)) continue;
            string culture = s.Owner != null ? Of(s.Owner).Culture! : NativeCulture(s).Key;
            double size = Rules.StartingPops(_game, s);
            if (size > 0) sp.Pops.Add(new Pop { Culture = culture, Size = size });
        }
        // religions: the country's state religion is the most common faith of its people (or of its culture)
        if (ReligionByKey.Count > 0)
        {
            string fallback = ReligionByKey.Keys.First();
            foreach (var s in w.States)
                foreach (var p in Of(s).Pops)
                    p.Religion ??= Culture(p.Culture)?.Religion is { } cr && ReligionByKey.ContainsKey(cr) ? cr : (s.Owner != null ? Of(s.Owner).Religion : null) ?? fallback;
            foreach (var c in w.Countries)
            {
                var cp = Of(c);
                cp.Religion ??= c.OwnedStates.SelectMany(s => Of(s).Pops).GroupBy(p => p.Religion).MaxBy(g => g.Sum(p => p.Size))?.Key
                                ?? Culture(cp.Culture)?.Religion ?? fallback;
                if (!ReligionByKey.ContainsKey(cp.Religion)) cp.Religion = fallback;
            }
        }
        if (!ReligionEnabled) foreach (var s in w.States) foreach (var p in Of(s).Pops) p.Religion = null;
        // social classes: split pops by the classes' shares
        if (ClassesEnabled)
        {
            double shareSum = PopTypes.Sum(t => t.Share);
            if (shareSum <= 0) shareSum = 1;
            foreach (var s in w.States)
            {
                var sp = Of(s);
                var split = new List<Pop>();
                foreach (var p in sp.Pops)
                {
                    if (p.Type != null) { split.Add(p); continue; }
                    foreach (var t in PopTypes.Where(t => t.Share > 0))
                        split.Add(new Pop { Culture = p.Culture, Religion = p.Religion, Type = t.Key, Size = p.Size * t.Share / shareSum });
                    if (PopTypes.All(t => t.Share <= 0)) split.Add(new Pop { Culture = p.Culture, Religion = p.Religion, Type = PopTypes[0].Key, Size = p.Size });
                }
                sp.Pops = split;
            }
        }
        else foreach (var s in w.States) foreach (var p in Of(s).Pops) p.Type = null;
        foreach (var s in w.States)
        {
            var sp = Of(s);
            Merge(sp);
            sp.InitialTotal = sp.Total;
            sp.BaseTax = s.Tax;
            sp.BaseManpower = s.Manpower;
            sp.Owner = s.Owner;
            ApplyOutput(s, sp);
        }
    }

    private CultureDef AutoCulture(Country c)
    {
        string key = c.Tag.ToLowerInvariant() + "_culture";
        if (CultureByKey.TryGetValue(key, out var existing)) return existing;
        var grp = CultureGroups.FirstOrDefault(g => g.Key == "auto_cultures");
        if (grp == null) { grp = new CultureGroup { Key = "auto_cultures", Name = "Peoples", Color = new Rgb(150, 150, 150) }; CultureGroups.Add(grp); }
        var cd = new CultureDef { Key = key, Name = c.Name, Group = grp, Color = c.Color };
        grp.Cultures.Add(cd);
        CultureByKey[key] = cd;
        return cd;
    }

    /// <summary>Culture of the people of an unowned state without pops (a 'natives' culture).</summary>
    private CultureDef NativeCulture(State s)
    {
        if (CultureByKey.TryGetValue("natives", out var n)) return n;
        var grp = CultureGroups.FirstOrDefault(g => g.Key == "auto_cultures");
        if (grp == null) { grp = new CultureGroup { Key = "auto_cultures", Name = "Peoples", Color = new Rgb(150, 150, 150) }; CultureGroups.Add(grp); }
        var cd = new CultureDef { Key = "natives", Name = "Natives", Group = grp, Color = new Rgb(120, 100, 80) };
        grp.Cultures.Add(cd);
        CultureByKey[cd.Key] = cd;
        return cd;
    }

    private static void Merge(StatePopulation sp)
    {
        var merged = new List<Pop>();
        foreach (var p in sp.Pops)
        {
            if (p.Size < 0.5) continue;
            var same = merged.FirstOrDefault(m => m.Same(p.Culture, p.Religion, p.Type));
            if (same != null) same.Size += p.Size; else merged.Add(p);
        }
        sp.Pops = merged;
    }

    // =============================================================================================
    // Questions
    // =============================================================================================

    /// <summary>1.2M, 340k, 900 ...</summary>
    public static string Format(double n) => n >= 1e9 ? $"{n / 1e9:0.##}B" : n >= 1e6 ? $"{n / 1e6:0.##}M" : n >= 1e4 ? $"{n / 1e3:0}k" : n >= 1e3 ? $"{n / 1e3:0.#}k" : $"{n:0}";

    public double TotalOf(State s) => _states.TryGetValue(s, out var sp) ? sp.Total : 0;
    public double TotalOf(Country c) => c.OwnedStates.Sum(TotalOf);

    public bool IsAccepted(Country c, string culture) => Rules.IsAccepted(_game, c, culture);

    /// <summary>The biggest culture of a state (null without pops).</summary>
    public string? MajorityCulture(State s)
    {
        CheckMajorityCache();
        if (_majorCulture.TryGetValue(s, out var m)) return m;
        m = _states.TryGetValue(s, out var sp) && sp.Pops.Count > 0 ? sp.Pops.GroupBy(p => p.Culture).MaxBy(g => g.Sum(p => p.Size))!.Key : null;
        _majorCulture[s] = m;
        return m;
    }

    public string? MajorityReligion(State s)
    {
        CheckMajorityCache();
        if (_majorReligion.TryGetValue(s, out var m)) return m;
        m = _states.TryGetValue(s, out var sp) && sp.Pops.Count > 0 ? sp.Pops.Where(p => p.Religion != null).GroupBy(p => p.Religion!).MaxBy(g => g.Sum(p => p.Size))?.Key : null;
        _majorReligion[s] = m;
        return m;
    }

    // majorities are asked for a lot (war goals, AI); they are cached for the day
    private readonly Dictionary<State, string?> _majorCulture = new(), _majorReligion = new();
    private int _majorityDay = int.MinValue;

    private void CheckMajorityCache()
    {
        int today = _game.World.Date.TotalDays;
        if (today == _majorityDay) return;
        _majorCulture.Clear();
        _majorReligion.Clear();
        _majorityDay = today;
    }

    /// <summary>Forget cached majorities (after pops were changed outside the monthly update).</summary>
    public void InvalidateMajorities() => _majorityDay = int.MinValue;

    /// <summary>People per culture (or religion, or class) in a state or a whole country, biggest first.</summary>
    public List<(string Key, double Size)> Breakdown(IEnumerable<State> states, Func<Pop, string?> by)
        => states.Where(HasPops).SelectMany(s => Of(s).Pops).Where(p => by(p) != null).GroupBy(p => by(p)!)
                 .Select(g => (g.Key, g.Sum(p => p.Size))).OrderByDescending(x => x.Item2).ToList();

    /// <summary>The country's states that are not colonies (its home and conquered land): their people make up the
    /// culture and religion figures, stability and what cultures can be accepted.</summary>
    public IEnumerable<State> HomeStates(Country c) => c.OwnedStates.Where(s => !IsColony(s));

    /// <summary>Share of a country's people (outside its colonies) of accepted cultures (and, with religion, of its state religion).</summary>
    public (double Accepted, double Faithful) Shares(Country c)
    {
        double total = 0, acc = 0, faith = 0;
        var rel = Of(c).Religion;
        foreach (var s in HomeStates(c))
        {
            if (!_states.TryGetValue(s, out var sp)) continue;
            foreach (var p in sp.Pops)
            {
                total += p.Size;
                if (IsAccepted(c, p.Culture)) acc += p.Size;
                if (!ReligionEnabled || p.Religion == rel) faith += p.Size;
            }
        }
        return total <= 0 ? (1, 1) : (acc / total, faith / total);
    }

    public double Capacity(State s) => Rules.Capacity(_game, s, Of(s));

    /// <summary>How full a state is: its people compared to its capacity.</summary>
    public double FillOf(State s) => _states.TryGetValue(s, out var sp) ? sp.Total / Math.Max(1, Rules.Capacity(_game, s, sp)) : 0;

    /// <summary>Opinion between countries because of their religions (religion feature).</summary>
    public double ReligionOpinion(Country from, Country to)
    {
        if (!ReligionEnabled || from == to) return 0;
        return Rules.ReligionOpinion(_game, Religion(Of(from).Religion), Religion(Of(to).Religion));
    }

    // =============================================================================================
    // Monthly simulation
    // =============================================================================================

    public void MonthlyTick()
    {
        if (!Enabled) return;
        InvalidateMajorities();
        var w = _game.World;
        foreach (var s in w.States)
        {
            if (!_states.TryGetValue(s, out var sp) || sp.Pops.Count == 0) continue;
            if (sp.Owner != s.Owner) { sp.Owner = s.Owner; sp.OwnedSinceDay = w.Date.TotalDays; }
            double before = sp.Total;
            Grow(s, sp);
            Assimilate(s, sp);
            sp.LastGrowth = sp.Total - before;
        }
        Migrate();
        foreach (var s in w.States)
            if (_states.TryGetValue(s, out var sp) && sp.Pops.Count > 0) { Merge(sp); ApplyOutput(s, sp); }
        _countryModsDirty = true;
        if (CoresEnabled) MonthlyCores();
        if (ColonizationEnabled) MonthlyColonies();
        if (IndependenceEnabled) MonthlyIndependence();
        MonthlyGoldProjects();
    }

    private void Grow(State s, StatePopulation sp)
    {
        double total = sp.Total;
        double cap = Rules.Capacity(_game, s, sp);
        double room = 1 - total / Math.Max(1, cap);
        foreach (var p in sp.Pops)
        {
            double rate = Rules.YearlyGrowth(_game, s, p);
            p.Size = Math.Max(0, p.Size * (1 + Rules.MonthlyGrowth(_game, s, p, rate, room)));
        }
    }

    private void Assimilate(State s, StatePopulation sp)
    {
        var owner = s.Owner;
        if (owner == null) return;
        var cp = Of(owner);
        var additions = new List<Pop>();
        foreach (var p in sp.Pops)
        {
            // culture
            if (cp.Culture != null && !IsAccepted(owner, p.Culture))
            {
                double moved = p.Size * Rules.YearlyAssimilation(_game, s, p) / 12;
                if (moved > 0) { p.Size -= moved; additions.Add(new Pop { Culture = cp.Culture, Religion = p.Religion, Type = p.Type, Size = moved }); }
            }
            // religion
            if (ReligionEnabled && cp.Religion != null && p.Religion != cp.Religion)
            {
                double moved = p.Size * Rules.YearlyConversion(_game, s, p) / 12;
                if (moved > 0) { p.Size -= moved; additions.Add(new Pop { Culture = p.Culture, Religion = cp.Religion, Type = p.Type, Size = moved }); }
            }
            // social classes
            if (ClassesEnabled && p.Type != null && PopTypeByKey.TryGetValue(p.Type, out var t) && t.PromoteTo != null && t.PromoteRate > 0)
            {
                double moved = p.Size * Rules.YearlyPromotion(_game, s, p, t) / 12;
                if (moved > 0) { p.Size -= moved; additions.Add(new Pop { Culture = p.Culture, Religion = p.Religion, Type = t.PromoteTo, Size = moved }); }
            }
        }
        sp.Pops.AddRange(additions);
    }

    /// <summary>People leave crowded states for other states (rule MigrationTarget: emptier states of the same country next to them).</summary>
    private void Migrate()
    {
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists || c.OwnedStates.Count < 2) continue;
            foreach (var s in c.OwnedStates)
            {
                if (!_states.TryGetValue(s, out var sp) || sp.Pops.Count == 0) continue;
                double fill = sp.Total / Math.Max(1, Rules.Capacity(_game, s, sp));
                var best = Rules.MigrationTarget(_game, s, fill);
                if (best == null || best == s) continue;
                double share = Math.Min(1, Rules.MonthlyMigration(_game, s, best, fill, FillOf(best)));
                if (share <= 0) continue;
                var target = Of(best);
                foreach (var p in sp.Pops.ToList())
                {
                    double moved = p.Size * share;
                    p.Size -= moved;
                    target.Pops.Add(new Pop { Culture = p.Culture, Religion = p.Religion, Type = p.Type, Size = moved });
                }
            }
        }
    }

    /// <summary>The groups of the given states that touch each other (by land or strait).</summary>
    public List<List<State>> Components(IEnumerable<State> states)
    {
        var left = states.ToHashSet();
        var result = new List<List<State>>();
        while (left.Count > 0)
        {
            var start = left.OrderBy(s => s.Index).First();
            var comp = Reachable(start, left).ToList();
            foreach (var s in comp) left.Remove(s);
            result.Add(comp);
        }
        return result;
    }

    /// <summary>The states among <paramref name="within"/> that can be reached from <paramref name="start"/> through states of the set.</summary>
    public HashSet<State> Reachable(State start, IEnumerable<State> within)
    {
        var set = within as HashSet<State> ?? within.ToHashSet();
        var seen = new HashSet<State>();
        if (!set.Contains(start)) return seen;
        var q = new Queue<State>();
        seen.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
            foreach (var n in NeighbourStates(q.Dequeue()))
                if (set.Contains(n) && seen.Add(n)) q.Enqueue(n);
        return seen;
    }

    private readonly Dictionary<State, List<State>> _neighbours = new();

    public List<State> NeighbourStates(State s)
    {
        if (_neighbours.TryGetValue(s, out var list)) return list;
        var set = new HashSet<State>();
        foreach (var l in s.Locations)
            foreach (var a in l.Neighbors)
                if (a.Kind != AdjacencyKind.Sea && a.To.State is { } o && o != s) set.Add(o);
        return _neighbours[s] = set.OrderBy(x => x.Index).ToList();
    }

    /// <summary>Writes the state's tax and manpower from its base values and its people.</summary>
    private void ApplyOutput(State s, StatePopulation sp)
    {
        // a script changed tax or manpower since we last wrote them: take that as the new base
        if (!double.IsNaN(sp.WrittenTax) && Math.Abs(s.Tax - sp.WrittenTax) > 1e-9) sp.BaseTax = sp.WrittenTax > 0 ? sp.BaseTax * s.Tax / sp.WrittenTax : s.Tax;
        if (!double.IsNaN(sp.WrittenManpower) && Math.Abs(s.Manpower - sp.WrittenManpower) > 1e-9) sp.BaseManpower = sp.WrittenManpower > 0 ? sp.BaseManpower * s.Manpower / sp.WrittenManpower : s.Manpower;
        var (tf, mf) = Rules.OutputFactors(_game, s, sp);
        s.Tax = sp.BaseTax * tf;
        s.Manpower = sp.BaseManpower * mf;
        sp.WrittenTax = s.Tax;
        sp.WrittenManpower = s.Manpower;
    }

    // =============================================================================================
    // Modifiers: buildings (state and country), and unrest from foreign cultures and faiths
    // =============================================================================================

    private readonly Dictionary<string, Dictionary<string, double>> _buildingMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, double>> _buildingCountryMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _modifierKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, Dictionary<string, double>> _countryMods = new();
    private bool _countryModsDirty = true;

    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || !_modifierKeys.Contains(key)) return 0;
        if (scope is State s)
        {
            double v = 0;
            foreach (var kv in _buildingMods)
            {
                if (!kv.Value.TryGetValue(key, out var per)) continue;
                double levels = s.Working(kv.Key);
                if (levels > 0) v += per * levels;
            }
            return v;
        }
        if (scope is Country c)
        {
            if (_countryModsDirty) { _countryMods.Clear(); _countryModsDirty = false; }
            if (!_countryMods.TryGetValue(c, out var m)) _countryMods[c] = m = ComputeCountryModifiers(c);
            return m.GetValueOrDefault(key);
        }
        return 0;
    }

    private Dictionary<string, double> ComputeCountryModifiers(Country c)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in c.OwnedStates)
            foreach (var kv in _buildingCountryMods)
            {
                double levels = s.Working(kv.Key);
                if (levels <= 0) continue;
                foreach (var x in kv.Value) m[x.Key] = m.GetValueOrDefault(x.Key) + x.Value * levels;
            }
        // unrest: people of unaccepted cultures and other faiths lower stability (politics)
        if (c.Exists)
        {
            var (acc, faith) = Shares(c);
            double penalty = Rules.StabilityPenalty(_game, c, acc, faith);
            if (penalty > 0.01) m["stability_target"] = m.GetValueOrDefault("stability_target") - penalty;
        }
        return m;
    }

    /// <summary>Call after buildings, cultures or religions of a country change outside the monthly update.</summary>
    public void Invalidate() => _countryModsDirty = true;

    // =============================================================================================
    // Save
    // =============================================================================================

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void ResetRuntime()
    {
        _states.Clear();
        _countries.Clear();
        GoldProjects.Clear();
        Independence.Clear();
        IndependenceWars.Clear();
        FreedTotal = 0;
        _colonyCountDay = -1;
        InvalidateMajorities();
        _home.Clear();
        _countryModsDirty = true;
    }

    private void Save(Node root)
    {
        if (!Enabled) return;
        foreach (var (c, cp) in _countries)
        {
            if (!c.Exists && !c.Dynamic) continue;
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            if (cp.Culture != null) n.Add("culture", cp.Culture);
            if (cp.Religion != null) n.Add("religion", cp.Religion);
            var acc = n.AddBlock("accepted");
            foreach (var a in cp.Accepted) acc.AddItem(a);
        }
        foreach (var (s, sp) in _states)
        {
            var n = root.AddBlock("state");
            n.Add("key", s.Key);
            n.Add("initial", F(sp.InitialTotal));
            n.Add("base_tax", F(sp.BaseTax));
            n.Add("base_manpower", F(sp.BaseManpower));
            n.Add("owned_since", sp.OwnedSinceDay.ToString(CultureInfo.InvariantCulture));
            if (sp.Owner != null) n.Add("owner", sp.Owner.Tag);
            if (sp.Colonizer != null) { n.Add("colonizer", sp.Colonizer.Tag); n.Add("colony", F(sp.ColonyProgress)); }
            foreach (var p in sp.Pops)
            {
                var pn = n.AddBlock("pop");
                pn.Add("culture", p.Culture);
                if (p.Religion != null) pn.Add("religion", p.Religion);
                if (p.Type != null) pn.Add("type", p.Type);
                pn.Add("size", F(p.Size));
            }
        }
        SaveIndependence(root);
        foreach (var g in GoldProjects)
        {
            var n = root.AddBlock("gold_project");
            n.Add("country", g.Country.Tag);
            n.Add("state", g.State.Key);
            n.Add("building", g.Building.Key);
            n.Add("months", g.MonthsLeft.ToString(CultureInfo.InvariantCulture));
            n.Add("total", g.TotalMonths.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void LoadSave(Node root)
    {
        var w = _game.World;
        foreach (var n in root.GetAll("country"))
        {
            var c = w.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var cp = Of(c);
            cp.Culture = n.GetString("culture");
            if (cp.Culture != null && !CultureByKey.ContainsKey(cp.Culture)) cp.Culture = cp.Culture.EndsWith("_culture") ? AutoCulture(c).Key : null;
            cp.Religion = n.GetString("religion");
            cp.Accepted.Clear();
            foreach (var a in n.GetList("accepted")) cp.Accepted.Add(a);
        }
        foreach (var n in root.GetAll("state"))
        {
            var s = w.GetState(n.GetString("key"));
            if (s == null) continue;
            var sp = new StatePopulation
            {
                InitialTotal = n.GetDouble("initial"),
                BaseTax = n.GetDouble("base_tax", s.Tax),
                BaseManpower = n.GetDouble("base_manpower", s.Manpower),
                OwnedSinceDay = n.GetInt("owned_since", w.Date.TotalDays),
                Owner = w.GetCountry(n.GetString("owner")),
                Colonizer = w.GetCountry(n.GetString("colonizer")),
                ColonyProgress = n.GetDouble("colony"),
                WrittenTax = s.Tax,
                WrittenManpower = s.Manpower,
            };
            foreach (var pn in n.GetAll("pop"))
            {
                string culture = pn.GetString("culture") ?? "";
                if (!CultureByKey.ContainsKey(culture))
                {
                    if (culture == "natives") culture = NativeCulture(s).Key;
                    else if (culture.EndsWith("_culture") && w.GetCountry(culture[..^8]) is { } ac) culture = AutoCulture(ac).Key;
                    else continue;
                }
                sp.Pops.Add(new Pop { Culture = culture, Religion = ReligionEnabled ? pn.GetString("religion") : null, Type = ClassesEnabled ? pn.GetString("type") : null, Size = pn.GetDouble("size") });
            }
            _states[s] = sp;
        }
        foreach (var n in root.GetAll("gold_project"))
        {
            var c = w.GetCountry(n.GetString("country"));
            var s = w.GetState(n.GetString("state"));
            if (c == null || s == null || !_game.Data.BuildingTypes.TryGetValue(n.GetString("building") ?? "", out var b)) continue;
            GoldProjects.Add(new GoldProject { Country = c, State = s, Building = b, MonthsLeft = n.GetInt("months"), TotalMonths = Math.Max(1, n.GetInt("total", 12)) });
        }
        LoadIndependence(root);
        InvalidateMajorities();
        _colonyCountDay = -1;
        _countryModsDirty = true;
    }
}
