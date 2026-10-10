using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Population;

public sealed partial class PopulationSystem
{
    // =============================================================================================
    // Cores and claims (cores_claims feature)
    // =============================================================================================

    /// <summary>Monthly: owners gain cores (their culture lives there, or long ownership), former owners lose them,
    /// and once a year countries claim neighbouring land where their own culture is the majority.</summary>
    private void MonthlyCores()
    {
        var w = _game.World;
        int today = w.Date.TotalDays;
        foreach (var s in w.States)
        {
            var owner = s.Owner;
            if (owner == null || !_states.TryGetValue(s, out var sp)) continue;
            string? major = MajorityCulture(s);
            double owned = (today - sp.OwnedSinceDay) / 365.0;
            if (!s.Cores.Contains(owner) && Rules.CanCore(_game, s, owner, AcceptedShare(s, owner), owned, major != null && major == Of(owner).Culture))
            {
                s.Cores.Add(owner);
                s.Claims.Remove(owner);
                CoresGained++;
                if (owner.IsPlayer) _game.Notify($"{s.DisplayName} is now a core state of ours.", NotificationKind.Good);
            }
            foreach (var former in s.Cores.Where(c => c != owner).ToList())
            {
                bool cultureThere = major != null && Of(former).Culture != null && IsAccepted(former, major);
                if (!former.Exists && owned >= 1 && !cultureThere) { s.Cores.Remove(former); CoresLost++; continue; }
                if (!cultureThere && owned >= Rules.CoreLossYears(_game, s, former))
                {
                    s.Cores.Remove(former);
                    s.Claims.Add(former); // the memory remains as a claim
                    CoresLost++;
                    if (former.IsPlayer) _game.Notify($"We lost our core on {s.DisplayName}; a claim remains.", NotificationKind.Bad);
                }
            }
        }
        if (w.Date.Month != 1) return;
        // irredentism: claims on neighbouring states where our culture is the majority
        foreach (var c in w.Countries.Where(c => c.Exists))
        {
            string? own = Of(c).Culture;
            if (own == null) continue;
            var seen = new HashSet<State>();
            foreach (var s in c.OwnedStates)
                foreach (var n in NeighbourStates(s))
                {
                    if (n.Owner == null || n.Owner == c || !seen.Add(n) || n.Cores.Contains(c) || n.Claims.Contains(c)) continue;
                    if (MajorityCulture(n) == own) { n.Claims.Add(c); ClaimsMade++; }
                }
        }
    }

    /// <summary>Share of a state's people whose culture the country accepts (1 without pops).</summary>
    public double AcceptedShare(State s, Country c)
    {
        if (!_states.TryGetValue(s, out var sp) || sp.Total <= 0) return 1;
        return sp.Pops.Where(p => IsAccepted(c, p.Culture)).Sum(p => p.Size) / sp.Total;
    }

    /// <summary>
    /// A colony: a state marked <c>category = colony</c>, or (with cores) a state the owner has no core on that is not
    /// connected by land to its capital. Colonies are not assimilated or converted.
    /// </summary>
    public bool IsColony(State s)
    {
        if (s.Owner == null) return false;
        if (string.Equals(s.Category, "colony", StringComparison.OrdinalIgnoreCase)) return true;
        if (!CoresEnabled || s.Cores.Contains(s.Owner)) return false;
        return !HomeOf(s.Owner).Contains(s);
    }

    private readonly Dictionary<Country, (int Day, HashSet<State> States)> _home = new();

    /// <summary>The owner's states connected by land to its capital (cached for a month).</summary>
    public HashSet<State> HomeOf(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_home.TryGetValue(c, out var h) && today - h.Day < 30) return h.States;
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start != null && start.Owner == c)
        {
            var q = new Queue<State>();
            result.Add(start);
            q.Enqueue(start);
            while (q.Count > 0)
                foreach (var n in NeighbourStates(q.Dequeue()))
                    if (n.Owner == c && result.Add(n)) q.Enqueue(n);
        }
        _home[c] = (today, result);
        return result;
    }

    /// <summary>States of <paramref name="target"/> next to <paramref name="c"/>'s land whose majority follows c's religion while target does not.</summary>
    public List<State> FellowBelieverStates(Country c, Country target)
    {
        var result = new List<State>();
        if (!ReligionEnabled) return result;
        string? rel = Of(c).Religion;
        if (rel == null || Of(target).Religion == rel) return result;
        var mine = c.OwnedStates.ToHashSet();
        foreach (var s in target.OwnedStates)
            if (!s.Claims.Contains(c) && !s.Cores.Contains(c) && NeighbourStates(s).Any(mine.Contains) && MajorityReligion(s) == rel) result.Add(s);
        return result;
    }

    // =============================================================================================
    // Accepted cultures
    // =============================================================================================

    /// <summary>Cost of accepting a culture: political power with politics, else gold with the economy, else free.</summary>
    public (double Amount, string Currency) AcceptCost(Country c)
    {
        double cost = _game.Data.Defines.Get("accept_culture_cost", 100);
        if (_game.Politics.PowerResource is { } pr) return (cost, pr.Name);
        if (_game.Features.Has(FeatureSet.Economy)) return (cost, "gold");
        return (0, "");
    }

    public string? CannotAccept(Country c, string culture)
    {
        if (!Enabled || !CultureByKey.ContainsKey(culture)) return "Unknown culture";
        if (IsAccepted(c, culture)) return "Already accepted";
        // only people outside colonies count: a people that lives only in our colonies cannot be accepted
        var home = HomeStates(c).ToList();
        double homeTotal = home.Sum(TotalOf);
        double share = Breakdown(home, p => p.Culture).Where(x => x.Key == culture).Sum(x => x.Size) / Math.Max(1, homeTotal);
        double need = _game.Data.Defines.Get("accept_culture_share", 0.05);
        if (share <= 0 && Breakdown(c.OwnedStates, p => p.Culture).Any(x => x.Key == culture)) return "They live only in our colonies";
        if (share < need) return $"Needs at least {need:P0} of our people outside colonies ({share:P1} now)";
        int max = (int)_game.Data.Defines.Get("max_accepted_cultures", 3);
        if (Of(c).Accepted.Count >= max) return $"At most {max} accepted cultures";
        var (cost, cur) = AcceptCost(c);
        if (cost > 0 && (cur == "gold" ? c.Gold : _game.Politics.PowerOf(c)) < cost) return $"Needs {cost:0} {cur}";
        return null;
    }

    public string? AcceptCulture(Country c, string culture)
    {
        var reason = CannotAccept(c, culture);
        if (reason != null) return reason;
        var (cost, cur) = AcceptCost(c);
        if (cost > 0)
        {
            if (cur == "gold") c.Gold -= cost;
            else if (_game.Politics.PowerResource is { } pr) _game.Resources.Set(c, pr, _game.Resources.Get(c, pr) - cost);
        }
        Of(c).Accepted.Add(culture);
        _countryModsDirty = true;
        foreach (var s in c.OwnedStates) if (_states.TryGetValue(s, out var sp)) ApplyOutput(s, sp);
        return null;
    }

    // =============================================================================================
    // Colonization (colonization feature)
    // =============================================================================================

    public int MaxColonies(Country c) => Math.Max(0, (int)Math.Round(_game.Data.Defines.Get("colonists", 1) + _game.Modifiers.Sum(c, "colonists")));
    public IEnumerable<State> ColoniesOf(Country c) => _states.Where(kv => kv.Value.Colonizer == c && kv.Key.Owner == null).Select(kv => kv.Key);
    public double ColonizeCost => _game.Features.Has(FeatureSet.Economy) ? _game.Data.Defines.Get("colonize_cost", 50) : 0;

    /// <summary>Why <paramref name="c"/> cannot send a colonist to <paramref name="s"/> (null = it can).</summary>
    public string? CannotColonize(Country c, State s)
    {
        if (!ColonizationEnabled) return "No colonization in this game";
        if (s.Owner != null) return "Already owned";
        if (!s.Locations.Any(l => l.IsLand && l.IsPassable)) return "No land to settle";
        var sp = Of(s);
        if (sp.Colonizer != null && sp.Colonizer.Exists) return sp.Colonizer == c ? "Our colonists are already there" : $"{sp.Colonizer.Name} is settling it";
        if (ColoniesOf(c).Count() >= MaxColonies(c)) return $"All our colonists are busy ({MaxColonies(c)})";
        if (ColonizeCost > 0 && c.Gold < ColonizeCost) return $"Needs {ColonizeCost:0} gold";
        double range = _game.Data.Defines.Get("colony_range_km", 1200) * Math.Max(0.1, 1 + _game.Modifiers.Sum(c, "colony_range"));
        if (DistanceKm(c, s) > range) return $"Too far from our land (more than {range:0} km)";
        return null;
    }

    private double DistanceKm(Country c, State s)
    {
        double kmPerPx = _game.Data.Defines.KmPerPixel;
        var (sx, sy) = Center(s);
        double best = double.MaxValue;
        foreach (var o in c.OwnedStates)
            foreach (var l in o.Locations)
            {
                double dx = l.CenterX - sx, dy = l.CenterY - sy;
                double d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
        return best == double.MaxValue ? double.MaxValue : Math.Sqrt(best) * kmPerPx;
    }

    private static (double X, double Y) Center(State s)
    {
        double x = 0, y = 0; int n = 0;
        foreach (var l in s.Locations) { x += l.CenterX; y += l.CenterY; n++; }
        return n == 0 ? (0, 0) : (x / n, y / n);
    }

    public string? Colonize(Country c, State s)
    {
        var reason = CannotColonize(c, s);
        if (reason != null) return reason;
        c.Gold -= ColonizeCost;
        var sp = Of(s);
        sp.Colonizer = c;
        sp.ColonyProgress = 0;
        _game.NotifyPlayer($"{c.Name} sent colonists to {s.DisplayName}.", NotificationKind.Info, c);
        return null;
    }

    public void AbandonColony(State s)
    {
        var sp = Of(s);
        sp.Colonizer = null;
        sp.ColonyProgress = 0;
    }

    private void MonthlyColonies()
    {
        var d = _game.Data.Defines;
        foreach (var (s, sp) in _states.ToList())
        {
            var c = sp.Colonizer;
            if (c == null) continue;
            if (!c.Exists || s.Owner != null) { sp.Colonizer = null; sp.ColonyProgress = 0; continue; }
            if (_game.Features.Has(FeatureSet.Economy)) c.Gold -= d.Get("colony_upkeep", 1);
            double total = sp.Total;
            double natives = total > 0 ? sp.Pops.Where(p => !IsAccepted(c, p.Culture)).Sum(p => p.Size) / total : 0;
            sp.ColonyProgress += Rules.ColonyProgress(_game, s, c, natives);
            // natives fight back now and then
            if (natives > 0.05 && _game.Rng.NextDouble() < d.Get("native_resistance", 0.05) * natives)
            {
                sp.ColonyProgress = Math.Max(0, sp.ColonyProgress - 15);
                _game.NotifyPlayer($"Natives attacked our colonists in {s.DisplayName}.", NotificationKind.Bad, c);
            }
            if (sp.ColonyProgress < 100) continue;
            // the colony becomes our state
            sp.Colonizer = null;
            sp.ColonyProgress = 0;
            _game.Diplomacy.TransferState(s, c);
            var cp = Of(c);
            double settlers = d.Get("colony_settlers", 1000);
            if (cp.Culture != null && settlers > 0)
                sp.Pops.Add(new Pop { Culture = cp.Culture, Religion = ReligionEnabled ? cp.Religion : null, Type = ClassesEnabled ? PopTypes.OrderByDescending(t => t.Share).First().Key : null, Size = settlers });
            if (sp.InitialTotal <= 0) sp.InitialTotal = sp.Total;
            if (sp.BaseTax <= 0) sp.BaseTax = d.Get("colony_tax", 1);
            if (sp.BaseManpower <= 0) sp.BaseManpower = d.Get("colony_manpower", 20);
            sp.Owner = c;
            sp.OwnedSinceDay = _game.World.Date.TotalDays;
            ColoniesFounded++;
            ApplyOutput(s, sp);
            _game.NotifyPlayer($"{s.DisplayName} is now a colony of {c.Name}.", NotificationKind.Good, c);
            _game.Events.FireOnAction("on_colony_founded", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["state"] = s });
        }
    }

    private void AiColonize(Country c)
    {
        if (!ColonizationEnabled || ColoniesOf(c).Count() >= MaxColonies(c)) return;
        if (ColonizeCost > 0 && c.Gold < ColonizeCost * 3) return;
        State? best = null;
        double bestScore = double.MaxValue;
        foreach (var s in _game.World.States)
        {
            if (s.Owner != null || (_states.TryGetValue(s, out var sp) && sp.Colonizer != null)) continue;
            if (CannotColonize(c, s) != null) continue;
            double score = DistanceKm(c, s);
            if (score < bestScore) { bestScore = score; best = s; }
        }
        if (best != null) Colonize(c, best);
    }

    // =============================================================================================
    // Buildings bought with gold (games without production)
    // =============================================================================================

    public double GoldCost(BuildingType b) => b.Stats.GetValueOrDefault("gold_cost");
    public int BuildMonths(BuildingType b) => Math.Max(1, (int)Math.Round(b.Stats.GetValueOrDefault("build_months", 12)));

    /// <summary>Buildings that can be bought with gold (they have gold_cost) - only without the production feature.</summary>
    public IEnumerable<BuildingType> GoldBuildings()
        => _game.Features.Has(FeatureSet.Production) || !_game.Features.Has(FeatureSet.Economy)
            ? Enumerable.Empty<BuildingType>()
            : _game.Data.BuildingTypes.Values.Where(b => GoldCost(b) > 0);

    public string? CannotGoldBuild(Country c, BuildingType b, State s)
    {
        if (!Enabled) return "Needs the population feature";
        if (_game.Features.Has(FeatureSet.Production)) return "Built with factories (Production menu)";
        if (!_game.Features.Has(FeatureSet.Economy)) return "Needs the economy feature";
        if (GoldCost(b) <= 0) return "Cannot be bought with gold";
        if (s.Owner != c) return "You must own the state";
        if (s.ControlledShare(c) < 0.5) return "Most of the state is occupied";
        int level = s.Buildings.GetValueOrDefault(b.Key) + GoldProjects.Count(p => p.State == s && p.Building == b);
        if (level >= b.MaxLevel) return $"Maximum level ({b.MaxLevel})";
        if (b.Potential != null && !_game.Script.EvalTrigger(b.Potential, _game.Context(s))) return "Not possible in this state";
        if (c.Gold < GoldCost(b)) return $"Needs {GoldCost(b):0} gold";
        return null;
    }

    public string? GoldBuild(Country c, BuildingType b, State s)
    {
        var reason = CannotGoldBuild(c, b, s);
        if (reason != null) return reason;
        c.Gold -= GoldCost(b);
        int months = BuildMonths(b);
        GoldProjects.Add(new GoldProject { Country = c, State = s, Building = b, MonthsLeft = months, TotalMonths = months });
        return null;
    }

    private void MonthlyGoldProjects()
    {
        foreach (var p in GoldProjects.ToList())
        {
            if (p.State.Owner != p.Country) { GoldProjects.Remove(p); continue; }
            if (--p.MonthsLeft > 0) continue;
            GoldProjects.Remove(p);
            p.State.Buildings[p.Building.Key] = Math.Min(p.Building.MaxLevel, p.State.Buildings.GetValueOrDefault(p.Building.Key) + 1);
            BuildingsBuilt++;
            _countryModsDirty = true;
            if (p.Country.IsPlayer) _game.Notify($"{p.Building.Name} finished in {p.State.DisplayName}.", NotificationKind.Good);
        }
    }

    private void AiBuild(Country c)
    {
        var options = GoldBuildings().ToList();
        if (options.Count == 0 || GoldProjects.Count(p => p.Country == c) >= 2) return;
        double reserve = 100;
        // the most populous states get buildings first
        foreach (var s in c.OwnedStates.OrderByDescending(TotalOf).Take(5))
            foreach (var b in options.OrderBy(GoldCost))
            {
                if (c.Gold < GoldCost(b) + reserve || CannotGoldBuild(c, b, s) != null) continue;
                if (_game.Rng.NextDouble() < 0.5) continue;
                GoldBuild(c, b, s);
                return;
            }
    }

    /// <summary>Production games: now and then the AI queues a building that helps its people (modern buildings).</summary>
    private void AiProductionBuild(Country c)
    {
        if (!_game.Features.Has(FeatureSet.Production) || c.Construction.Count >= 4 || _game.Rng.NextDouble() > 0.25) return;
        var options = _game.Data.BuildingTypes.Values.Where(b => _buildingMods.ContainsKey(b.Key) || _buildingCountryMods.ContainsKey(b.Key)).ToList();
        if (options.Count == 0) return;
        foreach (var s in c.OwnedStates.OrderByDescending(TotalOf).Take(3))
        {
            var b = options[_game.Rng.Next(options.Count)];
            if (_game.Production.QueueBuilding(c, b, s) == null) return;
        }
    }

    /// <summary>AI: colonies and buildings (monthly).</summary>
    public void AiMonthly(Country c)
    {
        _aiCalled = true;
        if (!Enabled || c.IsPlayer || !c.Exists) return;
        AiColonize(c);
        AiIndependence(c);
        AiBuild(c);
        AiProductionBuild(c);
    }

    private bool _aiCalled;
    private int _monthsTicked;

    /// <summary>Runs the AI part itself when a project's backend AI.cs predates this feature (never calls AiMonthly).</summary>
    public void AiFallback()
    {
        if (!Enabled) return;
        if (!_aiCalled && _monthsTicked > 0 && _game.Features.Has(FeatureSet.Ai) && _game.Ai.Enabled)
            foreach (var c in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer).ToList()) AiMonthly(c);
        _aiCalled = false;
        _monthsTicked++;
    }
}
