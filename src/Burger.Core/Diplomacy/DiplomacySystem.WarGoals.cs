using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Diplomacy;

/// <summary>
/// War goals (EU-style casus belli, also allies' own goals), ticking war score, war exhaustion, liberty desire of subjects,
/// non-aggression pacts and the new peace terms (independence, release a people, regime change, humiliation).
/// </summary>
public sealed partial class DiplomacySystem
{
    // =============================================================================================
    // War goals
    // =============================================================================================

    private readonly Dictionary<WarGoal, (int Day, HashSet<State> States)> _goalStates = new();

    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public readonly Dictionary<string, int> CasusBelliUsed = new();
    public int PeaceDeals, WhitePeaces;

    /// <summary>States a war goal is about (cached for a day).</summary>
    public HashSet<State> GoalStates(WarGoal g)
    {
        int today = _game.World.Date.TotalDays;
        if (_goalStates.TryGetValue(g, out var c) && c.Day == today) return c.States;
        var set = Rules.WarGoalStates(_game, g.Holder, g.Target, g.CasusBelli).ToHashSet();
        _goalStates[g] = (today, set);
        return set;
    }

    /// <summary>Casus belli <paramref name="attacker"/> could use against <paramref name="defender"/> now, best (highest ai_weight) first.</summary>
    public List<CasusBelli> ValidCasusBelli(Country attacker, Country defender)
        => _game.Data.CasusBelli.Values.OrderByDescending(c => c.AiWeight).Where(cb => CanUse(attacker, defender, cb)).ToList();

    // the AI asks whether casus belli are valid very often: answers are kept for the day (wars, peace and pacts clear them)
    private readonly Dictionary<(Country, Country, CasusBelli), bool> _cbCache = new();
    private int _cbCacheDay = int.MinValue;

    /// <summary>True if <paramref name="attacker"/> may declare war on <paramref name="defender"/> with <paramref name="cb"/> (cached for the day).</summary>
    public bool CanUse(Country attacker, Country defender, CasusBelli cb)
    {
        int today = _game.World.Date.TotalDays;
        if (today != _cbCacheDay) { _cbCache.Clear(); _cbCacheDay = today; }
        if (_cbCache.TryGetValue((attacker, defender, cb), out var ok)) return ok;
        ok = Rules.CannotDeclareWarReason(_game, attacker, defender, cb) == null;
        _cbCache[(attacker, defender, cb)] = ok;
        return ok;
    }

    /// <summary>Forget cached casus belli answers (after wars, peace, pacts or truces change).</summary>
    public void InvalidateCasusBelli() => _cbCacheDay = int.MinValue;

    /// <summary>True if <paramref name="a"/> has a casus belli (other than plain conquest) against <paramref name="b"/>.</summary>
    public bool HasCasusBelli(Country a, Country b, string? type = null)
    {
        foreach (var cb in _game.Data.CasusBelli.Values)
        {
            if (type != null ? !cb.Key.Equals(type, StringComparison.OrdinalIgnoreCase) : cb.WarGoal == WarGoalType.Conquest) continue;
            if (CanUse(a, b, cb)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------------------------- who can take what

    /// <summary>The state borders land of <paramref name="c"/> (or land it gets in the same deal).</summary>
    public bool Touches(Country c, State s, HashSet<State>? gained = null)
    {
        foreach (var l in s.Locations)
            foreach (var a in l.Neighbors)
                if (a.Kind != AdjacencyKind.Sea && a.To.IsLand && a.To.State is { } ns && ns != s && (ns.Owner == c || gained?.Contains(ns) == true))
                    return true;
        return false;
    }

    /// <summary>
    /// Can <paramref name="c"/> get to the state from its own land: it borders its land (or land it gets in the same
    /// deal), or the state has a coast within the country's reach over sea (rule SeaReachKm: a short hop for anyone,
    /// far away only with a navy). Landlocked land far from home cannot be reached.
    /// </summary>
    public bool CanReach(Country c, State s, HashSet<State>? gained = null)
    {
        if (s.Owner == c || Touches(c, s, gained)) return true;
        if (!s.Locations.Any(l => l.IsCoastal)) return false;
        double range = Rules.SeaReachKm(_game, c);
        return SeaDistanceKm(c, s, gained) <= range;
    }

    private readonly Dictionary<Country, (int Day, List<Location> Coast)> _coasts = new();

    private List<Location> CoastOf(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_coasts.TryGetValue(c, out var e) && e.Day == today) return e.Coast;
        var coast = c.OwnedStates.SelectMany(o => o.Locations).Where(l => l.IsCoastal).ToList();
        _coasts[c] = (today, coast);
        return coast;
    }

    /// <summary>Distance in km from the nearest coast of <paramref name="c"/> (or of land it gets in the same deal) to the state's coast; endless without a coast.</summary>
    public double SeaDistanceKm(Country c, State s, HashSet<State>? gained = null)
    {
        double best = double.MaxValue;
        var from = CoastOf(c);
        var extra = gained?.SelectMany(o => o.Locations).Where(l => l.IsCoastal);
        foreach (var to in s.Locations)
        {
            if (!to.IsCoastal) continue;
            foreach (var l in extra == null ? from : from.Concat(extra))
            {
                double dx = l.CenterX - to.CenterX, dy = l.CenterY - to.CenterY, d2 = dx * dx + dy * dy;
                if (d2 < best) best = d2;
            }
        }
        return best == double.MaxValue ? double.MaxValue : Math.Sqrt(best) * _game.Data.Defines.KmPerPixel;
    }

    /// <summary>The country at the top of the chain of overlords above <paramref name="c"/> (itself if it is nobody's subject).</summary>
    public Country TopOverlord(Country c)
    {
        for (int i = 0; i < 8 && R.OverlordOf(c) is { } o && o != c; i++) c = o;
        return c;
    }

    /// <summary>A subject of <paramref name="overlord"/> (or of its own overlord: subjects have no subjects) whose people are of this culture (null = none).</summary>
    public Country? SubjectOfCulture(Country overlord, string culture)
    {
        var pop = _game.Population;
        if (!pop.Enabled || culture.Length == 0) return null;
        overlord = TopOverlord(overlord);
        return R.SubjectsOf(overlord).FirstOrDefault(x => x.Exists && pop.Of(x).Culture == culture);
    }

    /// <summary>True if <paramref name="c"/> stayed out of the war and the state's biggest culture is its own people.</summary>
    public bool IsKinOutsideWar(War war, Country c, State s)
    {
        var pop = _game.Population;
        return pop.Enabled && c.Exists && !war.Involves(c) && pop.MajorityCulture(s) is { } mc && pop.Of(c).Culture == mc;
    }

    /// <summary>
    /// A country of the state's own people that <paramref name="winner"/> would hand the state to in a peace deal (rule
    /// GivesLandToKin: its subjects first, then countries it likes most). Never the owner's side, never an enemy of the winner.
    /// </summary>
    public Country? KinFor(War war, Country winner, State s)
    {
        var pop = _game.Population;
        if (!pop.Enabled || s.Owner == null || pop.MajorityCulture(s) is not { } mc) return null;
        if (pop.Of(winner).Culture == mc) return null; // the winner's own people: it takes the land itself
        // core land is conquered or kept, never handed on: not the winner's own, and not the loser's heartland to a third country
        if (s.Cores.Contains(winner) || s.Cores.Contains(s.Owner)) return null;
        var ownerSide = war.Involves(s.Owner) ? war.SideOf(s.Owner) : new List<Country> { s.Owner };
        Country? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists || c == winner || c == s.Owner || ownerSide.Contains(c) || pop.Of(c).Culture != mc) continue;
            if (_game.World.AtWar(winner, c) || !Rules.GivesLandToKin(_game, winner, c)) continue;
            double score = Opinion(winner, c) + (R.OverlordOf(c) == winner ? 1000 : 0);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    /// <summary>Peace cost factor for taking <paramref name="s"/>: the casus belli's goal_cost_multiplier if someone on the beneficiary's side fights for it.</summary>
    public double GoalMultiplier(War war, State s, Country beneficiary)
    {
        double m = 1;
        var side = war.SideOf(beneficiary);
        foreach (var g in war.Goals)
            if (side.Contains(g.Holder) && GoalStates(g).Contains(s)) m = Math.Min(m, g.CasusBelli.GoalCostMultiplier);
        return m;
    }

    /// <summary>Peace cost factor for a demand of this kind: cheaper if a war goal of <paramref name="holder"/>'s side is of that type.</summary>
    public double GoalMultiplierFor(War war, Country holder, WarGoalType type)
    {
        if (!war.Involves(holder)) return 1;
        var side = war.SideOf(holder);
        return war.Goals.Where(g => side.Contains(g.Holder) && g.CasusBelli.WarGoal == type).Select(g => g.CasusBelli.GoalCostMultiplier).DefaultIfEmpty(1).Min();
    }

    /// <summary>Highest total cost of demands in this war: the largest max_demands of the winning side's goals (100 without goals).</summary>
    public double MaxDemands(War war, Country winner)
    {
        var side = war.SideOf(winner);
        var goals = war.Goals.Where(g => side.Contains(g.Holder)).ToList();
        return goals.Count == 0 ? 100 : goals.Max(g => g.CasusBelli.MaxDemands);
    }

    // ------------------------------------------------------------------------------------- releasing a people

    public enum ReleaseOutcome { NewCountry, ToCountry, ToConqueror }

    /// <summary>One nation of a release: what happens to its land.</summary>
    public sealed class ReleasedPart
    {
        public string Culture = "";
        public List<State> States = new();
        public ReleaseOutcome Outcome;
        public Country? Receiver;
        /// <summary>The receiver is a subject of the losing side and is set free with the land.</summary>
        public bool FreeReceiver;
    }

    public sealed class ReleasePlan
    {
        public List<ReleasedPart> Parts = new();
        public string? Problem;
        public List<State> AllStates => Parts.SelectMany(p => p.States).ToList();
    }

    /// <summary>
    /// What releasing a people of <paramref name="owner"/> would do (see rule SeparatistRegion for the land): the land goes to a
    /// country of that people that exists (the winner's side, or a puppet of the losing side that is set free with it, or a
    /// neutral friend), else to the winner when it accepts that people, else a new country is made - but never a second country
    /// of a people that has one, and never land the receiver cannot reach. Without a sensible outcome there is a <see cref="ReleasePlan.Problem"/>.
    /// </summary>
    public ReleasePlan PlanRelease(War war, Country owner, string culture, Country winner, Country beneficiary, bool asSubject)
    {
        var plan = new ReleasePlan();
        var pop = _game.Population;
        if (!pop.Enabled) { plan.Problem = "No peoples in this game"; return plan; }
        if (!owner.OwnedStates.Any(s => s != owner.Capital?.State && pop.MajorityCulture(s) == culture)) { plan.Problem = $"No {culture} states to release"; return plan; }
        var parts = _game.Rules.Population.SeparatistRegion(_game, owner, culture, true);
        if (parts.Count == 0) { plan.Problem = $"The {culture} land cannot be released without cutting up {owner.Name}"; return plan; }
        var lord = TopOverlord(beneficiary);
        var winSide = war.SideOf(winner);
        var ownerSide = war.Involves(owner) ? war.SideOf(owner) : new List<Country> { owner };
        foreach (var part in parts)
        {
            var rp = new ReleasedPart { Culture = part.Culture, States = part.States };
            bool Reaches(Country c) => part.States.Any(s => CanReach(c, s));
            Country? best = null;
            double bestScore = double.NegativeInfinity;
            bool free = false;
            var existing = _game.World.Countries.Where(c => c.Exists && c != owner && pop.Of(c).Culture == part.Culture).ToList();
            foreach (var c in existing)
            {
                var top = TopOverlord(c);
                int rank;
                if (winSide.Contains(c) || winSide.Contains(top)) rank = 0;
                else if (ownerSide.Contains(top) && top != c) rank = 1; // a puppet of the losing side: set free with the land
                else if (!war.Involves(c) && !ownerSide.Contains(top) && !_game.World.AtWar(winner, c) && Rules.GivesLandToKin(_game, winner, c)) rank = 2;
                else continue;
                if (!Reaches(c)) continue;
                double score = -rank * 1000 + Opinion(winner, c);
                if (score > bestScore) { bestScore = score; best = c; free = rank == 1; }
            }
            if (best != null) { rp.Outcome = ReleaseOutcome.ToCountry; rp.Receiver = best; rp.FreeReceiver = free; }
            else if (pop.IsAccepted(lord, part.Culture) || pop.IsAccepted(beneficiary, part.Culture))
            {
                // the conqueror's own people: it takes their land (no 'German Republic' out of German land)
                if (!part.States.Any(s => CanReach(beneficiary, s))) { plan.Problem = $"{beneficiary.Name} cannot reach the {part.Culture} land"; return plan; }
                rp.Outcome = ReleaseOutcome.ToConqueror;
                rp.Receiver = beneficiary;
            }
            else if (existing.Count > 0)
            {
                plan.Problem = $"The {part.Culture} people already have a country ({existing[0].Name}) that cannot take their land";
                return plan;
            }
            else rp.Outcome = ReleaseOutcome.NewCountry;
            plan.Parts.Add(rp);
        }
        return plan;
    }

    /// <summary>True if releasing the people would make nations of their own and nothing else (no land for the winner or for an existing country of that people).</summary>
    public bool ReleaseMakesNewNation(War war, Country owner, string culture, Country winner, bool asSubject)
        => PlanRelease(war, owner, culture, winner, winner, asSubject) is { Problem: null } plan && plan.Parts.Count > 0
           && plan.Parts.All(p => p.Outcome == ReleaseOutcome.NewCountry);

    // ------------------------------------------------------------------------------------- land without ties

    /// <summary>
    /// Takes <paramref name="states"/> (land <paramref name="c"/> has no cultural ties to) away from it: each block of one
    /// people goes to a country of that people that exists and can reach it, else becomes a country of its own - a subject
    /// of <paramref name="patron"/> if that is a winner that sets up puppets, else free. Returns how many states were let go.
    /// </summary>
    public int DissolveHoldings(Country c, IEnumerable<State> states, Country? patron = null)
    {
        var pop = _game.Population;
        if (!pop.Enabled) return 0;
        int moved = 0;
        foreach (var grp in states.Where(s => s.Owner == c).GroupBy(s => pop.MajorityCulture(s) ?? "").ToList())
        {
            if (grp.Key.Length == 0) continue;
            foreach (var block in pop.Components(grp))
            {
                block.RemoveAll(s => s.Owner != c);
                if (block.Count == 0) continue;
                Country? receiver = null;
                double best = double.NegativeInfinity;
                foreach (var x in _game.World.Countries)
                {
                    if (!x.Exists || x == c || pop.Of(x).Culture != grp.Key || _game.World.AtWar(c, x)) continue;
                    if (!block.Any(s => CanReach(x, s))) continue;
                    double score = Opinion(c, x) + (patron != null && TopOverlord(x) == TopOverlord(patron) ? 100 : 0);
                    if (score > best) { best = score; receiver = x; }
                }
                if (receiver != null)
                {
                    foreach (var s in block) TransferState(s, receiver);
                    Record($"{grp.Key} land of {c.Tag} went to {receiver.Tag} (no ties to {c.Tag})");
                    moved += block.Count;
                    continue;
                }
                var free = pop.ReleaseCulture(c, grp.Key, block);
                if (free == null) continue;
                moved += block.Count;
                if (patron != null && Rules.PrefersPuppets(_game, patron) && R.OverlordOf(patron) == null) MakeSubject(free, patron, _game.Data.SubjectTypes.Values.First());
                Record($"{grp.Key} land of {c.Tag} became {free.Tag} (no ties to {c.Tag})");
            }
        }
        return moved;
    }

    private readonly HashSet<Country> _hadTies = new();

    /// <summary>
    /// Monthly: a country that had a homeland and has lost every state it has ties to (the mainland was taken) cannot go on
    /// holding the rest from afar: its colonies and conquests become countries of their own peoples (define holdings_collapse).
    /// </summary>
    private void CheckHoldingsCollapse()
    {
        var pop = _game.Population;
        if (!pop.Enabled || _game.Data.Defines.Get("holdings_collapse", 1) <= 0) return;
        foreach (var c in _game.World.Countries.Where(x => x.Exists).ToList())
        {
            if (c.OwnedStates.Any(s => _game.Rules.Population.HasCulturalTies(_game, c, s))) { _hadTies.Add(c); continue; }
            if (!_hadTies.Remove(c)) continue; // never had a homeland: the data says so, leave it
            int n = DissolveHoldings(c, c.OwnedStates.ToList());
            if (n > 0) _game.Notify($"{c.Name} lost its homeland and cannot hold its distant lands: {n} states went their own way.", c.IsPlayer ? NotificationKind.Bad : NotificationKind.Info);
        }
    }

    // ------------------------------------------------------------------------------------- stranded land

    /// <summary>The home of a country: the block of its states around the capital (its biggest block if the capital is lost).</summary>
    public HashSet<State> LinkedHome(Country c)
    {
        var comps = _game.Population.Components(c.OwnedStates);
        if (comps.Count == 0) return new HashSet<State>();
        var cap = c.Capital?.State;
        var capComp = cap != null && cap.Owner == c ? comps.First(x => x.Contains(cap)) : null;
        return (capComp ?? comps.OrderByDescending(x => x.Sum(s => s.Locations.Count)).First()).ToHashSet();
    }

    /// <summary>Which states are linked to the capital now, for the countries of a war (before a peace deal).</summary>
    private Dictionary<Country, HashSet<State>> SnapshotHomes(War war)
        => war.Attackers.Concat(war.Defenders).Distinct().Where(c => c.Exists).ToDictionary(c => c, LinkedHome);

    /// <summary>
    /// After a peace deal: land that was joined to a country's capital and is not any more (the land between was given away)
    /// goes to the neighbour that surrounds it best - one of its own people, one the old owner likes - never an enemy.
    /// </summary>
    private void SettleStranded(Dictionary<Country, HashSet<State>> before, ICollection<Country>? winners = null)
    {
        var pop = _game.Population;
        foreach (var (c, set) in before)
        {
            if (!c.Exists) continue;
            // what was the home before, in blocks now: the capital's block stays home unless another block is bigger - then
            // the government moves there and the smaller block of the old capital is what is cut off
            var blocks = pop.Components(set.Where(s => s.Owner == c));
            if (blocks.Count == 0) continue;
            int Size(List<State> x) => x.Sum(s => s.Locations.Count);
            var capBlock = c.Capital?.State is { } cap0 && cap0.Owner == c ? blocks.FirstOrDefault(x => x.Contains(cap0)) : null;
            var bigBlock = blocks.OrderByDescending(Size).First();
            var home = (capBlock != null && Size(capBlock) >= Size(bigBlock) ? capBlock : bigBlock).ToHashSet();
            if (c.Capital?.State is not { } cs || !home.Contains(cs))
            {
                var seat = home.OrderByDescending(s => pop.Of(s).Total).ThenByDescending(s => s.Locations.Count).First();
                var spot = seat.Locations.Where(l => l.IsLand && l.IsPassable).OrderBy(l => l.Key).FirstOrDefault();
                if (spot != null)
                {
                    c.Capital = spot;
                    _game.NotifyPlayer($"The capital of {c.Name} moves to {seat.DisplayName}.", NotificationKind.Diplomacy, c);
                }
            }
            var orphans = set.Where(s => s.Owner == c && !home.Contains(s)).ToList();
            if (orphans.Count == 0) continue;
            foreach (var comp in pop.Components(orphans))
            {
                var inComp = comp.ToHashSet();
                var scores = new Dictionary<Country, double>();
                foreach (var s in comp)
                    foreach (var n in pop.NeighbourStates(s))
                        if (!inComp.Contains(n) && n.Owner is { } o && o != c && o.Exists) scores[o] = scores.GetValueOrDefault(o) + 10;
                string? major = pop.Enabled ? pop.Breakdown(comp, p => p.Culture).FirstOrDefault().Key : null;
                Country? best = null;
                double bestScore = double.NegativeInfinity;
                foreach (var (o, sc) in scores)
                {
                    if (_game.World.AtWar(c, o)) continue;
                    double v = sc + (major != null && pop.Of(o).Culture == major ? 15 : 0) + Opinion(c, o) / 10 + (winners != null && winners.Contains(o) ? 30 : 0);
                    if (v > bestScore) { bestScore = v; best = o; }
                }
                if (best == null) continue;
                foreach (var s in comp) TransferState(s, best);
                Record($"{string.Join(", ", comp.Select(s => s.DisplayName))} of {c.Tag} could no longer be reached: given to {best.Tag}");
                _game.NotifyPlayer($"{comp[0].DisplayName}{(comp.Count > 1 ? $" and {comp.Count - 1} more" : "")} could not be held by {c.Name} any more and went to {best.Name}.", NotificationKind.Diplomacy, c, best);
            }
        }
    }

    // ------------------------------------------------------------------------------------- enclaves

    private Dictionary<Country, HashSet<Country>>? _enclaves;
    private int _enclavesDay = int.MinValue;

    /// <summary>Countries that hold an enclave inside the land of <paramref name="c"/>: a state with neighbours of nobody else (counted once a day).</summary>
    public HashSet<Country> EnclaveHolders(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_enclaves == null || _enclavesDay != today)
        {
            _enclaves = new Dictionary<Country, HashSet<Country>>();
            _enclavesDay = today;
            var pop = _game.Population;
            foreach (var s in _game.World.States)
            {
                if (s.Owner == null) continue;
                Country? around = null;
                bool single = true;
                foreach (var n in pop.NeighbourStates(s))
                {
                    if (n.Owner == null || n.Owner == s.Owner) { single = false; break; }
                    if (around == null) around = n.Owner; else if (around != n.Owner) { single = false; break; }
                }
                if (!single || around == null) continue;
                if (!_enclaves.TryGetValue(around, out var set)) _enclaves[around] = set = new HashSet<Country>();
                set.Add(s.Owner);
            }
        }
        return _enclaves.TryGetValue(c, out var found) ? found : new HashSet<Country>();
    }

    /// <summary>The state is an enclave: every neighbour of it belongs to <paramref name="by"/> or its side.</summary>
    public bool IsEnclaveOf(State s, ICollection<Country> bySide)
    {
        bool any = false;
        foreach (var n in _game.Population.NeighbourStates(s))
        {
            if (n.Owner == null || n.Owner == s.Owner) return false;
            if (!bySide.Contains(n.Owner)) return false;
            any = true;
        }
        return any;
    }

    /// <summary>A participant joining a war adds its own war goal against an enemy if it has a casus belli that is about land (faction war goals).</summary>
    private void AddJoinGoal(War war, Country c)
    {
        if (war.Goals.Any(g => g.Holder == c)) return;
        foreach (var enemy in war.EnemiesOf(c).OrderBy(e => e == war.EnemyLeaderOf(c) ? 0 : 1))
        {
            var cb = _game.Data.CasusBelli.Values.OrderByDescending(x => x.AiWeight)
                .FirstOrDefault(x => Rules.NeedsGoalStates(x.WarGoal) && Rules.WarGoalStates(_game, c, enemy, x).Count > 0 && (x.Allow == null || AllowOk(x, c, enemy)));
            if (cb == null) continue;
            war.Goals.Add(new WarGoal { Holder = c, Target = enemy, CasusBelli = cb });
            return;
        }
    }

    private bool AllowOk(CasusBelli cb, Country attacker, Country defender)
    {
        var ctx = _game.Context(attacker);
        ctx.Scopes["actor"] = attacker;
        ctx.Scopes["recipient"] = defender;
        ctx.Scopes["target"] = defender;
        return _game.Script.EvalTrigger(cb.Allow, ctx);
    }

    /// <summary>Monthly: the front (occupied land) of every war, stalemates and ticking war score.</summary>
    /// <summary>A country that has lost its capital and half its land stops fighting (its divisions are gone) but stays in the war, occupied, until its faction makes peace.</summary>
    private void Capitulate(War war, Country c)
    {
        war.Vars.SetFlag("cap:" + c.Tag);
        foreach (var d in _game.World.Divisions.Where(d => d.Owner == c).ToList()) _game.Warfare.DestroyDivision(d);
        _game.Notify($"{c.Name} has capitulated.", NotificationKind.War);
        // a leader that capitulates hands the lead to an ally still fighting
        var side = war.SideOf(c);
        var next = side.FirstOrDefault(x => x != c && !IsCapitulated(war, x) && _game.World.Relations.OverlordOf(x) == null) ?? side.FirstOrDefault(x => x != c && !IsCapitulated(war, x));
        if (next != null)
        {
            if (war.AttackerLeader == c) war.AttackerLeader = next;
            if (war.DefenderLeader == c) war.DefenderLeader = next;
        }
        _game.Events.FireOnAction("on_capitulation", c);
    }

    private void MonthlyWars()
    {
        int today = _game.World.Date.TotalDays;
        foreach (var war in _game.World.Wars.ToList())
        {
            if (!war.Vars.HasFlag("civil_war"))
            {
                if (!war.Vars.HasFlag("total") && Rules.IsTotalWar(_game, war)) { war.Vars.SetFlag("total"); _game.Notify($"The {war.Name} has become a total war.", NotificationKind.War); }
                foreach (var c in war.Attackers.Concat(war.Defenders).ToList())
                    if (!IsCapitulated(war, c) && Rules.Capitulates(_game, war, c)) Capitulate(war, c);
            }
            var att = war.Attackers.ToHashSet();
            var def = war.Defenders.ToHashSet();
            int occupied = 0;
            foreach (var l in _game.World.Locations)
            {
                if (l.Owner == null || l.IsWater || l.Controller == null || l.Controller == l.Owner) continue;
                if ((att.Contains(l.Owner) && def.Contains(l.Controller)) || (def.Contains(l.Owner) && att.Contains(l.Controller))) occupied++;
            }
            if (occupied != war.LastOccupied) { war.LastOccupied = occupied; war.LastChangeDay = today; }
            // ticking war score: the attacker leader's goal held (or not held after a year)
            var goal = war.Goals.FirstOrDefault(g => att.Contains(g.Holder));
            if (goal == null || !goal.CasusBelli.TickingWarScore) continue;
            var states = GoalStates(goal);
            if (states.Count == 0) continue;
            int total = 0, held = 0;
            foreach (var s in states)
                foreach (var l in s.Locations)
                    if (l.IsLand) { total++; if (l.Controller != null && att.Contains(l.Controller)) held++; }
            double cap = _game.Data.Defines.Get("max_ticking_war_score", 25);
            if (total > 0 && held * 2 >= total) war.TickingScore = Math.Min(cap, war.TickingScore + 1);
            else if (today - war.StartDate.TotalDays > 365 && held == 0) war.TickingScore = Math.Max(-cap, war.TickingScore - 1);
        }
    }

    // =============================================================================================
    // War exhaustion
    // =============================================================================================

    private readonly Dictionary<Country, double> _exhaustion = new();
    private readonly Dictionary<Country, double> _menLostMonth = new();
    /// <summary>Men lost in battle per country since the start (for the Ledger; saved).</summary>
    public readonly Dictionary<Country, double> MenLost = new();

    /// <summary>War exhaustion of a country (0-20).</summary>
    public double WarExhaustion(Country c) => _exhaustion.GetValueOrDefault(c);

    public void SetWarExhaustion(Country c, double v) => _exhaustion[c] = Math.Clamp(v, 0, MaxWarExhaustion);

    public double MaxWarExhaustion => _game.Data.Defines.Get("max_war_exhaustion", 20);

    /// <summary>Records men a country lost in battle (war exhaustion, Ledger).</summary>
    public void RecordLosses(Country c, double men)
    {
        if (men <= 0) return;
        _menLostMonth[c] = _menLostMonth.GetValueOrDefault(c) + men;
        MenLost[c] = MenLost.GetValueOrDefault(c) + men;
    }

    private void MonthlyExhaustion()
    {
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists) { _exhaustion.Remove(c); continue; }
            double v = WarExhaustion(c) + Rules.MonthlyWarExhaustion(_game, c, _menLostMonth.GetValueOrDefault(c));
            if (v <= 0) _exhaustion.Remove(c); else _exhaustion[c] = Math.Min(MaxWarExhaustion, v);
        }
        _menLostMonth.Clear();
    }

    private static readonly HashSet<string> ExhaustionKeys = new(StringComparer.OrdinalIgnoreCase) { "stability_target", "manpower", "political_power_gain" };

    /// <summary>Modifier from war exhaustion (used by the modifier system).</summary>
    public double ExhaustionModifierValue(IScope scope, string key)
    {
        if (scope is not Country c || !ExhaustionKeys.Contains(key)) return 0;
        double e = WarExhaustion(c);
        return e <= 0 ? 0 : Rules.WarExhaustionModifier(_game, c, e, key);
    }

    // =============================================================================================
    // Liberty desire
    // =============================================================================================

    private readonly Dictionary<Country, double> _liberty = new();

    /// <summary>Liberty desire of a subject (0-100; 0 for countries that are not subjects). Recomputed monthly.</summary>
    public double LibertyDesire(Country subject)
    {
        if (R.OverlordOf(subject) == null) return 0;
        if (!_liberty.TryGetValue(subject, out var v)) _liberty[subject] = v = ComputeLiberty(subject);
        return v;
    }

    private double ComputeLiberty(Country s) => R.Subjects.TryGetValue(s, out var x) ? Rules.LibertyDesire(_game, s, x.Overlord, x.Type) : 0;

    private void MonthlyLiberty()
    {
        _liberty.Clear();
        foreach (var s in R.Subjects.Keys.ToList()) if (s.Exists) _liberty[s] = ComputeLiberty(s);
    }

    // =============================================================================================
    // Subjects
    // =============================================================================================

    /// <summary>
    /// The overlord already holds the other country's homeland: they are the same people (same primary culture), or it owns
    /// at least half of the other's core states. Such a country is annexed, never made a subject (no "Russian Republic"
    /// puppet in Siberia while its overlord rules Russia).
    /// </summary>
    public bool HoldsHomelandOf(Country overlord, Country other)
    {
        if (overlord == other) return true;
        var pop = _game.Population;
        if (pop.Enabled)
        {
            string a = pop.Of(overlord).Culture ?? "", b = pop.Of(other).Culture ?? "";
            if (a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        }
        int cores = 0, held = 0;
        foreach (var s in _game.World.States)
        {
            if (!s.Cores.Contains(other)) continue;
            cores++;
            if (s.Owner == overlord) held++;
        }
        return cores > 0 && held * 2 >= cores;
    }

    /// <summary>Makes a country a subject: its government follows the overlord's ideology (no democratic puppets of communists).</summary>
    /// <remarks>
    /// Subjects have no subjects of their own: if <paramref name="overlord"/> is a subject itself the new subject goes
    /// to its overlord, and the subjects the new subject had pass to the overlord with it.
    /// </remarks>
    public void MakeSubject(Country subject, Country overlord, SubjectType type)
    {
        if (TopOverlord(overlord) == subject) R.ReleaseSubject(overlord); // a subject that turned the tables on its overlord
        else overlord = TopOverlord(overlord);
        if (subject == overlord) return;
        foreach (var s in R.SubjectsOf(subject).ToList())
        {
            if (s == overlord) continue;
            R.SetSubject(s, overlord, R.SubjectTypeOf(s) ?? type);
            _liberty.Remove(s);
            FollowOverlord(s, overlord, popularity: 0.1);
            Record($"{s.Tag}, subject of {subject.Tag}, passed to {overlord.Tag}");
        }
        R.SetSubject(subject, overlord, type);
        _liberty.Remove(subject);
        FollowOverlord(subject, overlord, popularity: 0.2);
        _game.RefreshName(subject);
        InvalidateCasusBelli();
        _game.RaiseMapChanged();
    }

    /// <summary>Puts the overlord's ideology in power in a subject (with some extra support for it).</summary>
    private void FollowOverlord(Country subject, Country overlord, double popularity)
    {
        var pol = _game.Politics;
        if (!pol.Enabled) return;
        string want = pol.Of(overlord).Ruling;
        if (string.IsNullOrEmpty(want) || string.Equals(pol.Of(subject).Ruling, want, StringComparison.OrdinalIgnoreCase)) return;
        pol.SetRulingParty(subject, want, "overlord");
        if (popularity > 0) pol.AddPopularity(subject, want, popularity);
    }

    /// <summary>
    /// Monthly: an overlord that holds a subject's homeland annexes it; subjects that changed government (elections,
    /// revolts) are brought back in line with the overlord's ideology.
    /// </summary>
    private void MonthlySubjects()
    {
        foreach (var kv in R.Subjects.ToList())
        {
            var (subject, overlord) = (kv.Key, kv.Value.Overlord);
            if (!subject.Exists || !overlord.Exists) continue;
            // no chains: the subject of a subject answers to the country at the top
            if (TopOverlord(overlord) is var top && top != overlord && top != subject)
            {
                R.SetSubject(subject, top, kv.Value.Type);
                Record($"{subject.Tag}, subject of the subject {overlord.Tag}, now answers to {top.Tag}");
                overlord = top;
            }
            if (_game.World.AtWar(subject, overlord)) continue;
            if (HoldsHomelandOf(overlord, subject))
            {
                R.ReleaseSubject(subject);
                _liberty.Remove(subject);
                _game.Notify($"{overlord.Name} annexed {subject.Name}: it already rules their homeland.",
                    overlord.IsPlayer || subject.IsPlayer ? NotificationKind.Diplomacy : NotificationKind.Info);
                Record($"{overlord.Tag} annexed its subject {subject.Tag} (holds their homeland)");
                Annex(overlord, subject);
                continue;
            }
            // liberal overlords let restless subjects go
            if (Rules.WillGrantIndependence(_game, overlord, subject))
            {
                R.ReleaseSubject(subject);
                _liberty.Remove(subject);
                _game.RefreshName(subject);
                AddOpinion(subject, overlord, "Granted independence", 50, 0.5);
                _game.Notify($"{overlord.Name} granted independence to {subject.Name}.", overlord.IsPlayer || subject.IsPlayer ? NotificationKind.Diplomacy : NotificationKind.Info);
                Record($"{overlord.Tag} granted independence to its subject {subject.Tag}");
                continue;
            }
            FollowOverlord(subject, overlord, popularity: 0.05);
        }
    }

    /// <summary>A subject joins its overlord's wars only while its liberty desire is below 50.</summary>
    private bool JoinsOverlord(Country subject) => R.SubjectTypeOf(subject)?.JoinsOverlordWars == true && LibertyDesire(subject) < _game.Data.Defines.Get("liberty_desire_rebel", 50);

    // =============================================================================================
    // New peace terms
    // =============================================================================================

    private string? InvalidNewTerm(War war, PeaceTerm t, Country winner, Country loser)
    {
        switch (t.Type)
        {
            case PeaceTermType.Independence:
                // any subject of the losing side: one that fights on our side, or any puppet of the enemy
                if (t.Beneficiary == null || !t.Beneficiary.Exists) return "Invalid country to free";
                if (R.OverlordOf(t.Beneficiary) is not { } ov || !war.SideOf(loser).Contains(ov)) return $"{t.Beneficiary.Name} is not a subject of the losing side";
                break;
            case PeaceTermType.ReleaseCountry:
                if (!_game.Population.Enabled) return "No peoples in this game";
                if (t.State?.Owner == null || !war.SideOf(loser).Contains(t.State.Owner)) return "Invalid state";
                // (released as a subject by a subject: it becomes a subject of that country's overlord)
                if (t.AsSubject && t.Beneficiary == null) return "Invalid overlord";
                if (PlanRelease(war, t.State.Owner, t.Culture, winner, t.Beneficiary ?? winner, t.AsSubject).Problem is { } why) return why;
                break;
            case PeaceTermType.RegimeChange:
                if (!_game.Politics.Enabled || _game.Politics.Ideology(t.Ideology) == null) return "No ideologies in this game";
                break;
            case PeaceTermType.Demilitarize:
                if (!DemilEnabled) return "No demilitarized zones in this game";
                if (t.State?.Owner == null || !war.SideOf(loser).Contains(t.State.Owner)) return "Invalid state";
                if (t.State.Demilitarized) return $"{t.State.DisplayName} is demilitarized already";
                break;
        }
        return null;
    }

    private void ApplyNewTerm(War war, PeaceTerm t, Country winner, Country loser)
    {
        switch (t.Type)
        {
            case PeaceTermType.Independence:
                if (t.Beneficiary != null && R.OverlordOf(t.Beneficiary) != null)
                {
                    R.ReleaseSubject(t.Beneficiary);
                    _liberty.Remove(t.Beneficiary);
                    _game.RefreshName(t.Beneficiary);
                    AddOpinion(t.Beneficiary, winner, "Freed us", 50, 0.5);
                    _game.Notify($"{t.Beneficiary.Name} is free of its overlord.", NotificationKind.Diplomacy);
                }
                break;
            case PeaceTermType.ReleaseCountry:
                if (t.State?.Owner is { } owner)
                {
                    var receiver = t.Beneficiary ?? winner;
                    var plan = PlanRelease(war, owner, t.Culture, winner, receiver, t.AsSubject);
                    if (plan.Problem != null) { Record($"release of {t.Culture} from {owner.Tag} dropped: {plan.Problem}"); break; }
                    foreach (var part in plan.Parts)
                    {
                        switch (part.Outcome)
                        {
                            case ReleaseOutcome.ToCountry:
                                {
                                    var c = part.Receiver!;
                                    if (part.FreeReceiver && R.OverlordOf(c) != null)
                                    {
                                        R.ReleaseSubject(c);
                                        _liberty.Remove(c);
                                        _game.RefreshName(c);
                                        _game.Notify($"{c.Name} is free of its overlord.", NotificationKind.Diplomacy);
                                    }
                                    foreach (var st in part.States) TransferState(st, c);
                                    AddOpinion(c, winner, "Gave us our people's land", 50, 0.5);
                                    Record($"{part.Culture} land of {owner.Tag} went to {c.Tag}{(part.FreeReceiver ? " (set free)" : "")}");
                                    break;
                                }
                            case ReleaseOutcome.ToConqueror:
                                foreach (var st in part.States) TransferState(st, part.Receiver!);
                                Record($"{part.Culture} land of {owner.Tag} taken by {part.Receiver!.Tag} (its own people)");
                                break;
                            default:
                                {
                                    var freed = _game.Population.ReleaseCulture(owner, part.Culture, part.States);
                                    if (freed != null && t.AsSubject && part == plan.Parts[0])
                                        MakeSubject(freed, receiver, _game.Data.SubjectTypes.Values.First());
                                    else if (freed != null) AddOpinion(freed, winner, "Liberated us", 75, 0.5);
                                    break;
                                }
                        }
                    }
                }
                break;
            case PeaceTermType.Demilitarize:
                if (t.State != null) Demilitarize(t.State, winner);
                break;
            case PeaceTermType.RegimeChange:
                _game.Politics.SetRulingParty(loser, t.Ideology, "revolution");
                _game.Politics.AddPopularity(loser, t.Ideology, 0.2);
                break;
            case PeaceTermType.Humiliate:
                {
                    var stab = _game.Resources.Defs.FirstOrDefault(r => r.Key.Equals("stability", StringComparison.OrdinalIgnoreCase));
                    if (stab != null) _game.Resources.Set(loser, stab, _game.Resources.Get(loser, stab) - _game.Data.Defines.Get("humiliate_stability", 10));
                    if (_game.Politics.PowerResource is { } pp)
                    {
                        double take = Math.Min(_game.Resources.Get(loser, pp), _game.Data.Defines.Get("humiliate_power", 50));
                        _game.Resources.Set(loser, pp, _game.Resources.Get(loser, pp) - take);
                        _game.Resources.Set(winner, pp, _game.Resources.Get(winner, pp) + take);
                    }
                    AddOpinion(loser, winner, "Humiliated us", -50, 1);
                    break;
                }
        }
    }

    // =============================================================================================
    // Non-aggression pacts
    // =============================================================================================

    public void CreatePact(Country a, Country b, int years) { R.AddPact(a, b, _game.World.Date.AddYears(Math.Max(1, years))); InvalidateCasusBelli(); }

    /// <summary>Breaks a pact; everyone thinks less of the one who broke it.</summary>
    public void BreakPact(Country breaker, Country other)
    {
        if (!R.HasPact(breaker, other, _game.World.Date)) return;
        R.RemovePact(breaker, other);
        InvalidateCasusBelli();
        AddOpinion(other, breaker, "Broke our non-aggression pact", -60, 1);
        foreach (var c in _game.World.Countries.Where(x => x.Exists && x != breaker && x != other))
            AddOpinion(c, breaker, "Broke a non-aggression pact", _game.Data.Defines.Get("break_pact_opinion", -10), 0.5);
    }

    // =============================================================================================
    // Script
    // =============================================================================================

    public void RegisterScript()
    {
        var s = _game.Script;
        Country? Other(Node n, ScriptContext ctx) => s.TryResolveScope(n.Value ?? n.GetString("target") ?? "", ctx, out var o) ? o as Country : null;
        s.RegisterTrigger("has_non_aggression_pact", (ctx, n) => ctx.This is Country a && Other(n, ctx) is { } b && R.HasPact(a, b, _game.World.Date),
            (ctx, n) => $"Has a non-aggression pact with {Other(n, ctx)?.Name ?? n.Value}");
        s.RegisterTrigger("has_casus_belli", (ctx, n) =>
        {
            if (ctx.This is not Country a) return false;
            var b = Other(n, ctx);
            return b != null && HasCasusBelli(a, b, n.GetString("type"));
        }, (ctx, n) => $"Has a {(n.GetString("type") is { } t && _game.Data.CasusBelli.TryGetValue(t, out var cb) ? cb.Name : "")} casus belli against {Other(n, ctx)?.Name ?? n.Value}".Replace("a  casus", "a casus"));
        s.RegisterTrigger("ideological_enemy", (ctx, n) => ctx.This is Country a && Other(n, ctx) is { } b && _game.Politics.Enabled && _game.Politics.IdeologyOpinion(a, b) <= _game.Data.Defines.Get("ideological_enemy_opinion", -20),
            (ctx, n) => $"Our ideology despises the government of {Other(n, ctx)?.Name ?? n.Value}");
        s.RegisterValue("war_exhaustion", (ctx, x) => x is Country c ? WarExhaustion(c) : null);
        s.RegisterValue("liberty_desire", (ctx, x) => x is Country c ? LibertyDesire(c) : null);
        s.RegisterEffect("create_non_aggression_pact", (ctx, n) =>
        {
            if (ctx.This is Country a && Other(n, ctx) is { } b) CreatePact(a, b, n.Children != null ? (int)s.ResolveNumber(n.GetString("years") ?? "10", ctx) : 10);
        }, (ctx, n) => $"Non-aggression pact with {Other(n, ctx)?.Name ?? n.Value}");
        s.RegisterEffect("break_non_aggression_pact", (ctx, n) => { if (ctx.This is Country a && Other(n, ctx) is { } b) BreakPact(a, b); },
            (ctx, n) => $"Break the non-aggression pact with {Other(n, ctx)?.Name ?? n.Value}");
        s.RegisterEffect("add_war_exhaustion", (ctx, n) => { if (ctx.This is Country c) SetWarExhaustion(c, WarExhaustion(c) + s.ResolveNumber(n.Value, ctx)); },
            (ctx, n) => $"War exhaustion: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("cede_claimed_state", (ctx, n) =>
        {
            // diplomatic play: the target gives up its most valuable state that ROOT has a core or claim on
            if (ctx.This is not Country a || Other(n, ctx) is not { } b) return;
            var st = b.OwnedStates.Where(x => x.Cores.Contains(a) || x.Claims.Contains(a)).OrderByDescending(x => x.Tax + x.Manpower * 0.01).FirstOrDefault();
            if (st != null && b.OwnedStates.Count > 1) TransferState(st, a);
        }, (ctx, n) => $"{Other(n, ctx)?.Name ?? n.Value} cedes a state we claim");
    }

    // =============================================================================================
    // Save
    // =============================================================================================

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private void SaveWarGoals(Node root)
    {
        foreach (var war in _game.World.Wars)
        {
            var n = root.AddBlock("war");
            n.Add("id", war.Id.ToString(CultureInfo.InvariantCulture));
            n.Add("ticking", F(war.TickingScore));
            n.Add("last_change", war.LastChangeDay.ToString(CultureInfo.InvariantCulture));
            n.Add("occupied", war.LastOccupied.ToString(CultureInfo.InvariantCulture));
            foreach (var g in war.Goals)
            {
                var gn = n.AddBlock("goal");
                gn.Add("holder", g.Holder.Tag);
                gn.Add("target", g.Target.Tag);
                gn.Add("cb", g.CasusBelli.Key);
            }
        }
        var ex = root.AddBlock("exhaustion");
        foreach (var kv in _exhaustion) if (kv.Key.Exists) ex.Add(kv.Key.Tag, F(kv.Value));
        var ml = root.AddBlock("men_lost");
        foreach (var kv in MenLost) ml.Add(kv.Key.Tag, F(kv.Value));
    }

    private void LoadWarGoals(Node root)
    {
        ResetWarGoals();
        var w = _game.World;
        foreach (var n in root.GetAll("war"))
        {
            int id = n.GetInt("id", -1);
            var war = w.Wars.FirstOrDefault(x => x.Id == id);
            if (war == null) continue;
            war.TickingScore = n.GetDouble("ticking");
            war.LastChangeDay = n.GetInt("last_change", w.Date.TotalDays);
            war.LastOccupied = n.GetInt("occupied", -1);
            war.Goals.Clear();
            foreach (var gn in n.GetAll("goal"))
                if (w.GetCountry(gn.GetString("holder")) is { } h && w.GetCountry(gn.GetString("target")) is { } t && _game.Data.CasusBelli.TryGetValue(gn.GetString("cb") ?? "", out var cb))
                    war.Goals.Add(new WarGoal { Holder = h, Target = t, CasusBelli = cb });
        }
        foreach (var c in root.Get("exhaustion")?.All ?? Enumerable.Empty<Node>())
            if (w.GetCountry(c.Key) is { } country) _exhaustion[country] = c.AsDouble();
        foreach (var c in root.Get("men_lost")?.All ?? Enumerable.Empty<Node>())
            if (w.GetCountry(c.Key) is { } country) MenLost[country] = c.AsDouble();
    }

    private void ResetWarGoals()
    {
        _exhaustion.Clear();
        _menLostMonth.Clear();
        MenLost.Clear();
        _liberty.Clear();
        _goalStates.Clear();
    }
}
