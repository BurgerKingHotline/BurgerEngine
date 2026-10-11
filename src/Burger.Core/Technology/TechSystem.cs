using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Technology;

/// <summary>A technology from <c>common/technologies/*.txt</c> (technology feature).</summary>
public sealed class TechDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>infantry, armor, artillery, air, naval, industry, doctrine ... (free text; the AI weighs categories).</summary>
    public string Category = "";
    /// <summary>The year it was historically available. Researching it before that costs more.</summary>
    public double Year = 1930;
    /// <summary>Research points.</summary>
    public double Cost = 40;
    public List<string> Requires = new();
    /// <summary>Country modifiers while it is known. Keys ending in <c>_factor</c> change division stats (<c>soft_attack_factor = 0.1</c>); others are ordinary modifiers (production, manpower, research_speed ...).</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What it makes possible (unit types, ships, planes, equipment, buildings): shown in the interface; the things themselves say <c>requires_tech</c>.</summary>
    public List<string> Unlocks = new();
    public int Order;
    public Node Definition = Node.Root("");
}

/// <summary>One research slot of a country.</summary>
public sealed class ResearchSlot
{
    public TechDef Tech = null!;
    public double Progress;
}

/// <summary>
/// Overridable technology rules (the technology feature puts an editable copy in backend/Technology.cs): how many points and
/// slots a country has, what a technology costs and what the AI researches.
/// </summary>
public class TechRules
{
    /// <summary>
    /// How well equipped and trained a country's army is, as a factor added to its attack, defence and breakthrough: 0.35 per point of wealth index
    /// above or below the world's average (1), at most +0.15 and at least -0.5 (a poor country's divisions are half the worth of a rich one's),
    /// plus the modifier army_quality (doctrine, training, discipline). Recomputed monthly.
    /// </summary>
    public virtual double ArmyQuality(Game game, Country c)
    {
        double wealth = game.Politics.Enabled ? game.Politics.WealthIndex(c) : 1;
        return Math.Clamp(game.Data.Defines.Get("army_quality_per_wealth", 0.35) * (wealth - 1), -game.Data.Defines.Get("army_quality_floor", 0.5), 0.15) + game.Modifiers.Sum(c, "army_quality");
    }

    /// <summary>Research points a country gains each month: a base, its factories, and the modifier research_speed.</summary>
    public virtual double MonthlyPoints(Game game, Country c)
    {
        var d = game.Data.Defines;
        double factories = game.Production.Enabled ? game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c) : c.OwnedStates.Sum(s => s.Tax) * 2;
        double pts = d.Get("research_base", 1.0) + factories * d.Get("research_per_factory", 0.06);
        return pts * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "research_speed"));
    }

    /// <summary>Technologies a country researches at the same time: one, more with a big industry (research_slot_factories, 60 and 150) or the modifier research_slots.</summary>
    public virtual int Slots(Game game, Country c)
    {
        var d = game.Data.Defines;
        double factories = game.Production.Enabled ? game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c) : 0;
        int n = 1 + (factories >= d.Get("research_slot_factories", 60) ? 1 : 0) + (factories >= d.Get("research_slot_factories_2", 150) ? 1 : 0);
        return Math.Max(1, n + (int)Math.Round(game.Modifiers.Sum(c, "research_slots")));
    }

    /// <summary>Points a technology costs this country: more than listed when it is researched ahead of its time (research_ahead_penalty a year, 25%).</summary>
    public virtual double Cost(Game game, Country c, TechDef t)
    {
        double ahead = Math.Max(0, t.Year - (game.World.Date.Year + game.World.Date.Month / 12.0));
        return t.Cost * (1 + ahead * game.Data.Defines.Get("research_ahead_penalty", 0.25));
    }

    /// <summary>How many years ahead of its date a technology may be researched (research_ahead_years, 3).</summary>
    public virtual double AheadYears(Game game, Country c) => game.Data.Defines.Get("research_ahead_years", 3);

    /// <summary>Years behind 1930 a country's technology starts at: poor and non-industrial countries lag (history/technologies.txt can say more).</summary>
    public virtual double StartLag(Game game, Country c)
    {
        if (!game.Production.Enabled) return 0;
        int f = game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c);
        return f >= 60 ? 0 : f >= 25 ? 2 : f >= 10 ? 5 : 9;
    }

    /// <summary>What the AI wants to research: older technologies first, its profile's categories more, what it needs.</summary>
    public virtual double AiScore(Game game, Country c, TechDef t)
    {
        double score = 10 - Math.Min(9, Math.Max(0, game.World.Date.Year - t.Year)) * 0.2;
        score *= game.AiProfiles.CategoryWeight(c, t.Category);
        score += game.Rng.NextDouble() * 0.5;
        return score / Math.Max(1, Cost(game, c, t) / 40.0);
    }
}

/// <summary>Technologies, research and what they do to divisions (technology feature).</summary>
public sealed class TechSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, TechDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<Country, HashSet<string>> Known = new();
    public readonly Dictionary<Country, List<ResearchSlot>> Research = new();
    private readonly Dictionary<string, List<(string Key, double Value)>> _byCategory = new();

    public TechSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "technology", Write = Save, Read = Load, Reset = Reset });
    }

    public bool Enabled => _game.Features.Has(FeatureSet.Technology) && Defs.Count > 0;
    private TechRules Rules => _game.Rules.Tech;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Defs.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/technologies"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var t = new TechDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Category = n.GetString("category", "")!,
                    Year = n.GetDouble("year", 1930),
                    Cost = n.GetDouble("cost", 40),
                    Order = Defs.Count,
                    Definition = n,
                };
                foreach (var r in n.GetList("requires")) t.Requires.Add(r);
                foreach (var u in n.GetList("unlocks")) t.Unlocks.Add(u);
                if (n.Get("modifier") is { Children: not null } m)
                    foreach (var kv in m.All) if (kv.Value != null) t.Modifier[kv.Key] = kv.AsDouble(0);
                if (Defs.ContainsKey(t.Key)) Log.Warn($"Technology '{t.Key}' redefined", n.Location);
                Defs[t.Key] = t;
            }
        foreach (var t in Defs.Values)
            foreach (var r in t.Requires)
                if (!Defs.ContainsKey(r)) Log.Error($"Technology '{t.Key}' requires unknown technology '{r}'", t.Definition.Location);
    }

    // ------------------------------------------------------------------------------------- questions
    public bool Has(Country c, string tech) => Known.TryGetValue(c, out var set) && set.Contains(tech);
    public int Count(Country c) => Known.TryGetValue(c, out var set) ? set.Count : 0;
    public IEnumerable<ResearchSlot> SlotsOf(Country c) => Research.TryGetValue(c, out var l) ? l : Enumerable.Empty<ResearchSlot>();

    /// <summary>A thing from the game files (unit type, ship, plane, equipment, building) may be used: every <c>requires_tech</c> is known, and its potential trigger holds.</summary>
    public bool CanUse(Country c, Node definition)
    {
        if (!Enabled) return true;
        foreach (var t in definition.GetList("requires_tech")) if (!Has(c, t)) return false;
        return true;
    }

    /// <summary>Technologies a country could research now: not known, requirements known, not too far ahead of their date.</summary>
    public IEnumerable<TechDef> Available(Country c)
    {
        double year = _game.World.Date.Year + _game.World.Date.Month / 12.0;
        double ahead = Rules.AheadYears(_game, c);
        var busy = SlotsOf(c).Select(s => s.Tech).ToHashSet();
        foreach (var t in Defs.Values)
            if (!Has(c, t.Key) && !busy.Contains(t) && t.Year <= year + ahead && t.Requires.All(r => Has(c, r))) yield return t;
    }

    public string? CannotResearch(Country c, TechDef t)
    {
        if (Has(c, t.Key)) return "Already known";
        if (SlotsOf(c).Any(s => s.Tech == t)) return "Already researching it";
        if (!t.Requires.All(r => Has(c, r))) return "Needs " + string.Join(", ", t.Requires.Where(r => !Has(c, r)).Select(r => Defs[r].Name));
        double year = _game.World.Date.Year + _game.World.Date.Month / 12.0;
        if (t.Year > year + Rules.AheadYears(_game, c)) return $"Not before {t.Year - Rules.AheadYears(_game, c):0}";
        return null;
    }

    public string? Start(Country c, TechDef t)
    {
        var why = CannotResearch(c, t);
        if (why != null) return why;
        if (!Research.TryGetValue(c, out var list)) Research[c] = list = new List<ResearchSlot>();
        if (list.Count >= Rules.Slots(_game, c)) return "All research slots are busy";
        list.Add(new ResearchSlot { Tech = t });
        return null;
    }

    public void Cancel(Country c, ResearchSlot slot)
    {
        if (Research.TryGetValue(c, out var l)) l.Remove(slot);
    }

    // ------------------------------------------------------------------------------------- effects of technology
    /// <summary>Sum of a modifier key over the technologies a country knows (the modifier system asks for it).</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (!Enabled || scope is not Country c || !Known.TryGetValue(c, out var set)) return 0;
        double v = 0;
        foreach (var k in set) if (Defs.TryGetValue(k, out var t) && t.Modifier.TryGetValue(key, out var x)) v += x;
        return v;
    }

    /// <summary>Recomputes the division stat factors of a country from what it knows.</summary>
    private static readonly string[] CombatStats = { "soft_attack", "hard_attack", "defense", "breakthrough" };

    private void UpdateFactors(Country c)
    {
        c.StatFactors.Clear();
        double quality = Rules.ArmyQuality(_game, c);
        if (Math.Abs(quality) > 1e-6) foreach (var stat in CombatStats) c.StatFactors[stat] = 1 + quality;
        if (!Known.TryGetValue(c, out var set)) return;
        foreach (var k in set)
        {
            if (!Defs.TryGetValue(k, out var t)) continue;
            foreach (var (key, value) in t.Modifier)
                if (key.EndsWith("_factor", StringComparison.OrdinalIgnoreCase))
                {
                    string stat = key[..^7];
                    c.StatFactors[stat] = c.StatFactors.GetValueOrDefault(stat, 1) + value;
                }
        }
    }

    public void Grant(Country c, string key, bool silent = true)
    {
        if (!Defs.TryGetValue(key, out var t)) { Log.ErrorOnce("tech:" + key, $"Unknown technology '{key}'"); return; }
        if (!Known.TryGetValue(c, out var set)) Known[c] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!set.Add(key)) return;
        UpdateFactors(c);
        if (!silent) _game.NotifyPlayer($"{c.Name} has developed {t.Name}.", NotificationKind.Good, c);
    }

    // ------------------------------------------------------------------------------------- game start and monthly
    /// <summary>
    /// Everybody starts with the technologies of 1930 minus their lag (rule StartLag; history/technologies.txt: <c>TAG = { lag = 2 know = { ... } forget = { ... } }</c>),
    /// by the technologies' own years.
    /// </summary>
    public void OnGameStart()
    {
        if (!Enabled) return;
        Known.Clear();
        Research.Clear();
        var special = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/technologies"))
            foreach (var n in root.All) if (n.Children != null) special[n.Key] = n;
        double now = _game.World.Date.Year + _game.World.Date.Month / 12.0;
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            special.TryGetValue(c.Tag, out var s);
            double lag = s != null && s.Has("lag") ? s.GetDouble("lag") : Rules.StartLag(_game, c);
            // in dependency order: a technology is known if its date is not too late and everything it needs is known
            foreach (var t in Defs.Values.OrderBy(t => t.Year).ThenBy(t => t.Order))
                if (t.Year <= now - lag && t.Requires.All(r => Has(c, r))) Grant(c, t.Key);
            if (s != null)
            {
                foreach (var k in s.GetList("know")) Grant(c, k);
                foreach (var k in s.GetList("forget")) { if (Known.TryGetValue(c, out var set)) set.Remove(k); }
                UpdateFactors(c);
            }
        }
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var c in _game.World.Countries.Where(c => c.Exists).ToList())
        {
            if (!Research.TryGetValue(c, out var slots)) Research[c] = slots = new List<ResearchSlot>();
            UpdateFactors(c); // (the army's quality follows the country's wealth)
            double points = Rules.MonthlyPoints(_game, c);
            int n = Math.Max(1, slots.Count);
            foreach (var slot in slots.ToList())
            {
                slot.Progress += points / n * Math.Min(1.0, 1.0 + (1.0 - n) * 0.0); // the points are shared between the slots
                if (slot.Progress < Rules.Cost(_game, c, slot.Tech)) continue;
                slots.Remove(slot);
                Grant(c, slot.Tech.Key, silent: !c.IsPlayer);
                _game.Events.FireOnAction("on_tech_researched", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { });
            }
            // the AI picks the best technologies for its empty slots; the player picks his own
            if (!c.IsPlayer)
                while (slots.Count < Rules.Slots(_game, c))
                {
                    var best = Available(c).OrderByDescending(t => Rules.AiScore(_game, c, t)).FirstOrDefault();
                    if (best == null) break;
                    slots.Add(new ResearchSlot { Tech = best });
                }
        }
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var (c, set) in Known)
        {
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            var k = n.AddBlock("known");
            foreach (var t in set) k.AddItem(t);
            if (Research.TryGetValue(c, out var slots))
                foreach (var s in slots)
                {
                    var r = n.AddBlock("research");
                    r.Add("tech", s.Tech.Key);
                    r.Add("progress", F(s.Progress));
                }
        }
    }

    private void Load(Node root)
    {
        Known.Clear();
        Research.Clear();
        foreach (var n in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in n.Get("known")?.GetList("") ?? n.GetList("known")) if (Defs.ContainsKey(t)) set.Add(t);
            Known[c] = set;
            UpdateFactors(c);
            var list = new List<ResearchSlot>();
            foreach (var r in n.GetAll("research"))
                if (Defs.TryGetValue(r.GetString("tech") ?? "", out var td)) list.Add(new ResearchSlot { Tech = td, Progress = r.GetDouble("progress") });
            Research[c] = list;
        }
    }

    private void Reset()
    {
        Known.Clear();
        Research.Clear();
        foreach (var c in _game.World.Countries) c.StatFactors.Clear();
    }
}
