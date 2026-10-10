using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Supply;

/// <summary>
/// Overridable supply rules. Two modes come with the engine (choose one feature):
/// <list type="bullet">
/// <item><b>supply_range</b>: supply spreads from the capital and from every own, controlled location, getting weaker
/// with every step (rough terrain makes steps longer). It only travels through friendly land, so encircled armies starve.</item>
/// <item><b>supply_hubs</b>: supply comes from the capital and from supply hubs; a hub only works while railways
/// (or friendly land next to the capital) connect it to the capital. Cut the railway and the hub goes dark.</item>
/// </list>
/// Subclass and assign to <c>Game.Rules.Supply</c> (the feature puts an editable copy in backend/Supply.cs).
/// </summary>
public class SupplyRules
{
    /// <summary>Can supply of <paramref name="c"/> pass through this location? (own, allied, subject/overlord or military access land)</summary>
    public virtual bool Friendly(Game game, Country c, Location l)
    {
        if (!l.IsLand || !l.IsPassable) return false;
        var ctrl = l.Controller ?? l.Owner;
        if (ctrl == null) return false;
        if (ctrl == c) return true;
        var r = game.World.Relations;
        return r.AreAllied(c, ctrl) || r.OverlordOf(c) == ctrl || r.OverlordOf(ctrl) == c || r.HasAccess(c, ctrl) && !game.World.AtWar(c, ctrl);
    }

    /// <summary>Cost of carrying supply into a location (1 = one normal step; rough terrain costs more, infrastructure less).</summary>
    public virtual double StepCost(Game game, Location l)
    {
        double infra = l.State?.Working("infrastructure") ?? 0;
        return Math.Max(0.3, l.Terrain.MovementCost) / (1 + 0.1 * infra);
    }

    /// <summary>Supply a location produces on its own (own, controlled land: a little; more with infrastructure).</summary>
    public virtual double LocalSupply(Game game, Country c, Location l)
    {
        if (l.Owner != c || l.Controller != c) return 0;
        double infra = l.State?.Working("infrastructure") ?? 0;
        return game.Data.Defines.Get("supply_local", 2) * (1 + 0.15 * infra);
    }

    /// <summary>Supply sources (location, amount) for range mode.</summary>
    public virtual IEnumerable<(Location L, double Amount)> RangeSources(Game game, Country c)
    {
        if (c.Capital != null && c.Capital.Controller == c) yield return (c.Capital, game.Data.Defines.Get("supply_capital", 15));
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
            {
                double local = LocalSupply(game, c, l);
                if (local > 0) yield return (l, local);
            }
    }

    /// <summary>Supply sources for hub mode: the capital and every hub connected to it by railways.</summary>
    public virtual IEnumerable<(Location L, double Amount)> HubSources(Game game, Country c)
    {
        if (c.Capital == null || c.Capital.Controller != c) yield break;
        yield return (c.Capital, game.Data.Defines.Get("supply_capital", 15));
        var connected = RailwayNetwork(game, c);
        foreach (var s in connected)
        {
            double hub = s.Working("supply_hub");
            if (hub <= 0) continue;
            var loc = s.Locations.FirstOrDefault(l => l.Controller != null && Friendly(game, c, l));
            if (loc == null) continue;
            double rail = Math.Min(5, s.Working("railway"));
            yield return (loc, game.Data.Defines.Get("supply_hub", 10) * hub * (0.6 + 0.1 * rail));
        }
        // a little local supply everywhere at home
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
            {
                double local = LocalSupply(game, c, l) * 0.35;
                if (local > 0) yield return (l, local);
            }
    }

    /// <summary>States reachable from the capital's state through friendly states with railways (the capital's neighbours count too).</summary>
    public virtual HashSet<State> RailwayNetwork(Game game, Country c)
    {
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start == null) return result;
        var q = new Queue<State>();
        result.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var s = q.Dequeue();
            bool rail = s == start || s.Working("railway") > 0.5;
            if (!rail) continue;
            foreach (var l in s.Locations)
            {
                if (!Friendly(game, c, l)) continue;
                foreach (var adj in l.Neighbors)
                {
                    var ns = adj.To.State;
                    if (ns == null || result.Contains(ns) || !adj.To.IsLand) continue;
                    // the next state must be friendly where it touches
                    if (!Friendly(game, c, adj.To)) continue;
                    result.Add(ns);
                    q.Enqueue(ns);
                }
            }
        }
        return result;
    }

    /// <summary>Supply a division needs per day.</summary>
    public virtual double Usage(Game game, Division d) => d.Stat("supply_use", 0.05 * Math.Max(1, d.Template.Battalions));
}

/// <summary>
/// Computes a supply map for every country with divisions (daily) and applies its effects: divisions in locations
/// where usage is higher than supply lose strength, recover organisation slower, fight worse and are not reinforced.
/// </summary>
public sealed class SupplySystem
{
    private readonly Game _game;
    private readonly Dictionary<Country, double[]> _maps = new();
    public SupplySystem(Game game) { _game = game; }

    private SupplyRules Rules => _game.Rules.Supply;
    public bool Enabled => _game.Features.HasSupply;
    public bool HubMode => _game.Features.Has(FeatureSet.SupplyHubs);

    /// <summary>Supply available to a country in a location (0 if unknown).</summary>
    public double SupplyAt(Country c, Location l)
    {
        if (!_maps.TryGetValue(c, out var m)) m = Compute(c);
        return l.Index < m.Length ? m[l.Index] : 0;
    }

    /// <summary>Recomputes and returns a country's supply map.</summary>
    // per-day caches shared by all countries: the falloff factor of every location (terrain, infrastructure)
    private double[] _stepPow = Array.Empty<double>(), _stepPowRiver = Array.Empty<double>();
    private int _stepDay = -1;
    private sbyte[] _friendly = Array.Empty<sbyte>();

    public double[] Compute(Country c)
    {
        var w = _game.World;
        int n0 = w.Locations.Count;
        var map = new double[n0];
        var sources = (HubMode ? Rules.HubSources(_game, c) : Rules.RangeSources(_game, c)).ToList();
        double falloff = Math.Clamp(_game.Data.Defines.Get("supply_falloff", 0.75), 0.05, 0.99);
        if (HubMode) falloff = Math.Min(0.99, falloff * 0.9); // hubs reach less far without them
        int today = w.Date.TotalDays;
        if (_stepDay != today || _stepPow.Length != n0)
        {
            if (_stepPow.Length != n0) { _stepPow = new double[n0]; _stepPowRiver = new double[n0]; }
            foreach (var l in w.Locations)
            {
                if (!l.IsLand) continue;
                double sc = Rules.StepCost(_game, l);
                _stepPow[l.Index] = Math.Pow(falloff, sc);
                _stepPowRiver[l.Index] = Math.Pow(falloff, sc * 1.3);
            }
            _stepDay = today;
        }
        if (_friendly.Length != n0) _friendly = new sbyte[n0];
        Array.Fill(_friendly, (sbyte)-1);
        bool Friendly(Location l)
        {
            ref sbyte f = ref _friendly[l.Index];
            if (f < 0) f = (sbyte)(Rules.Friendly(_game, c, l) ? 1 : 0);
            return f == 1;
        }
        // best-first spread from every source: value = amount * falloff ^ (path cost)
        var queue = new PriorityQueue<Location, double>();
        foreach (var (l, amount) in sources)
        {
            if (amount <= map[l.Index]) continue;
            map[l.Index] = amount;
            queue.Enqueue(l, -amount);
        }
        while (queue.TryDequeue(out var l, out double pri))
        {
            double v = map[l.Index];
            if (v < 0.05 || -pri < v - 1e-9) continue; // weak, or an old queue entry
            bool canPass = Friendly(l);
            foreach (var adj in l.Neighbors)
            {
                var n = adj.To;
                if (!n.IsLand) continue;
                bool friendly = Friendly(n);
                // supply reaches one step into enemy land (attacking divisions), but only spreads through friendly land
                if (!canPass && !friendly) continue;
                double nv = v * (adj.River ? _stepPowRiver[n.Index] : _stepPow[n.Index]);
                if (!friendly) nv *= 0.5;
                if (nv <= map[n.Index] + 1e-6) continue;
                map[n.Index] = nv;
                if (friendly) queue.Enqueue(n, -nv);
            }
        }
        _maps[c] = map;
        _computedDay[c] = today;
        _computedVersion[c] = _game.MapVersion;
        return map;
    }

    private readonly Dictionary<Country, int> _computedVersion = new();

    private readonly Dictionary<Country, int> _computedDay = new();

    public void DailyTick()
    {
        // countries at war get a fresh supply map every day; at peace once a week is enough (and much cheaper on big maps)
        int today = _game.World.Date.TotalDays;
        foreach (var c in _maps.Keys.ToList())
        {
            int age = today - _computedDay.GetValueOrDefault(c, -999);
            bool changed = _computedVersion.GetValueOrDefault(c, -1) != _game.MapVersion;
            if (age >= 7 || (changed && _game.World.WarsOf(c).Any())) _maps.Remove(c);
        }
        var byLoc = new Dictionary<(Country, Location), double>();
        foreach (var d in _game.World.Divisions)
            byLoc[(d.Owner, d.Location)] = byLoc.GetValueOrDefault((d.Owner, d.Location)) + Rules.Usage(_game, d);
        double attrition = _game.Data.Defines.Get("supply_attrition", 0.02);
        foreach (var d in _game.World.Divisions.ToList())
        {
            if (d.Location.IsWater) continue; // armies at sea carry their supplies
            double cap = SupplyAt(d.Owner, d.Location);
            double use = byLoc[(d.Owner, d.Location)];
            double ratio = use <= 0 ? 1 : Math.Clamp(cap / use, 0, 1);
            d.Supply += (ratio - d.Supply) * 0.35; // stocks run out over a few days
            if (d.Supply < 0.99)
            {
                double lost = d.MaxStrength * attrition * (1 - d.Supply);
                d.Strength = Math.Max(d.MaxStrength * 0.05, d.Strength - lost);
                d.Org = Math.Min(d.Org, d.MaxOrg * (0.3 + 0.7 * d.Supply));
            }
        }
    }
}
