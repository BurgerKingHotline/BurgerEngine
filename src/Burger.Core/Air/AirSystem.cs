using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Air;

/// <summary>
/// Overridable air rules (HOI4 style). Subclass and assign to <c>Game.Rules.Air</c> (the air feature puts a copy in backend/Air.cs).
/// </summary>
public class AirRules
{
    /// <summary>Planes in a full wing.</summary>
    public virtual double WingSize(Game game) => Math.Max(1, game.Data.Defines.Get("air_wing_size", 50));

    /// <summary>Planes a state's air base can hold (0 = no air base).</summary>
    public virtual double BaseCapacity(Game game, State s) => s.Working("air_base") * game.Data.Defines.Get("air_base_capacity", 100);

    /// <summary>Can wings of <paramref name="c"/> be based in this state? (own or allied state with an air base, controlled by a friend)</summary>
    public virtual bool CanUseBase(Game game, Country c, State s)
    {
        if (s.Buildings.GetValueOrDefault("air_base") <= 0 || s.Owner == null) return false;
        var ctrl = s.Locations.Count > 0 ? s.Locations[0].Controller ?? s.Owner : s.Owner;
        bool friend(Country x) => x == c || game.World.Relations.AreAllied(c, x);
        return friend(s.Owner) && friend(ctrl);
    }

    /// <summary>Can this plane type fly the mission? (needs the matching attack stat)</summary>
    public virtual bool CanFly(Game game, PlaneType t, AirMission m) => m switch
    {
        AirMission.Superiority => t.Stat("air_attack") > 0,
        AirMission.CloseSupport => t.Stat("ground_attack") > 0,
        AirMission.Bombing => t.Stat("strategic_attack") > 0 && game.Features.Has(FeatureSet.Production),
        _ => true,
    };

    /// <summary>Km from the wing's base to the center of the region.</summary>
    public virtual double DistanceKm(Game game, State from, AirRegion to)
    {
        if (from.Locations.Count == 0) return double.MaxValue;
        float x = from.Locations.Average(l => l.CenterX), y = from.Locations.Average(l => l.CenterY);
        double dx = x - to.CenterX, dy = y - to.CenterY;
        return Math.Sqrt(dx * dx + dy * dy) * game.Data.Defines.KmPerPixel;
    }

    public virtual bool InRange(Game game, AirWing w, AirRegion r) => r.States.Contains(w.Base) || DistanceKm(game, w.Base, r) <= w.Type.Range;

    /// <summary>Air superiority strength of a wing (superiority mission).</summary>
    public virtual double AirPower(Game game, AirWing w)
        => w.Planes * (w.Type.Stat("air_attack") + 0.5 * w.Type.Stat("air_defense") + 0.25 * w.Type.Stat("agility"));

    /// <summary>
    /// Combat multiplier of a division from air superiority over its region: share 0..1 of the air power of its side.
    /// 0.5 (or no planes) = no effect; full superiority gives +air_superiority_bonus, none gives -air_superiority_bonus.
    /// </summary>
    public virtual double SuperiorityFactor(Game game, Division d, double share, bool contested)
        => contested ? 1 + game.Data.Defines.Get("air_superiority_bonus", 0.2) * (share * 2 - 1) : 1;

    /// <summary>Share of planes a superiority wing loses per day against enemy fighters (0.5 = even fight).</summary>
    public virtual double DogfightLosses(Game game, AirWing w, double enemyShare)
        => game.Data.Defines.Get("air_loss_rate", 0.03) * enemyShare * 10 / (10 + w.Type.Stat("air_defense"));

    /// <summary>Share of planes a CAS or bomber wing loses per day to enemy fighters (scaled by their air superiority).</summary>
    public virtual double InterceptLosses(Game game, AirWing w, double enemyShare)
        => game.Data.Defines.Get("air_loss_rate", 0.03) * 1.5 * enemyShare * 10 / (10 + w.Type.Stat("air_defense"));

    /// <summary>Daily damage (organisation points per division in a battle) of a close air support wing.</summary>
    public virtual double CasDamage(Game game, AirWing w, double friendlyShare)
        => w.Planes * w.Type.Stat("ground_attack") * game.Data.Defines.Get("cas_damage", 0.02) * (0.3 + 0.7 * friendlyShare);

    /// <summary>Building levels a bomber wing knocks out per day.</summary>
    public virtual double BombingDamage(Game game, AirWing w, double friendlyShare)
        => w.Planes * w.Type.Stat("strategic_attack") * game.Data.Defines.Get("bombing_damage", 0.0005) * (0.2 + 0.8 * friendlyShare);

    /// <summary>Buildings bombers aim at, most important first.</summary>
    public virtual IReadOnlyList<string> BombingTargets(Game game) => new[] { "military_factory", "civilian_factory", "dockyard", "supply_hub", "railway", "infrastructure" };

    /// <summary>Damaged building levels repaired per day.</summary>
    public virtual double RepairPerDay(Game game, State s, string building) => game.Data.Defines.Get("bomb_repair", 0.05);
}

/// <summary>
/// Air regions, air wings at air bases and their missions (air feature): air superiority (fighters), close air support
/// (damages enemy divisions in battles) and strategic bombing (knocks out factories and infrastructure, production feature).
/// Air superiority gives the divisions of the stronger side a combat bonus in that region.
/// </summary>
public sealed class AirSystem
{
    private readonly Game _game;
    public readonly List<AirWing> Wings = new();
    public readonly Dictionary<Country, List<WingOrder>> Orders = new();
    private List<AirRegion>? _regions;
    private Dictionary<State, AirRegion> _regionOf = new();
    private int _nextWing = 1;
    /// <summary>Air superiority power per region and country (missions flown today).</summary>
    private readonly Dictionary<(AirRegion, Country), double> _power = new();
    private bool _powerDirty = true;
    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public double PlanesLost, LevelsBombed, CasDamageDone;

    public AirSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "air", Write = Save, Read = Load, Reset = Reset });
        game.Warfare.CombatFactorSources.Add(SuperiorityFactor);
    }

    private AirRules Rules => _game.Rules.Air;
    public bool Enabled => _game.Features.Has(FeatureSet.Air);
    public double WingSize => Rules.WingSize(_game);

    // ------------------------------------------------------------------------------------- regions
    public IReadOnlyList<AirRegion> Regions => _regions ??= LoadRegions();
    public AirRegion? RegionOf(State? s) { _ = Regions; return s != null && _regionOf.TryGetValue(s, out var r) ? r : null; }
    public AirRegion? RegionOf(Location l) => RegionOf(l.State);
    public AirRegion? GetRegion(string? key) => key == null ? null : Regions.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private List<AirRegion> LoadRegions()
    {
        var list = new List<AirRegion>();
        _regionOf = new Dictionary<State, AirRegion>();
        foreach (var root in Burger.Core.Data.DataLoader.ParseFileOrFolder(_game.Data, "map/air_regions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var r = new AirRegion { Index = list.Count, Key = n.Key, NameOverride = n.GetString("name") };
                foreach (var sk in n.GetList("states"))
                {
                    var s = _game.World.GetState(sk);
                    if (s == null) { Log.Error($"Air region '{n.Key}': unknown state '{sk}'", n.Location); continue; }
                    if (_regionOf.ContainsKey(s)) { Log.Warn($"State '{sk}' is in two air regions", n.Location); continue; }
                    r.States.Add(s);
                    _regionOf[s] = r;
                }
                if (r.States.Count > 0) list.Add(r);
            }
        // states not in any region are grouped automatically
        var rest = _game.World.States.Where(s => !_regionOf.ContainsKey(s) && s.Locations.Count > 0).ToList();
        if (rest.Count > 0)
            foreach (var group in Cluster(_game, rest))
            {
                var r = new AirRegion { Index = list.Count, Key = $"air_region_{list.Count + 1}" };
                r.States.AddRange(group);
                foreach (var s in group) _regionOf[s] = r;
                list.Add(r);
            }
        foreach (var r in list)
        {
            var locs = r.States.SelectMany(s => s.Locations).ToList();
            r.CenterX = locs.Count == 0 ? 0 : locs.Average(l => l.CenterX);
            r.CenterY = locs.Count == 0 ? 0 : locs.Average(l => l.CenterY);
        }
        return list;
    }

    /// <summary>Groups neighbouring states into regions of about <c>air_region_states</c> states (used for states without a region and by the air feature's install step).</summary>
    public static List<List<State>> Cluster(Game game, List<State> states)
    {
        int size = Math.Max(1, (int)game.Data.Defines.Get("air_region_states", 4));
        double maxKm = game.Data.Defines.Get("air_region_km", 700);
        var left = new HashSet<State>(states);
        var result = new List<List<State>>();
        (float X, float Y) Center(State s) => (s.Locations.Average(l => l.CenterX), s.Locations.Average(l => l.CenterY));
        var centers = states.ToDictionary(s => s, Center);
        IEnumerable<State> NeighbourStates(State s) => s.Locations.SelectMany(l => l.Neighbors).Select(a => a.To.State).Where(x => x != null && x != s).Cast<State>().Distinct();
        while (left.Count > 0)
        {
            // start in the top-left corner of what is left, then grow to the nearest neighbouring states
            var seed = left.OrderBy(s => centers[s].Y + centers[s].X * 0.5).First();
            var group = new List<State> { seed };
            left.Remove(seed);
            while (group.Count < size)
            {
                float gx = group.Average(s => centers[s].X), gy = group.Average(s => centers[s].Y);
                var next = group.SelectMany(NeighbourStates).Where(left.Contains).Distinct()
                    .OrderBy(s => Math.Pow(centers[s].X - gx, 2) + Math.Pow(centers[s].Y - gy, 2)).FirstOrDefault();
                if (next == null) break;
                double km = Math.Sqrt(Math.Pow(centers[next].X - centers[seed].X, 2) + Math.Pow(centers[next].Y - centers[seed].Y, 2)) * game.Data.Defines.KmPerPixel;
                if (km > maxKm) break;
                group.Add(next);
                left.Remove(next);
            }
            result.Add(group);
        }
        // tiny leftovers join a neighbouring region
        foreach (var g in result.Where(g => g.Count == 1).ToList())
        {
            var other = result.Where(o => o != g && o.Count < size + 2 && NeighbourStates(g[0]).Any(o.Contains)).OrderBy(o => o.Count).FirstOrDefault();
            if (other == null) continue;
            other.Add(g[0]);
            result.Remove(g);
        }
        return result;
    }

    // ------------------------------------------------------------------------------------- wings
    public IEnumerable<AirWing> WingsOf(Country c) => Wings.Where(w => w.Owner == c);
    public List<WingOrder> OrdersOf(Country c) => Orders.TryGetValue(c, out var l) ? l : Orders[c] = new List<WingOrder>();
    public bool CanUseBase(Country c, State s) => Rules.CanUseBase(_game, c, s);
    public double Capacity(State s) => Rules.BaseCapacity(_game, s);
    public double PlanesAt(State s) => Wings.Where(w => w.Base == s).Sum(w => w.Planes);
    public double FreeCapacity(State s, AirWing? except = null) => Capacity(s) - Wings.Where(w => w.Base == s && w != except).Sum(w => w.Planes);
    public bool CanFly(PlaneType t, AirMission m) => Rules.CanFly(_game, t, m);
    public bool InRange(AirWing w, AirRegion r) => Rules.InRange(_game, w, r);

    /// <summary>The air base new wings of a country go to: the one with the most free room (the capital's state first).</summary>
    public State? BestBase(Country c, double planes = 0)
    {
        var bases = _game.World.States.Where(s => CanUseBase(c, s) && FreeCapacity(s) >= Math.Max(1, planes)).ToList();
        var cap = c.Capital?.State;
        if (cap != null && bases.Contains(cap)) return cap;
        return bases.Where(s => s.Owner == c).OrderByDescending(s => FreeCapacity(s)).FirstOrDefault() ?? bases.OrderByDescending(s => FreeCapacity(s)).FirstOrDefault();
    }

    public AirWing CreateWing(Country c, PlaneType t, State baseState, double planes, string? name = null)
    {
        int n = WingsOf(c).Count(w => w.Type == t) + 1;
        var w = new AirWing { Id = _nextWing++, Name = name ?? $"{Burger.Core.Warfare.WarfareSystem.Ordinal(n)} {t.Name} Wing", Owner = c, Type = t, Base = baseState, Planes = Math.Max(1, planes) };
        Wings.Add(w);
        _powerDirty = true;
        return w;
    }

    public void Disband(AirWing w)
    {
        Wings.Remove(w);
        if (_game.Production.Enabled) w.Owner.Stockpile[w.Type.Key] = w.Owner.Stockpile.GetValueOrDefault(w.Type.Key) + Math.Floor(w.Planes);
        _powerDirty = true;
    }

    /// <summary>Why the wing cannot move to that air base (null = it can).</summary>
    public string? CannotRebaseReason(AirWing w, State s)
    {
        if (!CanUseBase(w.Owner, s)) return "Not an air base you can use";
        if (s == w.Base) return "Already there";
        if (FreeCapacity(s, w) < w.Planes) return $"The air base is full ({PlanesAt(s):0}/{Capacity(s):0} planes)";
        return null;
    }

    public string? Rebase(AirWing w, State s)
    {
        var reason = CannotRebaseReason(w, s);
        if (reason != null) return reason;
        w.Base = s;
        if (w.Region != null && !InRange(w, w.Region)) { w.Region = null; w.Mission = AirMission.None; }
        _powerDirty = true;
        return null;
    }

    /// <summary>Why the wing cannot fly this mission over that region (null = it can).</summary>
    public string? CannotAssignReason(AirWing w, AirRegion r, AirMission m)
    {
        if (!CanFly(w.Type, m)) return $"{w.Type.Name} cannot fly {MissionName(m)}";
        if (!InRange(w, r)) return $"Out of range ({Rules.DistanceKm(_game, w.Base, r):0} km, range {w.Type.Range:0} km)";
        return null;
    }

    public string? Assign(AirWing w, AirRegion? r, AirMission m)
    {
        if (r != null && m != AirMission.None)
        {
            var reason = CannotAssignReason(w, r, m);
            if (reason != null) return reason;
        }
        w.Region = r;
        w.Mission = r == null ? AirMission.None : m;
        _powerDirty = true;
        return null;
    }

    /// <summary>The mission this plane type is best at.</summary>
    public AirMission DefaultMission(PlaneType t)
    {
        // the mission of its strongest attack stat
        var options = new[] { (AirMission.Superiority, t.Stat("air_attack")), (AirMission.CloseSupport, t.Stat("ground_attack")), (AirMission.Bombing, t.Stat("strategic_attack")) };
        return options.Where(o => CanFly(t, o.Item1)).OrderByDescending(o => o.Item2).Select(o => o.Item1).DefaultIfEmpty(AirMission.None).First();
    }

    public static string MissionName(AirMission m) => m switch
    {
        AirMission.Superiority => "air superiority",
        AirMission.CloseSupport => "close air support",
        AirMission.Bombing => "strategic bombing",
        _ => "no mission",
    };

    // ------------------------------------------------------------------------------------- superiority
    private bool Flying(AirWing w) => w.Region != null && w.Mission != AirMission.None && w.Planes >= 1 && InRange(w, w.Region) && CanUseBase(w.Owner, w.Base);

    private void UpdatePower()
    {
        _power.Clear();
        foreach (var w in Wings)
        {
            w.Active = Flying(w);
            if (!w.Active || w.Mission != AirMission.Superiority) continue;
            var key = (w.Region!, w.Owner);
            _power[key] = _power.GetValueOrDefault(key) + Rules.AirPower(_game, w);
        }
        _powerDirty = false;
    }

    /// <summary>Air superiority power a country has over a region.</summary>
    public double PowerOf(AirRegion r, Country c)
    {
        if (_powerDirty) UpdatePower();
        return _power.GetValueOrDefault((r, c));
    }

    /// <summary>Share (0-1) of the air superiority power in a region held by <paramref name="c"/> and its friends against its enemies. Contested = anyone flies there.</summary>
    public (double Share, bool Contested) Superiority(AirRegion r, Country c)
    {
        if (_powerDirty) UpdatePower();
        double own = 0, enemy = 0;
        foreach (var ((reg, owner), p) in _power)
        {
            if (reg != r) continue;
            if (owner == c || _game.World.Relations.AreAllied(c, owner)) own += p;
            else if (_game.World.AtWar(c, owner)) enemy += p;
        }
        double total = own + enemy;
        return total <= 0 ? (0.5, false) : (own / total, enemy > 0 || own > 0);
    }

    private double SuperiorityFactor(Division d)
    {
        if (!Enabled || d.Location.IsWater) return 1;
        var r = RegionOf(d.Location);
        if (r == null) return 1;
        var (share, contested) = Superiority(r, d.Owner);
        return Rules.SuperiorityFactor(_game, d, share, contested);
    }

    // ------------------------------------------------------------------------------------- daily
    public void DailyTick()
    {
        FixBases();
        UpdatePower();
        var losses = new Dictionary<AirWing, double>();
        foreach (var w in Wings.Where(w => w.Active))
        {
            var r = w.Region!;
            var (share, contested) = Superiority(r, w.Owner);
            bool enemyFighters = contested && share < 1;
            switch (w.Mission)
            {
                case AirMission.Superiority:
                    if (enemyFighters) losses[w] = Rules.DogfightLosses(_game, w, 1 - share);
                    break;
                case AirMission.CloseSupport:
                    if (enemyFighters) losses[w] = Rules.InterceptLosses(_game, w, 1 - share);
                    CloseSupport(w, r, share);
                    break;
                case AirMission.Bombing:
                    if (enemyFighters) losses[w] = Rules.InterceptLosses(_game, w, 1 - share);
                    Bomb(w, r, share);
                    break;
            }
        }
        foreach (var (w, rate) in losses)
        {
            double lost = w.Planes * Math.Clamp(rate, 0, 1);
            w.Planes -= lost;
            PlanesLost += lost;
            if (w.Planes < 1)
            {
                Wings.Remove(w);
                _game.NotifyPlayer($"{w.Name} was shot down.", NotificationKind.Bad, w.Owner);
            }
        }
        Repair();
        Reinforce();
        foreach (var (c, orders) in Orders)
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                o.HoursLeft -= 24;
                if (o.HoursLeft > 0) continue;
                orders.RemoveAt(i--);
                var b = CanUseBase(c, o.Base) && FreeCapacity(o.Base) >= WingSize ? o.Base : BestBase(c, WingSize) ?? BestBase(c);
                if (b == null) { _game.NotifyPlayer($"A {o.Type.Name} wing is ready but you have no air base with room.", NotificationKind.Bad, c); continue; }
                var wing = CreateWing(c, o.Type, b, WingSize);
                _game.NotifyPlayer($"{wing.Name} is ready at {b.DisplayName}.", NotificationKind.Good, c);
            }
        _powerDirty = true;
    }

    /// <summary>Wings whose base was lost move to the nearest usable base (or are lost).</summary>
    private void FixBases()
    {
        foreach (var w in Wings.ToList())
        {
            if (CanUseBase(w.Owner, w.Base)) continue;
            var to = _game.World.States.Where(s => CanUseBase(w.Owner, s) && FreeCapacity(s) >= w.Planes)
                .OrderBy(s => Dist(s, w.Base)).FirstOrDefault();
            if (to == null)
            {
                Wings.Remove(w);
                _game.NotifyPlayer($"{w.Name} lost its air base and had nowhere to go.", NotificationKind.Bad, w.Owner);
                continue;
            }
            w.Base = to;
            if (w.Region != null && !InRange(w, w.Region)) { w.Region = null; w.Mission = AirMission.None; }
            _game.NotifyPlayer($"{w.Name} lost its air base and moved to {to.DisplayName}.", NotificationKind.War, w.Owner);
        }
    }

    private static double Dist(State a, State b)
    {
        if (a.Locations.Count == 0 || b.Locations.Count == 0) return double.MaxValue;
        double dx = a.Locations[0].CenterX - b.Locations[0].CenterX, dy = a.Locations[0].CenterY - b.Locations[0].CenterY;
        return dx * dx + dy * dy;
    }

    private void CloseSupport(AirWing w, AirRegion r, double share)
    {
        var battles = _game.World.Battles.Where(b => !b.Finished && b.Location.State != null && RegionOf(b.Location.State) == r).ToList();
        if (battles.Count == 0) return;
        double total = Rules.CasDamage(_game, w, share);
        bool friend(Country x) => x == w.Owner || _game.World.Relations.AreAllied(w.Owner, x);
        var targets = new List<Division>();
        foreach (var b in battles)
        {
            if (b.Attackers.Concat(b.Defenders).Count() == 0) continue;
            bool weAttack = b.Attackers.Any(d => friend(d.Owner)), weDefend = b.Defenders.Any(d => friend(d.Owner));
            if (weAttack) targets.AddRange(b.Defenders.Where(d => _game.World.AtWar(w.Owner, d.Owner)));
            if (weDefend) targets.AddRange(b.Attackers.Where(d => _game.World.AtWar(w.Owner, d.Owner)));
        }
        if (targets.Count == 0) return;
        double each = total / targets.Count;
        foreach (var d in targets)
        {
            double org = Math.Min(d.Org, each);
            d.Org -= org;
            d.Strength = Math.Max(0.01, d.Strength - each * 0.15);
            CasDamageDone += org;
        }
    }

    private void Bomb(AirWing w, AirRegion r, double share)
    {
        var target = r.States.Where(s => s.Owner != null && _game.World.AtWar(w.Owner, s.Owner))
            .Select(s => (State: s, Building: Rules.BombingTargets(_game).FirstOrDefault(b => s.Working(b) >= 0.5)))
            .Where(t => t.Building != null)
            .OrderByDescending(t => t.State.Working("military_factory") + t.State.Working("civilian_factory")).FirstOrDefault();
        if (target.State == null) return;
        double dmg = Rules.BombingDamage(_game, w, share);
        double level = target.State.Buildings.GetValueOrDefault(target.Building!);
        double cur = target.State.Damaged.GetValueOrDefault(target.Building!);
        double add = Math.Min(dmg, level - cur);
        target.State.Damaged[target.Building!] = cur + add;
        LevelsBombed += add;
    }

    private void Repair()
    {
        foreach (var s in _game.World.States)
        {
            if (s.Damaged.Count == 0) continue;
            foreach (var b in s.Damaged.Keys.ToList())
            {
                double v = s.Damaged[b] - Rules.RepairPerDay(_game, s, b);
                if (v <= 0.001) s.Damaged.Remove(b); else s.Damaged[b] = v;
            }
        }
    }

    /// <summary>Under-strength wings get planes: from the stockpile (production) or bought (2% of a wing per day; gold with the economy feature).</summary>
    private void Reinforce()
    {
        double size = WingSize;
        foreach (var w in Wings)
        {
            double missing = Math.Min(size - w.Planes, Math.Max(0, FreeCapacity(w.Base)));
            if (missing < 1) continue;
            var c = w.Owner;
            if (_game.Production.Enabled)
            {
                double have = Math.Floor(c.Stockpile.GetValueOrDefault(w.Type.Key));
                double take = Math.Min(have, Math.Floor(missing));
                if (take <= 0) continue;
                c.Stockpile[w.Type.Key] = c.Stockpile.GetValueOrDefault(w.Type.Key) - take;
                w.Planes += take;
            }
            else
            {
                double add = Math.Min(missing, size * 0.02);
                double gold = w.Type.Cost / size * add;
                if (_game.Features.Has(FeatureSet.Economy)) { if (c.Gold < gold) continue; c.Gold -= gold; }
                w.Planes += add;
            }
        }
    }

    // ------------------------------------------------------------------------------------- building wings
    /// <summary>Why a new wing of this type cannot be made now (null = it can).</summary>
    public string? CannotBuildReason(Country c, PlaneType t)
    {
        if (BestBase(c) == null) return "You have no air base with room";
        if (_game.Production.Enabled)
            return c.Stockpile.GetValueOrDefault(t.Key) < Math.Min(10, WingSize) ? $"Needs at least {Math.Min(10, WingSize):0} {t.Name} planes in the stockpile (build them in Production)" : null;
        if (_game.Features.Has(FeatureSet.Economy) && c.Gold < t.Cost) return $"Needs {t.Cost:0} gold";
        if (c.Manpower < t.Manpower) return $"Needs {t.Manpower:0} manpower";
        return null;
    }

    /// <summary>Production: makes a wing from the stockpile now. Otherwise: orders a wing (gold, manpower, build days).</summary>
    public string? BuildWing(Country c, PlaneType t, State? at = null)
    {
        var reason = CannotBuildReason(c, t);
        if (reason != null) return reason;
        if (_game.Production.Enabled)
        {
            double planes = Math.Min(WingSize, Math.Floor(c.Stockpile.GetValueOrDefault(t.Key)));
            var b = at != null && CanUseBase(c, at) && FreeCapacity(at) >= planes ? at : BestBase(c, planes) ?? BestBase(c)!;
            planes = Math.Min(planes, Math.Max(1, Math.Floor(FreeCapacity(b))));
            c.Stockpile[t.Key] -= planes;
            CreateWing(c, t, b, planes);
            return null;
        }
        if (_game.Features.Has(FeatureSet.Economy)) c.Gold -= t.Cost;
        c.Manpower -= t.Manpower;
        int hours = Math.Max(24, t.BuildDays * 24);
        OrdersOf(c).Add(new WingOrder { Type = t, Base = at ?? BestBase(c)!, HoursLeft = hours, TotalHours = hours });
        return null;
    }

    // ------------------------------------------------------------------------------------- game start
    /// <summary>New game: starting wings from history/air/*.txt: <c>TAG = { wing = { type = fighter base = state planes = 50 } }</c></summary>
    public void OnGameStart()
    {
        _ = Regions;
        foreach (var root in Burger.Core.Data.DataLoader.ParseFileOrFolder(_game.Data, "history/air"))
            foreach (var cn in root.All)
            {
                var c = _game.World.GetCountry(cn.Key);
                if (c == null || cn.Children == null) { Log.Error($"Unknown country '{cn.Key}' in history/air", cn.Location); continue; }
                foreach (var wn in cn.GetAll("wing"))
                {
                    if (!_game.Data.PlaneTypes.TryGetValue(wn.GetString("type") ?? "", out var t)) { Log.Error($"Unknown plane type '{wn.GetString("type")}'", wn.Location); continue; }
                    var b = _game.World.GetState(wn.GetString("base"));
                    if (b != null && !CanUseBase(c, b)) { Log.Warn($"{c.Tag}: '{b.Key}' has no air base it can use", wn.Location); b = null; }
                    b ??= BestBase(c);
                    if (b == null) { Log.Warn($"{c.Tag} has no air base for its {t.Name} wing", wn.Location); continue; }
                    CreateWing(c, t, b, wn.GetDouble("planes", WingSize), wn.GetString("name"));
                }
            }
    }

    // ------------------------------------------------------------------------------------- AI
    /// <summary>Builds planes (production lines or gold) and new wings.</summary>
    public void AiMonthly(Country c)
    {
        var types = _game.Data.PlaneTypes.Values.ToList();
        if (types.Count == 0 || BestBase(c) == null) return;
        var fighter = types.Where(t => DefaultMission(t) == AirMission.Superiority).OrderBy(t => t.ProductionCost).FirstOrDefault();
        var cas = types.Where(t => DefaultMission(t) == AirMission.CloseSupport).OrderByDescending(t => t.Stat("ground_attack")).FirstOrDefault();
        int wings = WingsOf(c).Count();
        int want = 1 + c.OwnedStates.Count / 4;
        if (_game.Production.Enabled)
        {
            foreach (var t in new[] { fighter, cas })
                if (t != null && _game.Data.Equipment.TryGetValue(t.Key, out var e) && !c.ProductionLines.Any(l => l.Equipment == e))
                    _game.Production.AddLine(c, e, 0);
            foreach (var t in new[] { fighter, cas })
                if (t != null && wings < want && c.Stockpile.GetValueOrDefault(t.Key) >= WingSize * 0.5 && CannotBuildReason(c, t) == null) { BuildWing(c, t); wings++; }
        }
        else if (fighter != null && OrdersOf(c).Count == 0 && wings < want && CannotBuildReason(c, fighter) == null && (!_game.Features.Has(FeatureSet.Economy) || c.Gold > fighter.Cost * 3))
            BuildWing(c, wings % 3 == 2 && cas != null && CannotBuildReason(c, cas) == null ? cas : fighter);
    }

    /// <summary>Assigns missions: at war, fighters and CAS go where the armies fight; bombers to enemy factories in range.</summary>
    public void AiWeekly(Country c)
    {
        var enemies = _game.World.WarsOf(c).SelectMany(w => w.EnemiesOf(c)).ToHashSet();
        var home = RegionOf(c.Capital?.State);
        // regions where our divisions fight, most battles first; else regions where we border an enemy
        var hot = _game.World.Battles.Where(b => b.Attackers.Concat(b.Defenders).Any(d => d.Owner == c) && b.Location.State != null)
            .GroupBy(b => RegionOf(b.Location.State)).Where(g => g.Key != null).OrderByDescending(g => g.Count()).Select(g => g.Key!).ToList();
        if (hot.Count == 0 && enemies.Count > 0)
            hot = Regions.Where(r => r.States.Any(s => s.Owner == c) && r.States.Any(s => s.Owner != null && enemies.Contains(s.Owner))).ToList();
        foreach (var w in WingsOf(c))
        {
            AirRegion? target = null;
            var m = DefaultMission(w.Type);
            if (enemies.Count == 0) { target = home; m = CanFly(w.Type, AirMission.Superiority) ? AirMission.Superiority : AirMission.None; }
            else if (m == AirMission.Bombing)
                target = Regions.Where(r => InRange(w, r) && r.States.Any(s => s.Owner != null && enemies.Contains(s.Owner)))
                    .OrderByDescending(r => r.States.Where(s => s.Owner != null && enemies.Contains(s.Owner)).Sum(s => s.Working("military_factory") + s.Working("civilian_factory"))).FirstOrDefault();
            else target = hot.FirstOrDefault(r => InRange(w, r)) ?? (home != null && InRange(w, home) ? home : null);
            if (target == null || m == AirMission.None) { if (w.Region != null) Assign(w, null, AirMission.None); continue; }
            if (w.Region != target || w.Mission != m) Assign(w, target, m);
        }
        // wings far from the action move to a base closer to it
        if (hot.Count > 0)
            foreach (var w in WingsOf(c).Where(w => w.Region == null && w.Mission == AirMission.None).ToList())
            {
                var near = _game.World.States.Where(s => CanUseBase(c, s) && FreeCapacity(s, w) >= w.Planes && hot.Any(r => Rules.DistanceKm(_game, s, r) <= w.Type.Range))
                    .OrderBy(s => Rules.DistanceKm(_game, s, hot[0])).FirstOrDefault();
                if (near != null) Rebase(w, near);
            }
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        root.Add("next_wing", I(_nextWing));
        foreach (var w in Wings)
        {
            var n = root.AddBlock("wing");
            n.Add("num", I(w.Id));
            n.Add("name", w.Name, true);
            n.Add("owner", w.Owner.Tag);
            n.Add("type", w.Type.Key);
            n.Add("planes", F(w.Planes));
            n.Add("base", w.Base.Key);
            if (w.Region != null) n.Add("region", w.Region.Key);
            n.Add("mission", w.Mission.ToString());
        }
        foreach (var (c, orders) in Orders)
            foreach (var o in orders)
            {
                var n = root.AddBlock("order");
                n.Add("country", c.Tag);
                n.Add("type", o.Type.Key);
                n.Add("base", o.Base.Key);
                n.Add("hours", I(o.HoursLeft));
                n.Add("total", I(o.TotalHours));
            }
        foreach (var s in _game.World.States.Where(s => s.Damaged.Count > 0))
        {
            var n = root.AddBlock("damaged");
            n.Add("state", s.Key);
            foreach (var (b, v) in s.Damaged) n.Add(b, F(v));
        }
    }

    private void Reset()
    {
        Wings.Clear();
        Orders.Clear();
        foreach (var s in _game.World.States) s.Damaged.Clear();
        _nextWing = 1;
        _powerDirty = true;
    }

    private void Load(Node root)
    {
        _nextWing = root.GetInt("next_wing", 1);
        foreach (var n in root.GetAll("wing"))
        {
            var owner = _game.World.GetCountry(n.GetString("owner"));
            var b = _game.World.GetState(n.GetString("base"));
            if (owner == null || b == null || !_game.Data.PlaneTypes.TryGetValue(n.GetString("type") ?? "", out var t)) continue;
            Wings.Add(new AirWing
            {
                Id = n.GetInt("num"), Name = n.GetString("name", t.Name)!, Owner = owner, Type = t, Base = b,
                Planes = n.GetDouble("planes", WingSize), Region = GetRegion(n.GetString("region")),
                Mission = Enum.TryParse<AirMission>(n.GetString("mission"), true, out var m) ? m : AirMission.None,
            });
        }
        foreach (var n in root.GetAll("order"))
        {
            var c = _game.World.GetCountry(n.GetString("country"));
            var b = _game.World.GetState(n.GetString("base"));
            if (c == null || b == null || !_game.Data.PlaneTypes.TryGetValue(n.GetString("type") ?? "", out var t)) continue;
            OrdersOf(c).Add(new WingOrder { Type = t, Base = b, HoursLeft = n.GetInt("hours"), TotalHours = n.GetInt("total") });
        }
        foreach (var n in root.GetAll("damaged"))
        {
            var s = _game.World.GetState(n.GetString("state"));
            if (s == null) continue;
            foreach (var v in n.All.Where(x => x.Key != "state" && x.Value != null)) s.Damaged[v.Key] = v.AsDouble();
        }
        _nextWing = Math.Max(_nextWing, Wings.Select(w => w.Id + 1).DefaultIfEmpty(1).Max());
        _powerDirty = true;
    }
}
