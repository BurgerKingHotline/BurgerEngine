using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Production;

/// <summary>
/// Overridable production rules (HOI4 style). Subclass and assign to <c>Game.Rules.Production</c> from a C# script
/// (the production feature puts an editable copy in backend/Production.cs).
/// </summary>
public class ProductionRules
{
    /// <summary>Building keys that count as each kind of factory.</summary>
    public virtual string CivilianFactory => "civilian_factory";
    public virtual string MilitaryFactory => "military_factory";
    public virtual string Dockyard => "dockyard";

    /// <summary>Factories of a kind a country can use: levels in its owned states, scaled by how much of each state it controls.</summary>
    public virtual int Factories(Game game, Country c, string building)
    {
        double n = 0;
        foreach (var s in c.OwnedStates) n += s.Working(building) * s.ControlledShare(c); // bombed levels do not work
        return (int)Math.Floor(n + 0.001);
    }

    /// <summary>Civilian factories left for construction after consumer goods.</summary>
    public virtual int CivilianForConstruction(Game game, Country c)
    {
        int civ = Factories(game, c, CivilianFactory);
        double consumer = game.Data.Defines.Get("consumer_goods", 0.25) * (1 + game.Modifiers.Sum(c, "consumer_goods"));
        return Math.Max(0, (int)Math.Floor(civ * (1 - Math.Clamp(consumer, 0, 1))));
    }

    /// <summary>True if this equipment is built in dockyards (naval equipment) instead of military factories.</summary>
    public virtual bool UsesDockyards(EquipmentType e) => e.Category.Equals("naval", StringComparison.OrdinalIgnoreCase);

    /// <summary>Production points one line adds per day.</summary>
    public virtual double LineOutput(Game game, Country c, ProductionLine line, int factories)
    {
        double perFactory = game.Data.Defines.Get("production_per_factory", 4.5);
        return factories * perFactory * line.Efficiency * game.Modifiers.Factor(c, "production");
    }

    /// <summary>New efficiency after a day of production.</summary>
    public virtual double GrowEfficiency(Game game, Country c, ProductionLine line)
    {
        double cap = game.Data.Defines.Get("efficiency_cap", 1) * game.Modifiers.Factor(c, "efficiency_cap");
        double growth = game.Data.Defines.Get("efficiency_growth", 0.02) * game.Modifiers.Factor(c, "efficiency_growth");
        return Math.Min(cap, line.Efficiency + growth * Math.Max(0, cap - line.Efficiency) / Math.Max(0.01, cap) + 0.001);
    }

    /// <summary>Construction points per civilian factory per day (infrastructure in the state helps, as in HOI4).</summary>
    public virtual double ConstructionPerFactory(Game game, Country c, ConstructionProject p)
    {
        double basePts = game.Data.Defines.Get("construction_per_factory", 5);
        double infra = p.State.Buildings.GetValueOrDefault("infrastructure");
        // a poor country builds slower (capital, skills, roads): the square root of its wealth index against the world's (construction_wealth_effect, 0 = off)
        double wealth = game.Data.Defines.Get("construction_wealth_effect", 0) > 0 && game.Politics.Enabled ? Math.Clamp(Math.Sqrt(Math.Max(0.1, game.Politics.WealthIndex(c))), 0.45, 1.2) : 1;
        return basePts * (1 + 0.1 * infra) * game.Modifiers.Factor(c, "construction_speed") * wealth;
    }

    /// <summary>Why a building cannot be queued in a state (null = allowed).</summary>
    public virtual string? CannotBuildReason(Game game, Country c, BuildingType b, State s)
    {
        if (s.Owner != c) return "You must own the state";
        if (s.ControlledShare(c) < 0.5) return "Most of the state is occupied";
        int queued = c.Construction.Count(p => p.State == s && p.Building == b);
        int level = s.Buildings.GetValueOrDefault(b.Key) + queued;
        if (b.SharesSlots)
        {
            int used = game.Data.BuildingTypes.Values.Where(x => x.SharesSlots).Sum(x => s.Buildings.GetValueOrDefault(x.Key) + c.Construction.Count(p => p.State == s && p.Building == x));
            if (used >= SlotsOf(game, s)) return $"No free building slots ({SlotsOf(game, s)})";
        }
        if (level >= b.MaxLevel) return $"Maximum level ({b.MaxLevel})";
        if (b.Potential != null && !game.Script.EvalTrigger(b.Potential, game.Context(s))) return "Not possible in this state";
        return null;
    }

    /// <summary>
    /// How many construction projects the AI keeps going: its building capacity (civilian factories left after consumer goods)
    /// divided by max_factories_per_project, plus two.
    /// </summary>
    public virtual int AiQueueSize(Game game, Country c)
        => 2 + CivilianForConstruction(game, c) / Math.Max(1, (int)game.Data.Defines.Get("max_factories_per_project", 15));

    /// <summary>
    /// Which factory the AI builds next: a peacetime country wants ai_peace_military_ratio (0.6) military factories per
    /// civilian one, a country at war ai_war_military_ratio (1.5); below that it builds military ones, else civilian ones.
    /// </summary>
    public virtual BuildingType? AiNextBuilding(Game game, Country c, BuildingType? civ, BuildingType? mil)
    {
        bool war = game.World.WarsOf(c).Any();
        double ratio = game.Data.Defines.Get(war ? "ai_war_military_ratio" : "ai_peace_military_ratio", war ? 1.5 : 0.6);
        int queuedCiv = civ == null ? 0 : c.Construction.Count(p => p.Building == civ), queuedMil = mil == null ? 0 : c.Construction.Count(p => p.Building == mil);
        double haveCiv = Factories(game, c, CivilianFactory) + queuedCiv, haveMil = Factories(game, c, MilitaryFactory) + queuedMil;
        return haveMil < haveCiv * ratio ? mil ?? civ : civ ?? mil;
    }

    /// <summary>Shared factory slots of a state (HOI4: depends on state size; here: locations + tax).</summary>
    public virtual int SlotsOf(Game game, State s)
    {
        // a state's own number, else its size and wealth; infrastructure (+1 per 3 levels) and the building_slots modifiers of its owner (technology) add to it
        double baseSlots = s.Vars.Get("building_slots") > 0 ? s.Vars.Get("building_slots") : 2 + s.Locations.Count + s.Tax / 2;
        if (s.Owner != null) baseSlots += s.Buildings.GetValueOrDefault("infrastructure") / 3.0 + game.Modifiers.Sum(s.Owner, "building_slots");
        return (int)Math.Clamp(baseSlots, 1, 30);
    }
}

/// <summary>
/// Factories, production lines, the equipment stockpile and construction (production feature).
/// Military factories produce equipment for the lines a country sets up; efficiency grows over time.
/// Civilian factories build buildings. Training and reinforcing divisions take equipment from the stockpile.
/// </summary>
public sealed class ProductionSystem
{
    private readonly Game _game;
    public ProductionSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "production", Write = Save, Read = Load, Reset = Reset });
    }

    private ProductionRules Rules => _game.Rules.Production;
    public bool Enabled => _game.Features.Has(FeatureSet.Production);

    public int MilitaryFactories(Country c) => Rules.Factories(_game, c, Rules.MilitaryFactory);
    public int CivilianFactories(Country c) => Rules.Factories(_game, c, Rules.CivilianFactory);
    public int Dockyards(Country c) => Rules.Factories(_game, c, Rules.Dockyard);
    public int CivilianForConstruction(Country c) => Rules.CivilianForConstruction(_game, c);

    public double Stock(Country c, string equipment) => c.Stockpile.GetValueOrDefault(equipment);

    // ------------------------------------------------------------------------------------- lines
    public ProductionLine AddLine(Country c, EquipmentType e, int factories = 1)
    {
        var line = new ProductionLine { Equipment = e, Factories = Math.Max(0, factories), Efficiency = _game.Data.Defines.Get("efficiency_start", 0.1) };
        c.ProductionLines.Add(line);
        return line;
    }

    public void RemoveLine(Country c, ProductionLine line) => c.ProductionLines.Remove(line);

    /// <summary>Changes what a line produces: efficiency drops (less if the new equipment is in the same category).</summary>
    public void ChangeLine(ProductionLine line, EquipmentType e)
    {
        if (line.Equipment == e) return;
        double keep = line.Equipment.Category.Equals(e.Category, StringComparison.OrdinalIgnoreCase) ? 0.6 : 0.3;
        line.Efficiency = Math.Max(_game.Data.Defines.Get("efficiency_start", 0.1), line.Efficiency * keep);
        line.Equipment = e;
        line.Progress = 0;
    }

    /// <summary>Factories of the right kind not assigned to any line.</summary>
    public int FreeFactories(Country c, bool naval)
    {
        int total = naval ? Dockyards(c) : MilitaryFactories(c);
        int used = c.ProductionLines.Where(l => Rules.UsesDockyards(l.Equipment) == naval).Sum(l => l.Factories);
        return total - used;
    }

    public bool UsesDockyards(EquipmentType e) => Rules.UsesDockyards(e);

    // ------------------------------------------------------------------------------------- equipment
    /// <summary>Takes equipment for a share of a template. Returns the share (0-1) that could be equipped (all or nothing per call when allOrNothing).</summary>
    public double TakeEquipment(Country c, IReadOnlyDictionary<string, double> needs, double share, bool allOrNothing)
    {
        if (!Enabled || needs.Count == 0 || share <= 0) return share;
        double possible = share;
        foreach (var (k, amount) in needs)
        {
            double want = amount * share;
            if (want <= 0) continue;
            possible = Math.Min(possible, share * Math.Min(1, Stock(c, k) / want));
        }
        if (allOrNothing && possible < share - 1e-9) return 0;
        foreach (var (k, amount) in needs)
            c.Stockpile[k] = Math.Max(0, Stock(c, k) - amount * possible);
        return possible;
    }

    /// <summary>Share (0-1) of a template's equipment that is in the stockpile.</summary>
    public double AvailableShare(Country c, IReadOnlyDictionary<string, double> needs)
    {
        if (!Enabled) return 1;
        double share = 1;
        foreach (var (k, amount) in needs) if (amount > 0) share = Math.Min(share, Stock(c, k) / amount);
        return Math.Clamp(share, 0, 1);
    }

    /// <summary>Gives equipment back (cancelled training, disbanded divisions).</summary>
    public void ReturnEquipment(Country c, IReadOnlyDictionary<string, double> needs, double share)
    {
        if (!Enabled) return;
        foreach (var (k, amount) in needs) c.Stockpile[k] = Stock(c, k) + amount * share;
    }

    /// <summary>Total equipment the country's divisions and training orders are missing (for the interface and the AI).</summary>
    public Dictionary<string, double> Shortage(Country c)
    {
        var need = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _game.World.Divisions)
        {
            if (d.Owner != c) continue;
            double missing = 1 - d.StrengthFraction;
            if (missing <= 0.001) continue;
            foreach (var (k, v) in d.Template.Equipment) need[k] = need.GetValueOrDefault(k) + v * missing;
        }
        foreach (var o in c.Recruitment)
            foreach (var (k, v) in o.Template.Equipment) need[k] = need.GetValueOrDefault(k) + v;
        foreach (var k in need.Keys.ToList()) need[k] = Math.Max(0, need[k] - Stock(c, k));
        return need;
    }

    // ------------------------------------------------------------------------------------- construction
    public string? CannotBuildReason(Country c, BuildingType b, State s) => Rules.CannotBuildReason(_game, c, b, s);

    public string? QueueBuilding(Country c, BuildingType b, State s)
    {
        var reason = CannotBuildReason(c, b, s);
        if (reason != null) return reason;
        c.Construction.Add(new ConstructionProject { Building = b, State = s });
        return null;
    }

    public int SlotsOf(State s) => Rules.SlotsOf(_game, s);

    /// <summary>
    /// New game: countries without a stockpile get half their army's equipment in storage, and countries without
    /// production lines get lines for the equipment their templates use (like the AI sets up).
    /// </summary>
    public void OnGameStart()
    {
        foreach (var c in _game.World.Countries.Where(c => c.Exists))
        {
            if (c.Stockpile.Count == 0)
                foreach (var d in _game.World.Divisions.Where(d => d.Owner == c))
                    foreach (var (k, v) in d.Template.Equipment) c.Stockpile[k] = c.Stockpile.GetValueOrDefault(k) + v * 0.5;
            if (c.ProductionLines.Count == 0) AiMonthly(c);
        }
    }

    // ------------------------------------------------------------------------------------- ticks
    public void DailyTick()
    {
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists) continue;
            Produce(c);
            Construct(c);
        }
    }

    private void Produce(Country c)
    {
        foreach (var naval in new[] { false, true })
        {
            var lines = c.ProductionLines.Where(l => Rules.UsesDockyards(l.Equipment) == naval).ToList();
            int available = naval ? Dockyards(c) : MilitaryFactories(c);
            int assigned = lines.Sum(l => l.Factories);
            double scale = assigned <= available || assigned == 0 ? 1 : available / (double)assigned; // lost factories slow every line
            foreach (var line in lines)
            {
                int f = (int)Math.Floor(line.Factories * scale + 0.0001);
                if (f <= 0) { line.LastOutput = 0; continue; }
                // a shortage of the commodities the equipment needs slows the line (steel, oil, aluminium ...)
                line.Progress += Rules.LineOutput(_game, c, line, f) * _game.Commodities.Factor(c, line.Equipment);
                double units = Math.Floor(line.Progress / line.Equipment.Cost);
                line.Progress -= units * line.Equipment.Cost;
                line.LastOutput = units;
                if (units > 0) _game.Commodities.Use(c, line.Equipment, units);
                if (units > 0) c.Stockpile[line.Equipment.Key] = Stock(c, line.Equipment.Key) + units;
                line.Efficiency = Rules.GrowEfficiency(_game, c, line);
            }
        }
    }

    private void Construct(Country c)
    {
        int civ = CivilianForConstruction(c);
        int perProject = (int)_game.Data.Defines.Get("max_factories_per_project", 15);
        foreach (var p in c.Construction.ToList())
        {
            if (p.State.Owner != c) { c.Construction.Remove(p); continue; }
            int f = Math.Min(perProject, civ);
            civ -= f;
            p.Factories = f;
            if (f <= 0) continue;
            p.Progress += f * Rules.ConstructionPerFactory(_game, c, p);
            if (p.Progress < p.Building.Cost) continue;
            c.Construction.Remove(p);
            p.State.Buildings[p.Building.Key] = p.State.Buildings.GetValueOrDefault(p.Building.Key) + 1;
            _game.NotifyPlayer($"{p.Building.Name} completed in {p.State.DisplayName}.", NotificationKind.Good, c);
        }
    }

    // ------------------------------------------------------------------------------------- AI
    /// <summary>Keeps production lines for every equipment the AI's templates need and builds factories.</summary>
    public void AiMonthly(Country c)
    {
        // lines for the equipment of the templates the country can build with what it knows (old equipment is not dropped)
        var needed = _game.Data.Templates.Values
            .Where(t => (string.IsNullOrEmpty(t.OwnerTag) || t.OwnerTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase)) && t.Units.All(u => _game.Tech.CanUse(c, u.Type.Definition)))
            .SelectMany(t => t.Equipment.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(k => _game.Data.Equipment.GetValueOrDefault(k)).Where(e => e != null && _game.Tech.CanUse(c, e.Definition)).Cast<EquipmentType>().ToList();
        foreach (var e in needed)
            if (!c.ProductionLines.Any(l => l.Equipment == e)) AddLine(c, e, 0);
        // spread military factories by shortage (at least one each, unless the stockpile is already big)
        var shortage = Shortage(c);
        var fieldNeed = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _game.World.Divisions)
            if (d.Owner == c)
                foreach (var (k, v) in d.Template.Equipment) fieldNeed[k] = fieldNeed.GetValueOrDefault(k) + v;
        bool Enough(EquipmentType e) => !Rules.UsesDockyards(e) && e.Category != "air" && Stock(c, e.Key) > 1000 + fieldNeed.GetValueOrDefault(e.Key) * 0.75;
        foreach (var naval in new[] { false, true })
        {
            var lines = c.ProductionLines.Where(l => Rules.UsesDockyards(l.Equipment) == naval).ToList();
            int total = naval ? Dockyards(c) : MilitaryFactories(c);
            if (lines.Count == 0 || total == 0) continue;
            double weightSum = lines.Sum(l => 1 + shortage.GetValueOrDefault(l.Equipment.Key) * l.Equipment.Cost);
            int given = 0;
            foreach (var l in lines)
            {
                int want = (int)Math.Round(total * (1 + shortage.GetValueOrDefault(l.Equipment.Key) * l.Equipment.Cost) / weightSum);
                l.Factories = Enough(l.Equipment) ? 0 : Math.Max(total >= lines.Count ? 1 : 0, want);
                given += l.Factories;
            }
            while (given > total) { var big = lines.OrderByDescending(l => l.Factories).First(); big.Factories--; given--; }
        }
        // construction: as many projects as its building capacity supports (a few new ones a month)
        var civT = _game.Data.BuildingTypes.GetValueOrDefault(Rules.CivilianFactory);
        var milT = _game.Data.BuildingTypes.GetValueOrDefault(Rules.MilitaryFactory);
        int size = Rules.AiQueueSize(_game, c);
        for (int added = 0; added < 3 && c.Construction.Count < size; added++)
        {
            var type = Rules.AiNextBuilding(_game, c, civT, milT);
            if (type == null) return;
            var state = c.OwnedStates.Where(s => CannotBuildReason(c, type, s) == null)
                .OrderBy(s => c.Construction.Count(p => p.State == s)).ThenByDescending(s => s.Buildings.GetValueOrDefault("infrastructure")).ThenByDescending(s => s.Tax).FirstOrDefault();
            if (state == null || QueueBuilding(c, type, state) != null) return;
        }
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var c in _game.World.Countries)
        {
            if (c.Stockpile.Count == 0 && c.ProductionLines.Count == 0 && c.Construction.Count == 0) continue;
            var n = root.AddBlock(c.Tag);
            var st = n.AddBlock("stockpile");
            foreach (var (k, v) in c.Stockpile) st.Add(k, F(v));
            foreach (var l in c.ProductionLines)
            {
                var b = n.AddBlock("line");
                b.Add("equipment", l.Equipment.Key);
                b.Add("factories", l.Factories.ToString(CultureInfo.InvariantCulture));
                b.Add("efficiency", F(l.Efficiency));
                b.Add("progress", F(l.Progress));
            }
            foreach (var p in c.Construction)
            {
                var b = n.AddBlock("construction");
                b.Add("building", p.Building.Key);
                b.Add("state", p.State.Key);
                b.Add("progress", F(p.Progress));
            }
        }
    }

    private void Reset()
    {
        foreach (var c in _game.World.Countries) { c.Stockpile.Clear(); c.ProductionLines.Clear(); c.Construction.Clear(); }
    }

    private void Load(Node root)
    {
        foreach (var n in root.All)
        {
            var c = _game.World.GetCountry(n.Key);
            if (c == null || n.Children == null) continue;
            if (n.Get("stockpile") is { } st)
                foreach (var e in st.All) if (e.Value != null) c.Stockpile[e.Key] = e.AsDouble();
            foreach (var b in n.GetAll("line"))
                if (_game.Data.Equipment.TryGetValue(b.GetString("equipment") ?? "", out var e))
                    c.ProductionLines.Add(new ProductionLine { Equipment = e, Factories = b.GetInt("factories"), Efficiency = b.GetDouble("efficiency", 0.1), Progress = b.GetDouble("progress") });
            foreach (var b in n.GetAll("construction"))
                if (_game.Data.BuildingTypes.TryGetValue(b.GetString("building") ?? "", out var bt) && _game.World.GetState(b.GetString("state")) is { } s)
                    c.Construction.Add(new ConstructionProject { Building = bt, State = s, Progress = b.GetDouble("progress") });
        }
    }
}
