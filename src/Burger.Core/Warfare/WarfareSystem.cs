using Burger.Core.Model;

namespace Burger.Core.Warfare;

/// <summary>
/// Divisions, armies, generals, fronts, movement, battles and occupation (HOI4-style by default).
/// <list type="bullet">
/// <item>Divisions are grouped into <see cref="Army"/>s led by a general (a character).</item>
/// <item>An army can hold a front against an enemy country: its divisions spread along the border
/// automatically and, in <see cref="ArmyMode.Offensive"/>, attack the weakest enemy locations.</item>
/// <item>Divisions can also be moved by hand (right-click), EU4/Imperator style.</item>
/// </list>
/// </summary>
public sealed class WarfareSystem
{
    private readonly Game _game;
    private readonly Dictionary<Location, List<Division>> _byLocation = new();
    private readonly Dictionary<string, int> _nameCounters = new();

    public WarfareSystem(Game game) { _game = game; }

    private WarfareRules Rules => _game.Rules.Warfare;

    /// <summary>Extra combat multipliers features and scripts add (air superiority, amphibious landings...). Each returns a factor for a division.</summary>
    public readonly List<Func<Division, double>> CombatFactorSources = new();

    // =============================================================================================
    // Queries
    // =============================================================================================

    public bool IsHostile(Country a, Country b) => a != b && _game.World.AtWar(a, b);

    public bool CanEnter(Country c, Location l) => Rules.CanEnter(_game, c, l);

    public IReadOnlyList<Division> DivisionsAt(Location l)
        => _byLocation.TryGetValue(l, out var list) ? list : (IReadOnlyList<Division>)Array.Empty<Division>();

    public double DivisionPower(Division d) => Rules.DivisionPower(_game, d);

    /// <summary>Divisions in <paramref name="l"/> that would fight <paramref name="c"/> (at war, not retreating).</summary>
    public IEnumerable<Division> HostileDefendersAt(Country c, Location l)
    {
        if (l.IsWater) yield break; // armies at sea are only attacked by fleets
        foreach (var d in DivisionsAt(l))
            if (!d.Retreating && IsHostile(c, d.Owner)) yield return d;
    }

    public double HostilePowerAt(Country c, Location l) => HostileDefendersAt(c, l).Sum(DivisionPower);

    public Battle? BattleAt(Location l) => _game.World.Battles.FirstOrDefault(b => b.Location == l && !b.Finished);

    // =============================================================================================
    // Divisions & armies
    // =============================================================================================

    public Division CreateDivision(Country owner, DivisionTemplate t, Location loc, string? name = null)
    {
        var w = _game.World;
        if (name == null)
        {
            string key = owner.Tag + ":" + t.Key;
            int n = _nameCounters.TryGetValue(key, out var v) ? v + 1 : 1;
            _nameCounters[key] = n;
            name = $"{n}. {t.Name}";
        }
        var d = new Division
        {
            Id = w.NextDivisionId++,
            Name = name,
            Owner = owner,
            Template = t,
            Location = loc,
        };
        d.Strength = d.MaxStrength;
        d.Org = d.MaxOrg;
        w.Divisions.Add(d);
        AddToIndex(d);
        return d;
    }

    /// <summary>Re-adds a division loaded from a save (keeps its id and stats).</summary>
    public void AddLoadedDivision(Division d)
    {
        _game.World.Divisions.Add(d);
        AddToIndex(d);
    }

    public void DestroyDivision(Division d)
    {
        var w = _game.World;
        if (!w.Divisions.Remove(d)) return;
        RemoveFromIndex(d);
        d.Army?.Divisions.Remove(d);
        d.Army = null;
        if (d.Battle != null)
        {
            d.Battle.Attackers.Remove(d);
            d.Battle.Defenders.Remove(d);
            d.Battle = null;
        }
        _game.Mods.Invoke(m => m.OnDivisionDestroyed(d));
    }

    private void AddToIndex(Division d)
    {
        if (!_byLocation.TryGetValue(d.Location, out var list)) _byLocation[d.Location] = list = new List<Division>();
        list.Add(d);
    }

    private void RemoveFromIndex(Division d)
    {
        if (_byLocation.TryGetValue(d.Location, out var list)) list.Remove(d);
    }

    /// <summary>Moves a division to a location instantly (keeps the location index up to date).</summary>
    public void Teleport(Division d, Location to)
    {
        RemoveFromIndex(d);
        d.Location = to;
        d.MoveProgress = 0;
        AddToIndex(d);
    }

    public void RebuildIndex()
    {
        _byLocation.Clear();
        foreach (var d in _game.World.Divisions) AddToIndex(d);
    }

    public Army CreateArmy(Country owner, string? name = null, Character? general = null)
    {
        var w = _game.World;
        int n = w.Armies.Count(a => a.Owner == owner) + 1;
        var army = new Army
        {
            Id = w.NextArmyId++,
            Owner = owner,
            Name = name ?? $"{Ordinal(n)} Army",
        };
        w.Armies.Add(army);
        if (general != null) SetGeneral(army, general);
        return army;
    }

    public static string Ordinal(int n)
    {
        string suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + suffix;
    }

    public void AssignToArmy(Division d, Army? army)
    {
        if (d.Army == army) return;
        d.Army?.Divisions.Remove(d);
        d.Army = army;
        d.FrontPost = null;
        if (army != null)
        {
            army.Divisions.Add(d);
            army.FrontHash = 0; // force front re-assignment
        }
    }

    public void SetGeneral(Army army, Character? general)
    {
        if (general != null)
        {
            foreach (var other in _game.World.Armies.Where(a => a.General == general)) other.General = null;
            general.IsGeneral = true;
        }
        army.General = general;
    }

    public void DisbandArmy(Army army)
    {
        foreach (var d in army.Divisions.ToList()) AssignToArmy(d, null);
        _game.World.Armies.Remove(army);
    }

    /// <summary>Gives an army a front against an enemy (HOI4 battle plan). Null enemy clears the front.</summary>
    public void SetFront(Army army, Country? enemy, ArmyMode mode)
    {
        army.FrontEnemy = enemy;
        army.Mode = mode;
        army.MoveTarget = null;
        army.FrontHash = 0;
        foreach (var d in army.Divisions) { d.ManualOrder = false; d.FrontPost = null; }
        UpdateFront(army);
    }

    // =============================================================================================
    // Movement orders
    // =============================================================================================

    // connected land masses (passable land joined by land borders and straits), for a quick "unreachable" answer
    private int[]? _landComp;

    private int LandComponent(Location l)
    {
        if (_landComp == null || _landComp.Length != _game.World.Locations.Count)
        {
            var locs = _game.World.Locations;
            var comp = new int[locs.Count];
            int next = 0;
            var q = new Queue<Location>();
            foreach (var start in locs)
            {
                if (comp[start.Index] != 0 || !start.IsLand) continue;
                comp[start.Index] = ++next;
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    var cur = q.Dequeue();
                    foreach (var adj in cur.Neighbors)
                        if (adj.Kind != AdjacencyKind.Sea && adj.To.IsLand && comp[adj.To.Index] == 0) { comp[adj.To.Index] = next; q.Enqueue(adj.To); }
                }
            }
            _landComp = comp;
        }
        return l.Index < _landComp.Length ? _landComp[l.Index] : 0;
    }

    // path finding scratch arrays (reused between calls; a generation number marks what this call has touched)
    private double[] _pfCost = Array.Empty<double>();
    private int[] _pfCame = Array.Empty<int>(), _pfGen = Array.Empty<int>(), _pfEnterGen = Array.Empty<int>();
    private bool[] _pfEnter = Array.Empty<bool>();
    private int _pfGeneration;
    private readonly PriorityQueue<(Location Loc, double Cost), double> _pfOpen = new();

    // per country: connected regions of the land it may enter, rebuilt daily or when wars change.
    // Lets FindPath reject unreachable targets at once instead of searching the whole continent.
    private readonly Dictionary<Country, (int Day, int Wars, int[] Region)> _reach = new();

    private bool Reachable(Country c, Location from, Location to)
    {
        int day = _game.World.Date.TotalDays, wars = _game.World.Wars.Count;
        int n = _game.World.Locations.Count;
        if (!_reach.TryGetValue(c, out var r) || r.Day != day || r.Wars != wars || r.Region.Length != n)
        {
            var region = r.Region != null && r.Region.Length == n ? r.Region : new int[n];
            Array.Fill(region, -1);
            var locs = _game.World.Locations;
            var enter = new bool[n];
            for (int i = 0; i < n; i++) enter[i] = locs[i].IsPassable && CanEnter(c, locs[i]);
            var q = new Queue<Location>();
            int next = 0;
            foreach (var start in locs)
            {
                if (region[start.Index] != -1 || !enter[start.Index]) continue;
                int id = next++;
                region[start.Index] = id;
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    var l = q.Dequeue();
                    foreach (var adj in l.Neighbors)
                    {
                        var m = adj.To;
                        if (adj.Kind == AdjacencyKind.Sea || region[m.Index] != -1 || !enter[m.Index]) continue;
                        region[m.Index] = id;
                        q.Enqueue(m);
                    }
                }
            }
            r = (day, wars, region);
            _reach[c] = r;
        }
        int a = r.Region[from.Index], b = r.Region[to.Index];
        if (a < 0 || b < 0) return true; // standing on (or going to) land outside the regions: let the full search decide
        return a == b;
    }

    /// <summary>A* path through locations the country may enter. Null if unreachable.</summary>
    public List<Location>? FindPath(Country c, Location from, Location to, double speed = 4)
    {
        if (!_game.Profiling) return FindPathCore(c, from, to, speed);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var path = FindPathCore(c, from, to, speed);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var pm = _game.ProfileMs;
        string k = path != null ? "Path.Found" : "Path.Failed";
        pm[k] = pm.GetValueOrDefault(k) + ms;
        pm[k + "#"] = pm.GetValueOrDefault(k + "#") + 1;
        return path;
    }

    private List<Location>? FindPathCore(Country c, Location from, Location to, double speed)
    {
        if (from == to) return new List<Location>();
        if (!CanEnter(c, to)) return null;
        bool sea = _game.Navy.CanUseSea(c) || (_game.Navy.Enabled && from.IsWater); // transports: armies may cross the sea (and always get off it)
        if (!sea && from.IsLand && LandComponent(from) != LandComponent(to)) return null; // other continent/island without transports
        if (!sea && from.IsLand)
        {
            bool reach = true;
            _game.P("Path.Reach", () => reach = Reachable(c, from, to));
            if (!reach) { if (_game.Profiling) _game.ProfileMs["Path.Rejected#"] = _game.ProfileMs.GetValueOrDefault("Path.Rejected#") + 1; return null; } // walled off by land we may not enter
        }
        int n0 = _game.World.Locations.Count;
        if (_pfCost.Length != n0)
        {
            _pfCost = new double[n0]; _pfCame = new int[n0]; _pfGen = new int[n0]; _pfEnterGen = new int[n0]; _pfEnter = new bool[n0];
            _pfGeneration = 0;
        }
        int gen = ++_pfGeneration;
        bool Enter(Location l)
        {
            if (_pfEnterGen[l.Index] != gen) { _pfEnterGen[l.Index] = gen; _pfEnter[l.Index] = CanEnter(c, l); }
            return _pfEnter[l.Index];
        }
        double kmPerPx = _game.Data.Defines.KmPerPixel;
        // A* guidance: land steps cost about distance/speed (sea steps are faster). Weighted above 1 on land:
        // paths can be slightly longer than the very best one, but far fewer locations are searched.
        double heuristic = sea ? 0.3 : 1.5;
        var open = _pfOpen;
        open.Clear();
        _pfGen[from.Index] = gen; _pfCost[from.Index] = 0; _pfCame[from.Index] = -1;
        open.Enqueue((from, 0), 0);
        int expanded = 0;
        bool found = false;
        while (open.Count > 0 && expanded < 30000)
        {
            var (cur, queuedCost) = open.Dequeue();
            if (cur == to) { found = true; break; }
            if (queuedCost > _pfCost[cur.Index]) continue; // stale entry: a cheaper way here was found later
            expanded++;
            double cc = _pfCost[cur.Index];
            foreach (var adj in cur.Neighbors)
            {
                var n = adj.To;
                double step;
                if (adj.Kind == AdjacencyKind.Sea)
                {
                    // over the sea: only with transports, from water or coastal land, into water or a coastal destination
                    if (!sea) continue;
                    if (n.IsLand && (n.Impassable || n.Terrain.Impassable)) continue;
                    if (n.IsLand && n != to && !Enter(n)) continue;
                    if (n.IsWater && n.Impassable) continue;
                    step = _game.Navy.SeaStepHours(adj) * (cur.IsLand ? 2 : 1); // embarking takes time
                }
                else
                {
                    if (!n.IsPassable) continue;
                    if (n != to && !Enter(n)) continue;
                    step = adj.DistanceKm / speed * Math.Max(0.1, n.Terrain.MovementCost) * (adj.River ? 1.25 : 1) * (adj.Kind == AdjacencyKind.Strait ? 2 : 1);
                }
                double nc = cc + step;
                if (_pfGen[n.Index] == gen && _pfCost[n.Index] <= nc) continue;
                _pfGen[n.Index] = gen;
                _pfCost[n.Index] = nc;
                _pfCame[n.Index] = cur.Index;
                double hx = (n.CenterX - to.CenterX) * kmPerPx, hy = (n.CenterY - to.CenterY) * kmPerPx;
                open.Enqueue((n, nc), nc + Math.Sqrt(hx * hx + hy * hy) / speed * heuristic);
            }
        }
        if (!found && _pfGen[to.Index] != gen) return null;
        var locs = _game.World.Locations;
        var path = new List<Location>();
        for (int li = to.Index; li != from.Index && li >= 0; li = _pfCame[li]) path.Add(locs[li]);
        path.Reverse();
        return path;
    }

    /// <summary>Orders a division to move (attacking or occupying along the way). Returns false if no path.</summary>
    public bool OrderMove(Division d, Location target, bool manual = true)
    {
        if (d.Battle != null && d.AttackTarget != null) LeaveBattleAsAttacker(d);
        if (d.Battle != null) return false; // defenders cannot walk away from a battle
        var path = FindPath(d.Owner, d.Location, target, d.Stat("speed", 4));
        if (path == null) return false;
        // keep progress if the first step is unchanged
        if (d.Path.Count == 0 || path.Count == 0 || d.Path[0] != path[0]) d.MoveProgress = 0;
        d.Path = path;
        d.ManualOrder = manual && path.Count > 0;
        return true;
    }

    public int OrderMove(IEnumerable<Division> divisions, Location target)
    {
        int ok = 0;
        foreach (var d in divisions) if (OrderMove(d, target)) ok++;
        return ok;
    }

    /// <summary>Moves a whole army (cancels its front).</summary>
    public void MoveArmy(Army army, Location target)
    {
        army.FrontEnemy = null;
        army.MoveTarget = target;
        foreach (var d in army.Divisions) { d.FrontPost = null; OrderMove(d, target); }
    }

    public void StopDivision(Division d)
    {
        if (d.Battle != null && d.AttackTarget != null) LeaveBattleAsAttacker(d);
        d.Path.Clear();
        d.MoveProgress = 0;
        d.ManualOrder = false;
    }

    // =============================================================================================
    // Control
    // =============================================================================================

    public void SetController(Location l, Country? c, bool silent = false)
    {
        if (l.Controller == c) return;
        var old = l.Controller;
        l.Controller = c;
        l.SiegeProgress = 0;
        l.Sieger = null;
        _game.Mods.Invoke(m => m.OnLocationControllerChanged(l, old, c));
        if (!silent && c != null)
        {
            _game.Events.FireOnAction("on_location_occupied", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase)
            {
                ["location"] = l,
                ["old_controller"] = old,
            });
            if (l.Owner?.Capital == l && old != null && (old.IsPlayer || c.IsPlayer))
                _game.Notify($"{c.Name} has captured {l.DisplayName}, the capital of {l.Owner.Name}!", c.IsPlayer ? NotificationKind.Good : NotificationKind.Bad, l);
        }
        _game.RaiseMapChanged();
    }

    /// <summary>Who takes control when <paramref name="occupier"/> enters an enemy-held location: an allied owner gets it back.</summary>
    private Country OccupierFor(Country occupier, Location l)
    {
        var owner = l.Owner;
        if (owner != null && owner != occupier && l.Controller != null && IsHostile(owner, l.Controller) && !IsHostile(owner, occupier))
            return owner; // liberating an ally's land
        return occupier;
    }

    // =============================================================================================
    // Ticks
    // =============================================================================================

    public void HourlyTick()
    {
        MoveDivisions();
        var model = _game.Combat.Current(_game);
        foreach (var b in _game.World.Battles.ToList())
        {
            if (b.Finished) continue;
            b.Hours++;
            foreach (var d in b.Attackers) d.CombatFactor = Rules.CombatFactor(_game, d);
            foreach (var d in b.Defenders) d.CombatFactor = Rules.CombatFactor(_game, d);
            try { model.Tick(b, _game); }
            catch (Exception ex) { Log.ErrorOnce("combat-ex:" + model.Id, $"Combat model '{model.Id}' crashed: {ex.Message}"); }
            ProcessBattle(b);
        }
        Occupation();
    }

    public void DailyTick()
    {
        foreach (var d in _game.World.Divisions)
        {
            if (d.InBattle) continue;
            d.Org = Math.Min(d.MaxOrg, d.Org + Rules.OrgRecovery(_game, d));
        }
        var alive = new HashSet<Division>(_game.World.Divisions);
        foreach (var army in _game.World.Armies.ToList())
        {
            army.Divisions.RemoveAll(d => !alive.Contains(d));
            _game.P("Warfare.Fronts", () => UpdateFront(army));
            if (army.FrontEnemy != null && army.Mode == ArmyMode.Offensive && IsHostile(army.Owner, army.FrontEnemy)) _game.P("Warfare.Offensive", () => Offensive(army));
        }
    }

    private void MoveDivisions()
    {
        foreach (var d in _game.World.Divisions.ToList())
        {
            if (d.Battle != null || d.Path.Count == 0) continue;
            var next = d.Path[0];
            var adj = d.Location.AdjacencyTo(next);
            bool allowed = next.IsWater ? _game.Navy.Enabled && _game.Navy.CanCarry(d) : CanEnter(d.Owner, next);
            if (adj == null || !allowed)
            {
                d.Path.Clear();
                d.ManualOrder = false;
                d.MoveProgress = 0;
                continue;
            }
            if (!d.Retreating && HostileDefendersAt(d.Owner, next).Any())
            {
                StartOrJoinBattle(d, next);
                continue;
            }
            d.MoveProgress += 1.0 / (next.IsWater || d.Location.IsWater ? _game.Navy.SeaStepHours(adj) : Rules.MoveHours(_game, d, adj));
            if (d.MoveProgress < 1) continue;
            if (next.IsWater && d.Location.IsLand) _game.Navy.Embarked++;
            Teleport(d, next);
            d.Path.RemoveAt(0);
            if (d.Path.Count == 0)
            {
                d.ManualOrder = false;
                d.Retreating = false;
            }
            // reinforce a defending battle in this location
            var battle = BattleAt(next);
            if (battle != null && !d.Retreating && battle.Defenders.Count > 0 && !IsHostile(d.Owner, battle.Defenders[0].Owner)
                && IsHostile(d.Owner, battle.AttackerCountry))
            {
                battle.Defenders.Add(d);
                d.Battle = battle;
            }
        }
    }

    /// <summary>Instant occupation (occupation_hours = 0) or siege progress.</summary>
    private void Occupation()
    {
        int hoursNeeded = _game.Data.Defines.OccupationHours;
        var handled = new HashSet<Location>();
        foreach (var d in _game.World.Divisions)
        {
            if (d.Retreating || d.InBattle) continue;
            var l = d.Location;
            if (l.IsWater || handled.Contains(l)) continue;
            var ctrl = l.Controller;
            if (ctrl == null || !IsHostile(d.Owner, ctrl)) continue;
            if (HostileDefendersAt(d.Owner, l).Any()) continue;
            handled.Add(l);
            if (hoursNeeded <= 0)
            {
                SetController(l, OccupierFor(d.Owner, l));
                continue;
            }
            if (l.Sieger != d.Owner) { l.Sieger = d.Owner; l.SiegeProgress = 0; }
            int besiegers = DivisionsAt(l).Count(x => x.Owner == d.Owner && !x.Retreating);
            l.SiegeProgress += Math.Min(3, besiegers);
            if (l.SiegeProgress >= hoursNeeded * (1 + l.Fort)) SetController(l, OccupierFor(d.Owner, l));
        }
    }

    // =============================================================================================
    // Battles
    // =============================================================================================

    private void StartOrJoinBattle(Division attacker, Location target)
    {
        var battle = BattleAt(target);
        if (battle != null)
        {
            bool hostileToDefenders = battle.Defenders.Any(x => IsHostile(attacker.Owner, x.Owner));
            if (!hostileToDefenders) return;
            if (!battle.Attackers.Contains(attacker)) battle.Attackers.Add(attacker);
            attacker.Battle = battle;
            attacker.AttackTarget = target;
            return;
        }
        var defenders = HostileDefendersAt(attacker.Owner, target).ToList();
        if (defenders.Count == 0) return;
        battle = new Battle
        {
            Id = _game.World.NextBattleId++,
            Location = target,
            AttackerCountry = attacker.Owner,
            DefenderCountry = defenders[0].Owner,
            StartDate = _game.World.Date,
        };
        battle.Attackers.Add(attacker);
        attacker.Battle = battle;
        attacker.AttackTarget = target;
        foreach (var def in defenders)
        {
            if (def.Battle != null && def.AttackTarget != null) LeaveBattleAsAttacker(def);
            if (def.Battle != null) continue; // already defending elsewhere (should not happen)
            battle.Defenders.Add(def);
            def.Battle = battle;
        }
        _game.World.Battles.Add(battle);
        _game.Combat.Current(_game).OnBattleStart(battle, _game);
        if (attacker.Owner.IsPlayer || battle.DefenderCountry.IsPlayer)
            _game.Notify($"Battle of {target.DisplayName}: {attacker.Owner.Name} attacks {battle.DefenderCountry.Name}.", NotificationKind.War, target);
    }

    private void LeaveBattleAsAttacker(Division d)
    {
        d.Battle?.Attackers.Remove(d);
        d.Battle = null;
        d.AttackTarget = null;
    }

    private void ProcessBattle(Battle b)
    {
        foreach (var d in b.Attackers.Concat(b.Defenders).Where(x => x.Strength <= 0.01).ToList())
        {
            if (d.Owner.IsPlayer) _game.Notify($"{d.Name} was destroyed at {b.Location.DisplayName}.", NotificationKind.Bad, b.Location);
            DestroyDivision(d);
        }
        double threshold = 0.01;
        foreach (var a in b.Attackers.Where(x => x.Org <= x.MaxOrg * threshold).ToList())
        {
            LeaveBattleAsAttacker(a);
            a.Path.Clear();
            a.MoveProgress = 0;
            a.ManualOrder = false;
            // a failed naval invasion sails back home
            if (a.Location.IsWater && a.Owner.Capital != null) OrderMove(a, a.Owner.Capital, false);
        }
        foreach (var d in b.Defenders.Where(x => x.Org <= x.MaxOrg * threshold).ToList())
        {
            b.Defenders.Remove(d);
            d.Battle = null;
            Retreat(d, b);
        }
        if (b.Defenders.Count == 0) EndBattle(b, true);
        else if (b.Attackers.Count == 0) EndBattle(b, false);
    }

    private void Retreat(Division d, Battle b)
    {
        var attackerLocs = b.Attackers.Select(a => a.Location).ToHashSet();
        Location? best = null;
        double bestScore = double.MinValue;
        foreach (var adj in d.Location.Neighbors)
        {
            var n = adj.To;
            if (adj.Kind == AdjacencyKind.Sea || !n.IsPassable || !CanEnter(d.Owner, n)) continue;
            if (n.Controller != null && IsHostile(d.Owner, n.Controller)) continue;
            if (HostileDefendersAt(d.Owner, n).Any()) continue;
            double score = (n.Controller == d.Owner ? 10 : 0) - (attackerLocs.Contains(n) ? 100 : 0);
            foreach (var al in attackerLocs)
                score += Math.Sqrt((n.CenterX - al.CenterX) * (n.CenterX - al.CenterX) + (n.CenterY - al.CenterY) * (n.CenterY - al.CenterY)) * 0.01;
            if (score > bestScore) { bestScore = score; best = n; }
        }
        if (best == null)
        {
            _game.NotifyPlayer($"{d.Name} was encircled and destroyed at {b.Location.DisplayName}!", NotificationKind.War, d.Owner, b.AttackerCountry);
            DestroyDivision(d);
            return;
        }
        d.Path = new List<Location> { best };
        d.MoveProgress = 0;
        d.Retreating = true;
        d.ManualOrder = true;
    }

    private void EndBattle(Battle b, bool attackerWon)
    {
        b.Finished = true;
        b.AttackerWon = attackerWon;
        _game.World.Battles.Remove(b);
        foreach (var a in b.Attackers) { a.Battle = null; a.AttackTarget = null; }
        foreach (var d in b.Defenders) d.Battle = null;

        var war = _game.World.WarBetween(b.AttackerCountry, b.DefenderCountry);
        if (war != null)
        {
            double winnerLoss = attackerWon ? b.AttackerLosses : b.DefenderLosses;
            double loserLoss = attackerWon ? b.DefenderLosses : b.AttackerLosses;
            double delta = 1 + 4 * loserLoss / (winnerLoss + loserLoss + 1);
            var winner = attackerWon ? b.AttackerCountry : b.DefenderCountry;
            war.BattleScore += war.IsAttacker(winner) ? delta : -delta;
        }
        var winnerC = attackerWon ? b.AttackerCountry : b.DefenderCountry;
        var loserC = attackerWon ? b.DefenderCountry : b.AttackerCountry;
        if (winnerC.IsPlayer || loserC.IsPlayer)
            _game.Notify($"Battle of {b.Location.DisplayName}: {(winnerC.IsPlayer ? "victory" : "defeat")} against {(winnerC.IsPlayer ? loserC : winnerC).Name}.",
                winnerC.IsPlayer ? NotificationKind.Good : NotificationKind.Bad, b.Location);
        _game.Mods.Invoke(m => m.OnBattleEnded(b));
        var scopes = new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["location"] = b.Location, ["loser"] = loserC, ["winner"] = winnerC };
        _game.Events.FireOnAction("on_battle_won", winnerC, scopes);
        _game.Events.FireOnAction("on_battle_lost", loserC, scopes);
    }

    /// <summary>Ends battles whose sides are no longer at war (after peace).</summary>
    public void EndInvalidBattles()
    {
        foreach (var b in _game.World.Battles.ToList())
        {
            foreach (var a in b.Attackers.Where(a => !b.Defenders.Any(d => IsHostile(a.Owner, d.Owner))).ToList())
                LeaveBattleAsAttacker(a);
            if (b.Attackers.Count > 0) continue;
            _game.World.Battles.Remove(b);
            foreach (var d in b.Defenders) d.Battle = null;
        }
        foreach (var d in _game.World.Divisions)
            if (d.Battle != null && !_game.World.Battles.Contains(d.Battle)) { d.Battle = null; d.AttackTarget = null; }
    }

    /// <summary>Sends divisions standing where they are no longer allowed back to their own land.</summary>
    public void EvacuateTrespassers()
    {
        foreach (var d in _game.World.Divisions.ToList())
        {
            if (d.Location.IsWater || CanEnter(d.Owner, d.Location)) continue;
            var home = NearestOwnLocation(d.Owner, d.Location);
            if (home == null) { DestroyDivision(d); continue; }
            if (d.Battle != null) LeaveBattleAsAttacker(d);
            d.Battle = null;
            d.Path.Clear();
            d.Retreating = false;
            d.ManualOrder = false;
            Teleport(d, home);
        }
    }

    public Location? NearestOwnLocation(Country c, Location from)
    {
        var seen = new HashSet<Location> { from };
        var q = new Queue<Location>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var l = q.Dequeue();
            if (l.Controller == c && l.IsPassable) return l;
            foreach (var a in l.Neighbors)
                if (seen.Add(a.To)) q.Enqueue(a.To);
        }
        return c.Capital != null && c.Capital.Controller == c ? c.Capital : null;
    }

    // =============================================================================================
    // Fronts (HOI4 battle plans)
    // =============================================================================================

    /// <summary>Recomputes the front of an army and sends its divisions to their posts.</summary>
    public void UpdateFront(Army army)
    {
        army.FrontPairs.Clear();
        if (army.Mode == ArmyMode.Fallback && army.Fallback.Count > 0) { HoldFallback(army); return; }
        if (army.FrontEnemy == null) return;
        if (!army.FrontEnemy.Exists || army.FrontEnemy == army.Owner)
        {
            army.FrontEnemy = null;
            foreach (var d in army.Divisions) d.FrontPost = null;
            return;
        }
        var war = _game.World.WarBetween(army.Owner, army.FrontEnemy);
        List<(Location Own, Location Enemy)> pairs = null!;
        // at war: the border with the whole enemy side; at peace: our border with that country (hold the line)
        _game.P("Fronts.Border", () => pairs = war != null ? BorderPairs(war, army.Owner) : PeaceBorderPairs(army.Owner, army.FrontEnemy));
        // focus on the chosen enemy if it shares a border, and on our own land if possible
        var focus = pairs.Where(p => p.Enemy.Controller == army.FrontEnemy).ToList();
        if (focus.Count > 0) pairs = focus;
        var own = pairs.Where(p => p.Own.Controller == army.Owner).ToList();
        if (own.Count > 0) pairs = own;
        // a drawn front covers only part of the border (it follows the border as it moves: neighbours of drawn locations count too)
        if (army.FrontLimit.Count > 0)
        {
            var near = new HashSet<Location>(army.FrontLimit);
            foreach (var l in army.FrontLimit) foreach (var a2 in l.Neighbors) near.Add(a2.To);
            var limited = pairs.Where(p => army.FrontLimit.Contains(p.Own) || army.FrontLimit.Contains(p.Enemy)).ToList();
            if (limited.Count == 0) limited = pairs.Where(p => near.Contains(p.Own) || near.Contains(p.Enemy)).ToList();
            if (limited.Count > 0) pairs = limited;
        }
        army.FrontPairs = new List<(Location Own, Location Enemy)>(pairs); // own copy: the border list is shared between armies

        int hash = pairs.Count;
        foreach (var p in pairs) hash = HashCode.Combine(hash, p.Own.Index, p.Enemy.Index);
        hash = HashCode.Combine(hash, army.Divisions.Count);
        if (hash != army.FrontHash)
        {
            army.FrontHash = hash;
            _game.P("Fronts.Assign", () => AssignPosts(army, pairs));
        }
        _game.P("Fronts.Move", () =>
        {
            foreach (var d in army.Divisions)
            {
                if (d.ManualOrder || d.InBattle || d.Retreating || d.FrontPost == null) continue;
                if (d.Location == d.FrontPost) continue;
                if (d.Path.Count > 0 && d.Path[^1] == d.FrontPost) continue;
                int today = _game.World.Date.TotalDays;
                if (today < d.PathRetryDay) continue; // a blocked division tries again later, not every day
                d.PathRetryDay = today + 3;
                if (!OrderMove(d, d.FrontPost, manual: false)) d.FrontPost = null;
            }
        });
    }

    // all border pairs of one war side, shared by every army of that side (cached for the current hour)
    private readonly Dictionary<(War, Country), (long Stamp, List<(Location Own, Location Enemy)> Pairs)> _borderCache = new();

    private readonly Dictionary<(Country, Country), (long Stamp, List<(Location Own, Location Enemy)> Pairs)> _peaceBorderCache = new();

    /// <summary>Border pairs between a country's land and another country's land (fronts at peace).</summary>
    private List<(Location Own, Location Enemy)> PeaceBorderPairs(Country owner, Country other)
    {
        long stamp = (long)_game.World.Date.TotalDays * 24 + _game.World.Date.Hour;
        if (_peaceBorderCache.TryGetValue((owner, other), out var c) && c.Stamp == stamp) return c.Pairs;
        if (_peaceBorderCache.Count > 256) _peaceBorderCache.Clear();
        var pairs = new List<(Location Own, Location Enemy)>();
        foreach (var st in owner.OwnedStates)
            foreach (var l in st.Locations)
            {
                if (l.Controller != owner || !l.IsPassable) continue;
                foreach (var adj in l.Neighbors)
                    if (adj.Kind != AdjacencyKind.Sea && adj.To.IsPassable && adj.To.Controller == other) pairs.Add((l, adj.To));
            }
        _peaceBorderCache[(owner, other)] = (stamp, pairs);
        return pairs;
    }

    private List<(Location Own, Location Enemy)> BorderPairs(War war, Country owner)
    {
        long stamp = (long)_game.World.Date.TotalDays * 24 + _game.World.Date.Hour;
        var key = war.SideOf(owner).FirstOrDefault() ?? owner; // one list per war side, shared by all its members
        if (_borderCache.TryGetValue((war, key), out var c) && c.Stamp == stamp) return c.Pairs;
        if (_borderCache.Count > 256) _borderCache.Clear();
        var ownSide = war.SideOf(owner).ToHashSet();
        var enemySide = war.EnemiesOf(owner).ToHashSet();
        var pairs = new List<(Location Own, Location Enemy)>();
        foreach (var l in _game.World.Locations)
        {
            if (l.Controller == null || !ownSide.Contains(l.Controller) || !l.IsPassable) continue;
            foreach (var adj in l.Neighbors)
            {
                if (adj.Kind == AdjacencyKind.Sea || !adj.To.IsPassable) continue;
                var ec = adj.To.Controller;
                if (ec != null && enemySide.Contains(ec)) pairs.Add((l, adj.To));
            }
        }
        _borderCache[(war, key)] = (stamp, pairs);
        return pairs;
    }

    /// <summary>Fallback mode: the divisions spread over the fallback line locations still held by their side.</summary>
    private void HoldFallback(Army army)
    {
        var line = army.Fallback.Where(l => l.Controller != null && (l.Controller == army.Owner || !IsHostile(army.Owner, l.Controller)) && CanEnter(army.Owner, l)).ToList();
        var pairs = line.Select(l => (l, l)).ToList();
        army.FrontPairs.Clear();
        int hash = HashCode.Combine(-1, army.Divisions.Count);
        foreach (var l in line) hash = HashCode.Combine(hash, l.Index);
        if (hash != army.FrontHash)
        {
            army.FrontHash = hash;
            AssignPosts(army, pairs);
        }
        foreach (var d in army.Divisions)
        {
            if (d.ManualOrder || d.InBattle || d.Retreating || d.FrontPost == null) continue;
            if (d.Location == d.FrontPost || d.Path.Count > 0 && d.Path[^1] == d.FrontPost) continue;
            if (!OrderMove(d, d.FrontPost, manual: false)) d.FrontPost = null;
        }
    }

    /// <summary>Sets an army's battle plan mode (keeps its front, objective and fallback line).</summary>
    public void SetMode(Army army, ArmyMode mode)
    {
        army.Mode = mode;
        army.MoveTarget = null;
        army.FrontHash = 0;
        foreach (var d in army.Divisions) { d.ManualOrder = false; d.FrontPost = null; }
        UpdateFront(army);
    }

    /// <summary>Adds or removes a location of the drawn front (battle plan).</summary>
    public void ToggleFrontLocation(Army army, Location l)
    {
        if (!army.FrontLimit.Remove(l)) army.FrontLimit.Add(l);
        army.FrontHash = 0;
        UpdateFront(army);
    }

    /// <summary>Adds or removes a location of the fallback line (battle plan).</summary>
    public void ToggleFallbackLocation(Army army, Location l)
    {
        if (!army.Fallback.Remove(l)) army.Fallback.Add(l);
        army.FrontHash = 0;
        if (army.Mode == ArmyMode.Fallback) UpdateFront(army);
    }

    private static double Dist(Location a, Location b) => Math.Sqrt((a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (a.CenterY - b.CenterY) * (a.CenterY - b.CenterY));

    private static void AssignPosts(Army army, List<(Location Own, Location Enemy)> pairs)
    {
        var posts = pairs.GroupBy(p => p.Own)
            .Select(g => (Loc: g.Key, Weight: g.Select(p => p.Enemy).Distinct().Count()))
            .OrderBy(p => Math.Atan2(p.Loc.CenterY, p.Loc.CenterX)).ThenBy(p => p.Loc.CenterY).ToList();
        var divs = army.Divisions.OrderBy(d => d.Id).ToList();
        var previous = divs.ToDictionary(d => d, d => d.FrontPost);
        foreach (var d in divs) d.FrontPost = null;
        if (posts.Count == 0 || divs.Count == 0) return;
        var counts = new int[posts.Count];
        if (divs.Count >= posts.Count)
        {
            for (int i = 0; i < posts.Count; i++) counts[i] = 1;
            int remaining = divs.Count - posts.Count;
            double totalW = posts.Sum(p => p.Weight);
            var extra = posts.Select((p, i) => (i, exact: remaining * p.Weight / totalW)).ToList();
            foreach (var (i, exact) in extra) counts[i] += (int)Math.Floor(exact);
            int left = divs.Count - counts.Sum();
            foreach (var (i, _) in extra.OrderByDescending(e => e.exact - Math.Floor(e.exact)).Take(left)) counts[i]++;
        }
        else
        {
            // fewer divisions than posts: spread them evenly along the front
            for (int k = 0; k < divs.Count; k++) counts[(int)((k + 0.5) * posts.Count / divs.Count)]++;
        }
        // stable assignment: a division keeps its post (or the post it stands on) while that post still needs it,
        // so a small change of the front does not send the whole army walking (and path finding) again
        var index = new Dictionary<Location, int>(posts.Count);
        for (int i = 0; i < posts.Count; i++) index[posts[i].Loc] = i;
        var free = new List<Division>();
        foreach (var d in divs)
        {
            var keep = previous[d];
            if (keep != null && index.TryGetValue(keep, out int pi) && counts[pi] > 0) { counts[pi]--; d.FrontPost = keep; continue; }
            if (index.TryGetValue(d.Location, out int li) && counts[li] > 0) { counts[li]--; d.FrontPost = d.Location; continue; }
            free.Add(d);
        }
        if (free.Count == 0) return;
        // the rest go to the nearest open post
        for (int i = 0; i < posts.Count && free.Count > 0; i++)
        {
            while (counts[i] > 0 && free.Count > 0)
            {
                int best = 0;
                double bestD = double.MaxValue;
                for (int k = 0; k < free.Count; k++)
                {
                    double dd = Dist(free[k].Location, posts[i].Loc);
                    if (dd < bestD) { bestD = dd; best = k; }
                }
                free[best].FrontPost = posts[i].Loc;
                free.RemoveAt(best);
                counts[i]--;
            }
        }
    }

    private void Offensive(Army army)
    {
        double minOrg = _game.Data.Defines.MinOrgToAttack;
        foreach (var d in army.Divisions)
        {
            if (d.ManualOrder || d.InBattle || d.Retreating || d.IsMoving) continue;
            if (d.FrontPost == null || d.Location != d.FrontPost) continue;
            if (d.Org < d.MaxOrg * minOrg) continue;
            Location? best = null;
            double bestScore = double.MaxValue;
            foreach (var (own, enemy) in army.FrontPairs)
            {
                if (own != d.Location) continue;
                double score = (HostilePowerAt(d.Owner, enemy) + 1) * Math.Max(0.1, enemy.Terrain.MovementCost) * (1 + enemy.Terrain.DefenseModifier);
                if (own.AdjacencyTo(enemy)?.River == true) score *= 1.3;
                // offensive arrow: prefer steps that bring the division closer to the objective
                if (army.Objective != null)
                {
                    double before = Dist(own, army.Objective), after = Dist(enemy, army.Objective);
                    score *= after < before ? 0.4 + 0.6 * after / Math.Max(1, before) : 3;
                }
                if (score < bestScore) { bestScore = score; best = enemy; }
            }
            if (best != null)
            {
                d.Path = new List<Location> { best };
                d.MoveProgress = 0;
            }
        }
    }
}
