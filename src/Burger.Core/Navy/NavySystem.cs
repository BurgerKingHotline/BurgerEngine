using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Navy;

/// <summary>Overridable naval rules. Subclass and assign to <c>Game.Rules.Navy</c> (the navy feature puts a copy in backend/Navy.cs).</summary>
public class NavyRules
{
    /// <summary>Can fleets of <paramref name="c"/> use this land location as a port? (own or allied coastal land)</summary>
    public virtual bool IsPort(Game game, Country c, Location l)
    {
        if (!l.IsLand || !l.IsCoastal || l.Controller == null) return false;
        return l.Controller == c || game.World.Relations.AreAllied(c, l.Controller) || game.World.Relations.HasAccess(c, l.Controller) && !game.World.AtWar(c, l.Controller);
    }

    /// <summary>Damage one ship does to another in one hour of battle.</summary>
    public virtual double Damage(Game game, Ship shooter, Ship target)
    {
        bool capitalTarget = target.Type.Category.Equals("capital", StringComparison.OrdinalIgnoreCase);
        double attack = capitalTarget ? Math.Max(shooter.Type.Stat("heavy_attack"), shooter.Type.Stat("light_attack") * 0.3) : shooter.Type.Stat("light_attack") + shooter.Type.Stat("heavy_attack") * 0.5;
        double armor = target.Type.Stat("armor");
        double hit = game.Rng.NextDouble() < 0.35 ? 1 : 0;
        return attack * hit * (0.5 + game.Rng.NextDouble()) * 10 / (10 + armor) * shooter.HpFraction;
    }

    /// <summary>Chooses what a ship shoots at: screens shield capital ships while they last.</summary>
    public virtual Ship? PickTarget(Game game, Ship shooter, List<Ship> enemies)
    {
        var alive = enemies.Where(s => s.Hp > 0).ToList();
        if (alive.Count == 0) return null;
        var screens = alive.Where(s => s.Type.Category.Equals("screen", StringComparison.OrdinalIgnoreCase)).ToList();
        bool heavy = shooter.Type.Stat("heavy_attack") > shooter.Type.Stat("light_attack");
        var pool = heavy ? alive.Where(s => s.Type.Category.Equals("capital", StringComparison.OrdinalIgnoreCase)).ToList() : screens;
        // capital ships can only be reached once most screens are gone (screening)
        if (!heavy && pool.Count == 0) pool = alive;
        if (heavy && pool.Count == 0) pool = alive;
        if (!heavy && screens.Count > 0 && game.Rng.NextDouble() < 0.85) pool = screens;
        return pool[game.Rng.Next(pool.Count)];
    }

    /// <summary>A side whose ships have less than this share of their hit points left retreats.</summary>
    public virtual double RetreatBelow(Game game) => game.Data.Defines.Get("naval_retreat", 0.4);

    /// <summary>Share of hit points repaired per day in port.</summary>
    public virtual double RepairPerDay(Game game, Fleet f)
    {
        double bases = f.Location.State?.Buildings.GetValueOrDefault("naval_base") ?? 0;
        return 0.03 + 0.02 * bases;
    }

    /// <summary>Divisions a country can carry over sea at once (transport ships).</summary>
    public virtual double TransportCapacity(Game game, Country c)
        => game.Navy.FleetsOf(c).SelectMany(f => f.Ships).Sum(s => s.Type.Stat("transport"));

    /// <summary>Hours an army needs to cross one sea step.</summary>
    public virtual double SeaStepHours(Game game, Adjacency adj) => Math.Max(6, adj.DistanceKm / game.Data.Defines.Get("transport_speed", 12));

    /// <summary>Combat multiplier of divisions that attack from the sea (naval invasion).</summary>
    public virtual double InvasionFactor(Game game) => game.Data.Defines.Get("naval_invasion_penalty", 0.5);

    /// <summary>Share of strength unescorted armies at sea lose per hour when an enemy fleet is in the same sea.</summary>
    public virtual double ConvoyLosses(Game game) => 0.15;
}

/// <summary>
/// Fleets, naval battles, ship building and armies at sea (navy feature).
/// Fleets move through sea locations; when fleets of countries at war meet, they fight until one side retreats.
/// With transport ships a country's land path finding may cross the sea; landing on an enemy coast is a naval invasion.
/// </summary>
public sealed class NavySystem
{
    private readonly Game _game;
    public readonly List<Fleet> Fleets = new();
    public readonly List<NavalBattle> Battles = new();
    public readonly Dictionary<Country, List<ShipOrder>> ShipOrders = new();
    private int _nextFleet = 1, _nextShip = 1, _nextBattle = 1;
    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public int BattlesFought, ShipsSunk, Embarked;

    public NavySystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "navy", Write = Save, Read = Load, Reset = Reset });
        game.Warfare.CombatFactorSources.Add(d => Enabled && d.Location.IsWater ? Rules.InvasionFactor(_game) : 1);
    }

    private NavyRules Rules => _game.Rules.Navy;
    public bool Enabled => _game.Features.Has(FeatureSet.Navy);

    public IEnumerable<Fleet> FleetsOf(Country c) => Fleets.Where(f => f.Owner == c);
    public IEnumerable<Fleet> FleetsAt(Location l) => Fleets.Where(f => f.Location == l);
    public List<ShipOrder> OrdersOf(Country c) => ShipOrders.TryGetValue(c, out var l) ? l : ShipOrders[c] = new List<ShipOrder>();
    public bool IsPort(Country c, Location l) => Rules.IsPort(_game, c, l);

    // ------------------------------------------------------------------------------------- transport
    public double TransportCapacity(Country c) => Enabled ? Rules.TransportCapacity(_game, c) : 0;
    public int DivisionsAtSea(Country c) => _game.World.Divisions.Count(d => d.Owner == c && d.Location.IsWater);
    /// <summary>May the country's land path finding use the sea?</summary>
    public bool CanUseSea(Country c) => Enabled && TransportCapacity(c) > 0;

    /// <summary>Can this division step onto the sea now (free transport capacity, or already at sea)?</summary>
    public bool CanCarry(Division d) => d.Location.IsWater || DivisionsAtSea(d.Owner) < TransportCapacity(d.Owner);

    public double SeaStepHours(Adjacency adj) => Rules.SeaStepHours(_game, adj);

    // ------------------------------------------------------------------------------------- fleets
    public Fleet CreateFleet(Country c, Location at, string? name = null)
    {
        int n = FleetsOf(c).Count() + 1;
        var f = new Fleet { Id = _nextFleet++, Name = name ?? $"{Burger.Core.Warfare.WarfareSystem.Ordinal(n)} Fleet", Owner = c, Location = at, HomePort = at.IsLand ? at : null };
        Fleets.Add(f);
        return f;
    }

    public Ship AddShip(Fleet f, ShipType t, double hpShare = 1)
    {
        int same = FleetsOf(f.Owner).SelectMany(x => x.Ships).Count(s => s.Type == t) + 1;
        var s = new Ship { Id = _nextShip++, Type = t, Name = $"{t.Name} {same}", Hp = t.Hp * Math.Clamp(hpShare, 0.01, 1) };
        f.Ships.Add(s);
        return s;
    }

    public void DisbandFleet(Fleet f)
    {
        Fleets.Remove(f);
        f.Battle?.SideA.Remove(f);
        f.Battle?.SideB.Remove(f);
    }

    /// <summary>Moves every ship of <paramref name="other"/> into <paramref name="into"/> (same location).</summary>
    public bool Merge(Fleet into, Fleet other)
    {
        if (into == other || into.Owner != other.Owner || into.Location != other.Location || other.Battle != null) return false;
        into.Ships.AddRange(other.Ships);
        Fleets.Remove(other);
        return true;
    }

    /// <summary>The port where a country's new ships appear: the capital if it is a port, else the best own port.</summary>
    private readonly Dictionary<Country, (int Day, Location? Port)> _mainPort = new();

    /// <summary>The port where a country's new ships appear: the capital if it is a port, else the home port nearest the capital
    /// (land the country has cores on first, so the British fleet lives in Britain and not in a colony).</summary>
    public Location? MainPort(Country c)
    {
        int day = _game.World.Date.TotalDays;
        if (_mainPort.TryGetValue(c, out var cached) && cached.Day == day && (cached.Port == null || IsPort(c, cached.Port))) return cached.Port;
        Location? best = null;
        if (c.Capital != null && IsPort(c, c.Capital) && c.Capital.Owner == c) best = c.Capital;
        else
        {
            var cap = c.Capital ?? c.OwnedStates.SelectMany(s => s.Locations).FirstOrDefault();
            double bestScore = double.MaxValue;
            foreach (var s in c.OwnedStates)
            {
                bool core = s.Cores.Contains(c);
                foreach (var l in s.Locations)
                {
                    if (l.Owner != c || !IsPort(c, l)) continue;
                    double d = cap == null ? 0 : Dist(l, cap);
                    double score = d * (core ? 1 : 4) - 15 * (s.Buildings.GetValueOrDefault("naval_base") + s.Buildings.GetValueOrDefault("dockyard"));
                    if (score < bestScore) { bestScore = score; best = l; }
                }
            }
        }
        _mainPort[c] = (day, best);
        return best;
    }

    /// <summary>Puts a new ship in the country's fleet at its main port (joins a fleet waiting there, else the
    /// country's biggest fleet in port, else a new fleet).</summary>
    public Ship? Commission(Country c, ShipType t)
    {
        var port = MainPort(c);
        if (port == null) return null;
        var fleet = FleetsAt(port).Where(f => f.Owner == c && f.Battle == null && !f.IsMoving).OrderByDescending(f => f.Ships.Count).FirstOrDefault()
                    ?? CreateFleet(c, port, "Reserve Fleet");
        return AddShip(fleet, t);
    }

    // ------------------------------------------------------------------------------------- movement
    /// <summary>Path through sea locations (start and end may be ports). Null if unreachable.</summary>
    public List<Location>? FindSeaPath(Country c, Location from, Location to)
    {
        if (from == to) return new List<Location>();
        if (to.IsLand && !IsPort(c, to)) return null;
        var cost = new Dictionary<Location, double> { [from] = 0 };
        var came = new Dictionary<Location, Location>();
        var open = new PriorityQueue<Location, double>();
        open.Enqueue(from, 0);
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == to) break;
            foreach (var adj in cur.Neighbors)
            {
                var n = adj.To;
                if (n.IsLand && n != to) continue;          // only the destination port may be land
                if (cur.IsLand && !n.IsWater) continue;      // leaving a port goes to sea
                if (n.IsWater && n.Impassable) continue;
                double nc = cost[cur] + adj.DistanceKm;
                if (cost.TryGetValue(n, out var old) && old <= nc) continue;
                cost[n] = nc;
                came[n] = cur;
                open.Enqueue(n, nc);
            }
        }
        if (!came.ContainsKey(to)) return null;
        var path = new List<Location>();
        for (var l = to; l != from; l = came[l]) path.Add(l);
        path.Reverse();
        return path;
    }

    public bool OrderMove(Fleet f, Location target)
    {
        if (f.Battle != null) return false;
        var path = FindSeaPath(f.Owner, f.Location, target);
        if (path == null) return false;
        if (f.Path.Count == 0 || path.Count == 0 || f.Path[0] != path[0]) f.MoveProgress = 0;
        f.Path = path;
        f.Retreating = false;
        return true;
    }

    private void MoveFleets()
    {
        foreach (var f in Fleets.ToList())
        {
            if (f.Battle != null || f.Path.Count == 0 || f.Ships.Count == 0) continue;
            var next = f.Path[0];
            var adj = f.Location.AdjacencyTo(next);
            if (adj == null || next.IsLand && !IsPort(f.Owner, next)) { f.Path.Clear(); continue; }
            f.MoveProgress += f.Speed / Math.Max(1, adj.DistanceKm);
            if (f.MoveProgress < 1) continue;
            f.MoveProgress = 0;
            f.Location = next;
            f.Path.RemoveAt(0);
            if (f.Path.Count == 0) f.Retreating = false;
        }
    }

    // ------------------------------------------------------------------------------------- battles
    private bool Hostile(Country a, Country b) => a != b && _game.World.AtWar(a, b);

    private void StartBattles()
    {
        foreach (var group in Fleets.Where(f => f.Location.IsWater && f.Battle == null && !f.Retreating && f.Ships.Count > 0).GroupBy(f => f.Location))
        {
            var fleets = group.ToList();
            foreach (var a in fleets)
            {
                if (a.Battle != null) continue;
                var enemy = fleets.FirstOrDefault(b => b.Battle == null && Hostile(a.Owner, b.Owner));
                if (enemy == null) continue;
                var battle = new NavalBattle { Id = _nextBattle++, Location = group.Key, CountryA = a.Owner, CountryB = enemy.Owner };
                foreach (var f in fleets.Where(f => f.Battle == null))
                {
                    bool sideA = f.Owner == a.Owner || (!Hostile(f.Owner, a.Owner) && Hostile(f.Owner, enemy.Owner));
                    bool sideB = f.Owner == enemy.Owner || (!Hostile(f.Owner, enemy.Owner) && Hostile(f.Owner, a.Owner));
                    if (sideA) battle.SideA.Add(f); else if (sideB) battle.SideB.Add(f); else continue;
                    f.Battle = battle;
                    f.Path.Clear();
                }
                Battles.Add(battle);
                BattlesFought++;
                if (a.Owner.IsPlayer || enemy.Owner.IsPlayer)
                    _game.Notify($"Naval battle at {group.Key.DisplayName}: {a.Owner.Name} against {enemy.Owner.Name}.", NotificationKind.War, group.Key);
            }
        }
    }

    private void TickBattle(NavalBattle b)
    {
        b.Hours++;
        var shipsA = b.SideA.SelectMany(f => f.Ships).ToList();
        var shipsB = b.SideB.SelectMany(f => f.Ships).ToList();
        foreach (var s in shipsA.Where(s => s.Hp > 0)) Fire(s, shipsB);
        foreach (var s in shipsB.Where(s => s.Hp > 0)) Fire(s, shipsA);
        int sa = SinkDead(b.SideA), sb = SinkDead(b.SideB);
        b.SunkA += sa;
        b.SunkB += sb;
        ShipsSunk += sa + sb;
        double hpA = Share(b.SideA), hpB = Share(b.SideB);
        double retreat = Rules.RetreatBelow(_game);
        bool aGone = b.SideA.All(f => f.Ships.Count == 0) || hpA < retreat;
        bool bGone = b.SideB.All(f => f.Ships.Count == 0) || hpB < retreat;
        if (b.Hours > 24 * 7) { aGone = hpA <= hpB; bGone = !aGone; }
        if (!aGone && !bGone) return;
        b.Finished = true;
        Battles.Remove(b);
        foreach (var f in b.SideA.Concat(b.SideB)) f.Battle = null;
        if (aGone) foreach (var f in b.SideA) RetreatHome(f);
        if (bGone) foreach (var f in b.SideB) RetreatHome(f);
        var winner = aGone && !bGone ? b.CountryB : !aGone && bGone ? b.CountryA : null;
        if (winner != null)
        {
            var loser = winner == b.CountryA ? b.CountryB : b.CountryA;
            var war = _game.World.WarBetween(winner, loser);
            if (war != null)
            {
                int sunk = winner == b.CountryA ? b.SunkB : b.SunkA;
                double delta = 1 + sunk;
                war.BattleScore += war.IsAttacker(winner) ? delta : -delta;
            }
            if (winner.IsPlayer || loser.IsPlayer)
                _game.Notify($"Naval battle at {b.Location.DisplayName}: {(winner.IsPlayer ? "victory" : "defeat")} ({b.SunkA + b.SunkB} ships sunk).", winner.IsPlayer ? NotificationKind.Good : NotificationKind.Bad, b.Location);
        }
    }

    private void Fire(Ship shooter, List<Ship> enemies)
    {
        var target = Rules.PickTarget(_game, shooter, enemies);
        if (target != null) target.Hp -= Rules.Damage(_game, shooter, target);
    }

    private int SinkDead(List<Fleet> side)
    {
        int n = 0;
        foreach (var f in side)
            n += f.Ships.RemoveAll(s => s.Hp <= 0);
        foreach (var f in side.Where(f => f.Ships.Count == 0).ToList()) Fleets.Remove(f);
        return n;
    }

    private static double Share(List<Fleet> side)
    {
        double max = side.SelectMany(f => f.Ships).Sum(s => s.Type.Hp);
        return max <= 0 ? 0 : side.SelectMany(f => f.Ships).Sum(s => s.Hp) / max;
    }

    public void RetreatHome(Fleet f)
    {
        f.Retreating = true;
        var home = MainPort(f.Owner) ?? (f.HomePort != null && IsPort(f.Owner, f.HomePort) ? f.HomePort : null);
        if (home != null) f.Path = FindSeaPath(f.Owner, f.Location, home) ?? new List<Location>();
        f.MoveProgress = 0;
    }

    /// <summary>Unescorted armies at sea next to enemy fleets are sunk.</summary>
    private void Convoys()
    {
        foreach (var g in _game.World.Divisions.Where(d => d.Location.IsWater).GroupBy(d => (d.Location, d.Owner)).ToList())
        {
            var (loc, owner) = g.Key;
            var here = FleetsAt(loc).ToList();
            bool enemy = here.Any(f => Hostile(f.Owner, owner));
            bool escort = here.Any(f => f.Owner == owner || _game.World.Relations.AreAllied(f.Owner, owner));
            if (!enemy || escort) continue;
            foreach (var d in g.ToList())
            {
                d.Strength -= d.MaxStrength * Rules.ConvoyLosses(_game);
                if (d.Strength > 0) continue;
                _game.NotifyPlayer($"{d.Name} was sunk at sea near {loc.DisplayName}!", NotificationKind.War, owner);
                _game.Warfare.DestroyDivision(d);
            }
        }
    }

    // ------------------------------------------------------------------------------------- ticks
    public void HourlyTick()
    {
        MoveFleets();
        StartBattles();
        foreach (var b in Battles.ToList()) TickBattle(b);
        Convoys();
    }

    public void DailyTick()
    {
        foreach (var f in Fleets)
        {
            if (f.Battle != null || !f.InPort || !IsPort(f.Owner, f.Location)) continue;
            double rep = Rules.RepairPerDay(_game, f);
            foreach (var s in f.Ships) s.Hp = Math.Min(s.Type.Hp, s.Hp + s.Type.Hp * rep);
        }
        // fleets in ports that are no longer friendly sail out
        foreach (var f in Fleets.Where(f => f.InPort && !IsPort(f.Owner, f.Location) && f.Path.Count == 0).ToList()) RetreatHome(f);
        // ships bought with gold
        foreach (var (c, orders) in ShipOrders)
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                o.HoursLeft -= 24;
                if (o.HoursLeft > 0) continue;
                orders.RemoveAt(i--);
                if (Commission(c, o.Type) is { } ship) _game.NotifyPlayer($"{ship.Name} has been launched.", NotificationKind.Good, c);
            }
        // ships built by dockyards (production): one per finished unit in the stockpile
        if (_game.Production.Enabled)
            foreach (var c in _game.World.Countries.Where(c => c.Exists))
                foreach (var t in _game.Data.ShipTypes.Values)
                    while (c.Stockpile.GetValueOrDefault(t.Key) >= 1)
                    {
                        if (Commission(c, t) == null) break;
                        c.Stockpile[t.Key] -= 1;
                        _game.NotifyPlayer($"A new {t.Name} has been launched.", NotificationKind.Good, c);
                    }
    }

    // ------------------------------------------------------------------------------------- building with gold
    public string? CannotBuyReason(Country c, ShipType t)
    {
        if (MainPort(c) == null) return "You have no port";
        if (_game.Features.Has(FeatureSet.Economy) && c.Gold < t.Cost) return $"Needs {t.Cost:0} gold";
        if (c.Manpower < t.Manpower) return $"Needs {t.Manpower:0} manpower";
        return null;
    }

    public string? BuyShip(Country c, ShipType t)
    {
        var reason = CannotBuyReason(c, t);
        if (reason != null) return reason;
        if (_game.Features.Has(FeatureSet.Economy)) c.Gold -= t.Cost;
        c.Manpower -= t.Manpower;
        int hours = Math.Max(24, t.BuildDays * 24);
        OrdersOf(c).Add(new ShipOrder { Type = t, Port = MainPort(c)!, HoursLeft = hours, TotalHours = hours });
        return null;
    }

    // ------------------------------------------------------------------------------------- game start
    /// <summary>New game: starting fleets from history/fleets/*.txt: <c>TAG = { fleet = { name = "Home Fleet" location = x ships = { destroyer = 2 } } }</c></summary>
    public void OnGameStart()
    {
        foreach (var root in Burger.Core.Data.DataLoader.ParseFileOrFolder(_game.Data, "history/fleets"))
            foreach (var cn in root.All)
            {
                var c = _game.World.GetCountry(cn.Key);
                if (c == null || cn.Children == null) { Log.Error($"Unknown country '{cn.Key}' in history/fleets", cn.Location); continue; }
                foreach (var fn in cn.GetAll("fleet"))
                {
                    var loc = _game.World.GetLocation(fn.GetString("location")) ?? MainPort(c);
                    if (loc == null) { Log.Warn($"{c.Tag} has no port for its fleet", fn.Location); continue; }
                    var fleet = CreateFleet(c, loc, fn.GetString("name"));
                    foreach (var s in fn.Get("ships")?.All ?? Enumerable.Empty<Node>())
                    {
                        if (!_game.Data.ShipTypes.TryGetValue(s.Key, out var t)) { Log.Error($"Unknown ship type '{s.Key}'", s.Location); continue; }
                        for (int i = 0; i < (int)s.AsDouble(1); i++) AddShip(fleet, t);
                    }
                    if (fleet.Ships.Count == 0) Fleets.Remove(fleet);
                }
            }
    }

    // ------------------------------------------------------------------------------------- AI
    public void AiMonthly(Country c)
    {
        var w = _game.World;
        bool atWar = w.WarsOf(c).Any();
        var types = _game.Data.ShipTypes.Values.ToList();
        if (types.Count == 0 || MainPort(c) == null) return;
        var screen = types.Where(t => t.Category.Equals("screen", StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.ProductionCost).FirstOrDefault();
        var transport = types.FirstOrDefault(t => t.Stat("transport") > 0);
        int ships = FleetsOf(c).Sum(f => f.Ships.Count);
        int cap = Math.Min(150, 6 + c.OwnedStates.Count(s => s.Locations.Any(l => l.IsCoastal)) / 2); // a navy fitting the coastline
        if (_game.Production.Enabled && ships >= cap)
        {
            foreach (var l in c.ProductionLines.Where(l => _game.Data.ShipTypes.ContainsKey(l.Equipment.Key)).ToList()) _game.Production.RemoveLine(c, l);
            return;
        }
        if (_game.Production.Enabled)
        {
            // production lines for ships: a screen line and a transport line if needed
            if (_game.Production.Dockyards(c) > 0)
            {
                foreach (var t in new[] { screen, TransportCapacity(c) < 2 ? transport : null })
                    if (t != null && _game.Data.Equipment.TryGetValue(t.Key, out var e) && !c.ProductionLines.Any(l => l.Equipment == e))
                        _game.Production.AddLine(c, e, 0);
            }
        }
        else if (screen != null && OrdersOf(c).Count == 0 && ships < Math.Min(cap, 2 + c.OwnedStates.Count / 3) && CannotBuyReason(c, screen) == null && (!_game.Features.Has(FeatureSet.Economy) || c.Gold > screen.Cost * 3))
            BuyShip(c, transport != null && TransportCapacity(c) < 2 ? transport : screen);
    }

    public void AiDaily(Country c)
    {
        // fleets of ours idling in the same place join up (stops a new fleet per launched ship)
        foreach (var g in FleetsOf(c).Where(f => f.Battle == null && !f.IsMoving).GroupBy(f => f.Location).Where(g => g.Count() > 1).ToList())
        {
            var main = g.OrderByDescending(f => f.Ships.Count).First();
            foreach (var other in g.Where(f => f != main).ToList()) Merge(main, other);
        }
        var enemies = _game.World.WarsOf(c).SelectMany(w => w.EnemiesOf(c)).ToHashSet();
        List<Location>? enemyCoast = null; // sea next to enemy land, found once per call
        foreach (var f in FleetsOf(c).ToList())
        {
            if (f.Battle != null || f.IsMoving) continue;
            if (f.InPort && f.Ships.Count < 3 && f.Ships.Count < FleetsOf(c).Sum(x => x.Ships.Count)) continue; // small new fleets wait to be joined
            if (f.HpFraction < 0.6) { if (!f.InPort) RetreatHome(f); continue; }
            if (enemies.Count == 0) { if (!f.InPort) RetreatHome(f); continue; }
            if (!f.InPort && f.Location.IsWater) continue; // already patrolling
            // patrol the sea next to the nearest enemy coast (or an enemy fleet we can beat)
            double power = f.Ships.Sum(s => s.Type.Stat("light_attack") + s.Type.Stat("heavy_attack")) * f.HpFraction;
            var target = Fleets.Where(e => enemies.Contains(e.Owner) && e.Location.IsWater && e.Battle == null)
                .Where(e => e.Ships.Sum(s => s.Type.Stat("light_attack") + s.Type.Stat("heavy_attack")) * e.HpFraction < power * 0.9)
                .Select(e => e.Location).FirstOrDefault();
            enemyCoast ??= _game.World.Locations.Where(l => l.IsWater && l.Neighbors.Any(a => a.To.Controller != null && enemies.Contains(a.To.Controller))).ToList();
            target ??= enemyCoast.OrderBy(l => Dist(l, f.Location)).FirstOrDefault();
            if (target != null) OrderMove(f, target);
        }
    }

    private static double Dist(Location a, Location b) => Math.Sqrt((a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (a.CenterY - b.CenterY) * (a.CenterY - b.CenterY));

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        root.Add("next_fleet", I(_nextFleet));
        root.Add("next_ship", I(_nextShip));
        foreach (var f in Fleets)
        {
            var n = root.AddBlock("fleet");
            n.Add("num", I(f.Id));
            n.Add("name", f.Name, true);
            n.Add("owner", f.Owner.Tag);
            n.Add("location", f.Location.Key);
            if (f.HomePort != null) n.Add("home", f.HomePort.Key);
            n.Add("mission", f.Mission.ToString());
            if (f.Retreating) n.Add("retreating", "yes");
            if (f.Path.Count > 0) { var p = n.AddBlock("path"); foreach (var l in f.Path) p.AddItem(l.Key); n.Add("progress", F(f.MoveProgress)); }
            foreach (var s in f.Ships)
            {
                var sn = n.AddBlock("ship");
                sn.Add("num", I(s.Id));
                sn.Add("name", s.Name, true);
                sn.Add("type", s.Type.Key);
                sn.Add("hp", F(s.Hp));
            }
        }
        foreach (var (c, orders) in ShipOrders)
            foreach (var o in orders)
            {
                var n = root.AddBlock("order");
                n.Add("country", c.Tag);
                n.Add("type", o.Type.Key);
                n.Add("port", o.Port.Key);
                n.Add("hours", I(o.HoursLeft));
                n.Add("total", I(o.TotalHours));
            }
    }

    private void Reset()
    {
        Fleets.Clear();
        Battles.Clear();
        ShipOrders.Clear();
        _nextFleet = _nextShip = _nextBattle = 1;
    }

    private void Load(Node root)
    {
        _nextFleet = root.GetInt("next_fleet", 1);
        _nextShip = root.GetInt("next_ship", 1);
        foreach (var n in root.GetAll("fleet"))
        {
            var owner = _game.World.GetCountry(n.GetString("owner"));
            var loc = _game.World.GetLocation(n.GetString("location"));
            if (owner == null || loc == null) continue;
            var f = new Fleet
            {
                Id = n.GetInt("num"), Name = n.GetString("name", "Fleet")!, Owner = owner, Location = loc,
                HomePort = _game.World.GetLocation(n.GetString("home")),
                Mission = Enum.TryParse<FleetMission>(n.GetString("mission"), true, out var m) ? m : FleetMission.Hold,
                Retreating = n.GetBool("retreating"), MoveProgress = n.GetDouble("progress"),
            };
            foreach (var lk in n.GetList("path")) if (_game.World.GetLocation(lk) is { } pl) f.Path.Add(pl);
            foreach (var sn in n.GetAll("ship"))
                if (_game.Data.ShipTypes.TryGetValue(sn.GetString("type") ?? "", out var t))
                    f.Ships.Add(new Ship { Id = sn.GetInt("num"), Name = sn.GetString("name", t.Name)!, Type = t, Hp = sn.GetDouble("hp", t.Hp) });
            if (f.Ships.Count > 0) Fleets.Add(f);
        }
        foreach (var n in root.GetAll("order"))
        {
            var c = _game.World.GetCountry(n.GetString("country"));
            var port = _game.World.GetLocation(n.GetString("port"));
            if (c == null || port == null || !_game.Data.ShipTypes.TryGetValue(n.GetString("type") ?? "", out var t)) continue;
            OrdersOf(c).Add(new ShipOrder { Type = t, Port = port, HoursLeft = n.GetInt("hours"), TotalHours = n.GetInt("total") });
        }
        _nextFleet = Math.Max(_nextFleet, Fleets.Select(f => f.Id + 1).DefaultIfEmpty(1).Max());
        _nextShip = Math.Max(_nextShip, Fleets.SelectMany(f => f.Ships).Select(s => s.Id + 1).DefaultIfEmpty(1).Max());
    }
}
