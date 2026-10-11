using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Politics;

public sealed partial class PoliticsSystem
{
    // =============================================================================================
    // Movements (Victoria 3 style): supporters of a shut-out ideology build up to a revolt
    // =============================================================================================

    public IEnumerable<Movement> MovementsOf(Country c) => Movements.Where(m => m.Country == c);

    /// <summary>What a shut-out ideology demands, or null if it can get its way legally (it can win elections).</summary>
    private (MovementDemand Demand, string? Law)? DemandOf(Country c, CountryPolitics p, string ideology)
    {
        if (InGovernment(c, ideology)) return null;
        if (IsBanned(c, p, ideology))
        {
            // the law it wants: the first law of the banning group that does not ban it
            foreach (var kv in p.Laws)
            {
                if (!Laws.TryGetValue(kv.Value, out var current) || !(current.Bans.Contains(ideology) || current.SingleParty)) continue;
                var target = current.Group.Laws.FirstOrDefault(l => !l.Bans.Contains(ideology) && !l.SuspendsElections)
                             ?? current.Group.Laws.FirstOrDefault(l => !l.Bans.Contains(ideology) && !l.SingleParty);
                if (target != null) return (MovementDemand.Legalize, target.Key);
            }
            return (MovementDemand.Power, null);
        }
        if (HoldsElections(c, p)) return null; // free elections: it can win seats
        return (MovementDemand.Power, null);
    }

    public string DemandText(Movement m)
    {
        var i = Ideology(m.Ideology);
        return m.Demand == MovementDemand.Legalize && m.TargetLaw != null && Laws.TryGetValue(m.TargetLaw, out var l)
            ? $"Legalise the {PartyName(m.Country, m.Ideology)}: enact {l.Name}"
            : $"Put the {PartyName(m.Country, m.Ideology)} ({i?.Name}) in power";
    }

    private void MonthlyUnrest()
    {
        double threshold = _game.Data.Defines.Get("movement_threshold", 0.25);
        foreach (var c in _game.World.Countries.ToList()) // civil wars add countries
        {
            if (!c.Exists || CivilWars.Any(cw => cw.Origin == c || cw.Rebel == c)) continue;
            var p = Of(c);
            bool calm = _game.World.Date.TotalDays < p.CalmUntilDay;
            foreach (var i in Ideologies)
            {
                double pop = p.Popularity.GetValueOrDefault(i.Key);
                var m = Movements.FirstOrDefault(x => x.Country == c && x.Ideology == i.Key);
                var demand = DemandOf(c, p, i.Key);
                if (m == null)
                {
                    if (demand == null || pop < threshold) continue;
                    m = new Movement { Country = c, Ideology = i.Key, Demand = demand.Value.Demand, TargetLaw = demand.Value.Law, Started = _game.World.Date };
                    Movements.Add(m);
                    if (c.IsPlayer) _game.Notify($"A {i.Adjective.ToLowerInvariant()} movement has formed: {DemandText(m)}.", NotificationKind.Bad);
                    _game.Events.FireOnAction("on_movement_formed", c);
                }
                else if (demand == null) { Movements.Remove(m); continue; } // its demand was met
                else { m.Demand = demand.Value.Demand; m.TargetLaw = demand.Value.Law; }
                m.Progress = Math.Clamp(m.Progress + Rules.MovementProgress(_game, c, m, pop), 0, 100);
                if (m.Progress <= 0 && pop < threshold) { Movements.Remove(m); continue; }
                if (m.Progress >= 100 && !calm)
                {
                    Movements.Remove(m);
                    Revolt(c, i.Key, pop, "revolution");
                    break; // one revolt per country per month
                }
            }
            if (!c.Exists || CivilWars.Any(cw => cw.Origin == c) || calm || _game.World.Date.TotalDays < p.CalmUntilDay) continue;
            // a popular shut-out ideology may seize power in a coup, or (HOI4 style) start a civil war
            foreach (var i in Ideologies.OrderByDescending(i => p.Popularity.GetValueOrDefault(i.Key)))
            {
                if (InGovernment(c, i.Key)) continue;
                double pop = p.Popularity.GetValueOrDefault(i.Key);
                // it can simply win the next election - unless the democracy is falling apart (rule CanSeizePower)
                if (DemandOf(c, p, i.Key) == null && !Rules.CanSeizePower(_game, c, i)) continue;
                // (the civil war first: in a poor, unstable or war-weary country it needs less support than a coup)
                double chance = Rules.CivilWarChance(_game, c, i, pop);
                if (chance > 0 && _game.Rng.NextDouble() < chance) { Revolt(c, i.Key, pop, "civil war"); break; }
                double coup = Rules.CoupChance(_game, c, i, pop);
                if (coup > 0 && _game.Rng.NextDouble() < coup) { Coup(c, i.Key); break; }
            }
        }
    }

    /// <summary>Gives the movement what it wants (law change or new government) and dissolves it.</summary>
    public void GiveIn(Movement m)
    {
        Movements.Remove(m);
        var c = m.Country;
        if (m.Demand == MovementDemand.Legalize && m.TargetLaw != null && Laws.TryGetValue(m.TargetLaw, out var law))
            Enact(c, law, free: true);
        else SetRulingParty(c, m.Ideology, "coup");
        if (c.IsPlayer) _game.Notify($"We gave in to the {Ideology(m.Ideology)?.Adjective.ToLowerInvariant()} movement.", NotificationKind.Info);
    }

    public double SuppressCost => _game.Data.Defines.Get("suppress_movement_cost", 50);

    public string? CannotSuppress(Movement m) => CanAfford(m.Country, SuppressCost) ? null : $"Needs {SuppressCost:0} {PowerName}";

    /// <summary>Sets the movement back (costs political power and stability; martyrs make the ideology a little more popular).</summary>
    public string? Suppress(Movement m)
    {
        var reason = CannotSuppress(m);
        if (reason != null) return reason;
        Pay(m.Country, SuppressCost);
        m.Progress = Math.Max(0, m.Progress - _game.Data.Defines.Get("suppress_movement_progress", 25));
        var stab = _game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
        if (stab != null) _game.Resources.Set(m.Country, stab, _game.Resources.Get(m.Country, stab) - _game.Data.Defines.Get("suppress_movement_stability", 5));
        AddPopularity(m.Country, m.Ideology, 0.01);
        return null;
    }

    private void AiMovements(Country c)
    {
        foreach (var m in MovementsOf(c).ToList())
        {
            if (m.Progress < 60) continue;
            // give in to legalisation, fight attempts to take power
            if (m.Demand == MovementDemand.Legalize && _game.Rng.NextDouble() < 0.5) GiveIn(m);
            else if (CannotSuppress(m) == null) Suppress(m);
            else if (m.Progress > 85 && _game.Rng.NextDouble() < 0.3) GiveIn(m);
        }
    }

    // =============================================================================================
    // Civil wars
    // =============================================================================================

    public bool InCivilWar(Country c) => CivilWars.Any(cw => cw.Origin == c || cw.Rebel == c);

    /// <summary>A revolt: a civil war if the game has armies and wars, otherwise a coup.</summary>
    private void Revolt(Country c, string ideology, double popularity, string kind)
    {
        bool war = _game.Features.Has(FeatureSet.Military) && _game.Features.Has(FeatureSet.Diplomacy) && c.OwnedStates.Count >= 2
                   && Rules.RevoltIsCivilWar(_game, c, ideology, popularity);
        if (war && StartCivilWar(c, ideology, Rules.CivilWarShare(_game, c, ideology, popularity)) != null) return;
        Coup(c, ideology);
    }

    /// <summary>The ideology takes power without a war; its supporters are heartened and things calm down for a while.</summary>
    public void Coup(Country c, string ideology)
    {
        Movements.RemoveAll(m => m.Country == c && m.Ideology == ideology);
        SetRulingParty(c, ideology, "coup");
        AddPopularity(c, ideology, 0.05);
        Calm(c);
        Revolutions++;
        Coups++;
        CountRevolt(c);
    }

    private readonly Dictionary<Country, int> _revolts = new();

    /// <summary>Coups and civil wars a country has had in this game (for the Ledger; saved).</summary>
    public int RevoltsOf(Country c) => _revolts.GetValueOrDefault(c);

    private void CountRevolt(Country c) => _revolts[c] = _revolts.GetValueOrDefault(c) + 1;

    private void Calm(Country c) => Of(c).CalmUntilDay = _game.World.Date.TotalDays + (int)(Rules.CalmYears(_game, c) * 365);

    /// <summary>
    /// Splits <paramref name="c"/>: a share of its home states and divisions forms a new country ruled by
    /// <paramref name="ideology"/>, at war with it. If the rebels win they take over the country (it keeps its tag
    /// and gets their government); if they lose they are annexed. Returns the rebel country or null.
    /// </summary>
    public Country? StartCivilWar(Country c, string ideology, double share)
    {
        var ide = Ideology(ideology);
        if (ide == null || !c.Exists || InCivilWar(c) || c.Capital == null) return null;
        var w = _game.World;
        var p = Of(c);
        // the home block: states connected to the capital by land
        var capitalState = c.Capital.State;
        var home = HomeStates(c);
        if (home.Count < 2 || capitalState == null) return null;
        int count = Math.Clamp((int)Math.Round(home.Count * Math.Clamp(share, 0.05, 0.95)), 1, home.Count - 1);
        // grow the rebel area from the state farthest from the capital
        var (cx, cy) = Center(capitalState);
        var seed = home.Where(s => s != capitalState).MaxBy(s => { var (x, y) = Center(s); return (x - cx) * (x - cx) + (y - cy) * (y - cy); })!;
        var rebelStates = new List<State> { seed };
        var taken = new HashSet<State> { seed };
        var frontier = new Queue<State>();
        frontier.Enqueue(seed);
        while (rebelStates.Count < count && frontier.Count > 0)
        {
            var s = frontier.Dequeue();
            foreach (var n in StateNeighbours(s).OrderBy(x => x.Key))
            {
                if (rebelStates.Count >= count) break;
                if (n == capitalState || n.Owner != c || !home.Contains(n) || !taken.Add(n)) continue;
                rebelStates.Add(n);
                frontier.Enqueue(n);
            }
        }

        // the new country
        string tag = FreeTag();
        var col = Mix(c.Color, ide.Color, 0.45);
        var rebel = w.CreateCountry(tag, $"{ide.Adjective} {c.Name}", col, c.FlagPath, ide.Government ?? c.Government);
        rebel.Origin = c.Tag;
        rebel.Names.Male.AddRange(c.Names.Male);
        rebel.Names.Female.AddRange(c.Names.Female);
        rebel.Names.Family.AddRange(c.Names.Family);
        rebel.Playable = false;
        var rp = new CountryPolitics { TotalSeats = p.TotalSeats };
        foreach (var kv in p.Popularity) rp.Popularity[kv.Key] = kv.Value;
        foreach (var kv in p.PartyNames) rp.PartyNames[kv.Key] = kv.Value;
        foreach (var kv in p.Laws) rp.Laws[kv.Key] = kv.Value;
        // the rebels do not ban themselves
        foreach (var grp in LawGroups)
            if (Laws.TryGetValue(rp.Laws.GetValueOrDefault(grp.Key) ?? "", out var l) && l.Bans.Contains(ideology))
                rp.Laws[grp.Key] = grp.Laws.FirstOrDefault(x => !x.Bans.Contains(ideology))?.Key ?? grp.Default;
        _state[rebel] = rp;
        rp.Ruling = ideology;
        rp.Government = new List<string> { ideology };
        rp.Seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [ideology] = rp.TotalSeats };
        _modsDirty.Add(rebel);
        AddPopularity(rebel, ideology, Math.Max(0, 0.65 - rp.Popularity.GetValueOrDefault(ideology)));
        // the supporters who left take part of the ideology's popularity with them
        AddPopularity(c, ideology, -p.Popularity.GetValueOrDefault(ideology) * 0.5);
        Movements.RemoveAll(m => m.Country == c && m.Ideology == ideology);

        // land
        foreach (var s in rebelStates) _game.Diplomacy.TransferState(s, rebel);
        var capState = rebelStates.MaxBy(s => s.Tax + s.Locations.Count * 0.01)!;
        rebel.Capital = capState.Locations.Where(l => l.IsLand && l.IsPassable).OrderBy(l => l.Key).FirstOrDefault() ?? rebel.Capital;
        // treasury, manpower and equipment are split
        rebel.Gold = c.Gold * share; c.Gold -= rebel.Gold;
        rebel.Manpower = c.Manpower * share; c.Manpower -= rebel.Manpower;
        foreach (var k in c.Stockpile.Keys.ToList())
        {
            double part = Math.Floor(c.Stockpile[k] * share);
            c.Stockpile[k] -= part;
            rebel.Stockpile[k] = part;
        }
        // divisions in rebel land defect; more join until the rebels have their share
        if (_game.Features.Has(FeatureSet.Military))
        {
            var rebelLand = rebelStates.SelectMany(s => s.Locations).ToHashSet();
            var mine = w.Divisions.Where(d => d.Owner == c && d.VolunteerFrom == null).ToList();
            int want = (int)Math.Round(mine.Count * share);
            int defected = 0;
            // divisions in rebel land defect first; loyal ones left there fall back to the capital
            foreach (var d in mine.Where(d => rebelLand.Contains(d.Location)).OrderBy(d => d.Id).ToList())
            {
                if (defected < want) { Defect(d, rebel, false); defected++; }
                else if (d.Battle == null && c.Capital != null) { _game.Warfare.StopDivision(d); _game.Warfare.Teleport(d, c.Capital); }
            }
            // then others, spread over the army, until the rebels have their share
            var rest = mine.Where(d => d.Owner == c && !d.InBattle).OrderBy(d => d.Id).ToList();
            for (int k = 0; defected < want && k < rest.Count; k++)
            {
                if (rest.Count - k > want - defected && _game.Rng.NextDouble() < 0.5) continue;
                Defect(rest[k], rebel, true);
                defected++;
            }
        }

        // war
        var war = _game.Diplomacy.DeclareWar(rebel, c, null, null, force: true);
        if (war == null) { Log.Warn($"Civil war in {c.Tag}: the war could not start"); return rebel; }
        war.Name = $"{c.Name} Civil War";
        war.Vars.SetFlag("civil_war");
        CivilWars.Add(new CivilWar { War = war, Origin = c, Rebel = rebel, Ideology = ideology });
        CivilWarsStarted++;
        CountRevolt(c);
        _game.Notify($"Civil war in {c.Name}! The {PartyName(c, ideology)} rose up and formed {rebel.Name}.", c.IsPlayer ? NotificationKind.War : NotificationKind.Info);
        _game.Events.FireOnAction("on_civil_war_started", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["rebel"] = rebel });
        _game.RaiseMapChanged();
        return rebel;
    }

    private void Defect(Division d, Country to, bool teleport)
    {
        var wf = _game.Warfare;
        wf.StopDivision(d);
        wf.AssignToArmy(d, null);
        d.FrontPost = null;
        d.Owner = to;
        if (teleport && to.Capital != null && d.Battle == null) wf.Teleport(d, to.Capital);
    }

    /// <summary>Ends civil wars when one side is beaten (it lost its capital and most of its land, or all of it).</summary>
    private void CheckCivilWars()
    {
        foreach (var cw in CivilWars.ToList())
        {
            if (!_game.World.Wars.Contains(cw.War)) { CivilWars.Remove(cw); continue; }
            bool originBeaten = Beaten(cw.Origin), rebelBeaten = Beaten(cw.Rebel);
            if (Rules.CivilWarVerdict(_game, cw, originBeaten, rebelBeaten) is { } originWon) EndCivilWar(cw, originWon);
        }
    }

    private bool Beaten(Country c)
    {
        if (!c.Exists) return true;
        var land = c.OwnedStates.SelectMany(s => s.Locations).Where(l => l.IsLand).ToList();
        if (land.Count == 0) return true;
        double held = land.Count(l => l.Controller == c) / (double)land.Count;
        bool capitalLost = c.Capital == null || c.Capital.Controller != c;
        return held <= 0.05 || (capitalLost && held <= _game.Data.Defines.Get("civil_war_defeat_share", 0.3));
    }

    private void EndCivilWar(CivilWar cw, bool originWon)
    {
        CivilWars.Remove(cw);
        var origin = cw.Origin;
        var rebel = cw.Rebel;
        _game.Diplomacy.EndWar(cw.War);
        if (originWon)
        {
            _game.Diplomacy.Annex(origin, rebel);
            // the rebels' cause is discredited for a while
            var op0 = Of(origin);
            AddPopularity(origin, cw.Ideology, -op0.Popularity.GetValueOrDefault(cw.Ideology) * 0.5);
            AddPopularity(origin, op0.Ruling, 0.1);
            _game.Notify($"The {origin.Name} Civil War is over: {origin.Name} crushed the {rebel.Name} rebels.", origin.IsPlayer ? NotificationKind.Good : NotificationKind.Info);
        }
        else
        {
            // the revolution won: the country keeps its name and tag but gets the rebels' government, laws and army
            var w = _game.World;
            foreach (var d in w.Divisions.Where(d => d.Owner == origin && d.VolunteerFrom == null).ToList()) _game.Warfare.DestroyDivision(d);
            foreach (var d in w.Divisions.Where(d => d.Owner == rebel).ToList()) Defect(d, origin, false);
            origin.Gold += rebel.Gold;
            origin.Manpower += rebel.Manpower;
            foreach (var kv in rebel.Stockpile) origin.Stockpile[kv.Key] = origin.Stockpile.GetValueOrDefault(kv.Key) + kv.Value;
            var capital = rebel.Capital;
            _game.Diplomacy.Annex(origin, rebel);
            var op = Of(origin);
            var rp = Of(rebel);
            op.Popularity.Clear();
            foreach (var kv in rp.Popularity) op.Popularity[kv.Key] = kv.Value;
            foreach (var kv in rp.Laws) op.Laws[kv.Key] = kv.Value;
            op.Ministers.Clear();
            SetRulingParty(origin, cw.Ideology, "revolution");
            op.Seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [cw.Ideology] = op.TotalSeats };
            if (capital != null && capital.Controller == origin && (origin.Capital == null || origin.Capital.Controller != origin)) origin.Capital = capital;
            Revolutions++;
            _game.Notify($"The {origin.Name} Civil War is over: the revolution won and the {PartyName(origin, cw.Ideology)} rules {origin.Name}.", origin.IsPlayer ? NotificationKind.War : NotificationKind.Info);
        }
        Movements.RemoveAll(m => m.Country == rebel);
        Calm(origin);
        _game.Events.FireOnAction("on_civil_war_ended", origin);
        _game.RaiseMapChanged();
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>The owner's states connected by land to its capital state.</summary>
    private HashSet<State> HomeStates(Country c)
    {
        var result = new HashSet<State>();
        var start = c.Capital?.State;
        if (start == null || start.Owner != c) return result;
        var q = new Queue<State>();
        result.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
            foreach (var n in StateNeighbours(q.Dequeue()))
                if (n.Owner == c && result.Add(n)) q.Enqueue(n);
        return result;
    }

    private static IEnumerable<State> StateNeighbours(State s)
    {
        var seen = new HashSet<State>();
        foreach (var l in s.Locations)
            foreach (var a in l.Neighbors)
                if (a.Kind != AdjacencyKind.Sea && a.To.State is { } o && o != s && seen.Add(o)) yield return o;
    }

    private static (double X, double Y) Center(State s)
    {
        double x = 0, y = 0;
        int n = 0;
        foreach (var l in s.Locations) { x += l.CenterX; y += l.CenterY; n++; }
        return n == 0 ? (0, 0) : (x / n, y / n);
    }

    private string FreeTag()
    {
        for (char a = 'R'; a <= 'Z'; a++)
            for (int i = 0; i < 100; i++)
            {
                string t = $"{a}{i:00}";
                if (_game.World.GetCountry(t) == null) return t;
            }
        for (int i = 0; i < 10000; i++) { string t = $"X{i % 36:X1}{i / 36 % 36:X1}"; if (_game.World.GetCountry(t) == null) return t; }
        return "XXX";
    }

    private static Rgb Mix(Rgb a, Rgb b, double t) => a.Lerp(b, t);
}
