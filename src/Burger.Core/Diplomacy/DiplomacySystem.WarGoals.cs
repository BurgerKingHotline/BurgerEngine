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

    /// <summary>States of <paramref name="owner"/> where <paramref name="culture"/> is the biggest culture (never its capital state): what releasing that people frees.</summary>
    public List<State> ReleaseStates(Country owner, string culture)
    {
        var pop = _game.Population;
        if (!pop.Enabled) return new List<State>();
        return owner.OwnedStates.Where(s => s != owner.Capital?.State && pop.MajorityCulture(s) == culture).ToList();
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
    private void MonthlyWars()
    {
        int today = _game.World.Date.TotalDays;
        foreach (var war in _game.World.Wars)
        {
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
    public void MakeSubject(Country subject, Country overlord, SubjectType type)
    {
        if (subject == overlord) return;
        R.SetSubject(subject, overlord, type);
        _liberty.Remove(subject);
        FollowOverlord(subject, overlord, popularity: 0.2);
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
                if (t.Beneficiary == null || !war.SideOf(winner).Contains(t.Beneficiary)) return "Invalid country to free";
                if (R.OverlordOf(t.Beneficiary) is not { } ov || !war.SideOf(loser).Contains(ov)) return $"{t.Beneficiary.Name} is not a subject of the losing side";
                break;
            case PeaceTermType.ReleaseCountry:
                if (!_game.Population.Enabled) return "No peoples in this game";
                if (t.State?.Owner == null || !war.SideOf(loser).Contains(t.State.Owner)) return "Invalid state";
                if (ReleaseStates(t.State.Owner, t.Culture).Count == 0) return $"No {t.Culture} states to release";
                if (t.AsSubject && (t.Beneficiary == null || R.OverlordOf(t.Beneficiary) != null)) return "A subject cannot have subjects of its own";
                if (t.AsSubject && t.Beneficiary != null && _game.Population.IsAccepted(t.Beneficiary, t.Culture))
                    return $"The {t.Culture} people are {t.Beneficiary.Name}'s own: take their land instead";
                break;
            case PeaceTermType.RegimeChange:
                if (!_game.Politics.Enabled || _game.Politics.Ideology(t.Ideology) == null) return "No ideologies in this game";
                break;
        }
        return null;
    }

    private void ApplyNewTerm(PeaceTerm t, Country winner, Country loser)
    {
        switch (t.Type)
        {
            case PeaceTermType.Independence:
                if (t.Beneficiary != null && R.OverlordOf(t.Beneficiary) != null)
                {
                    R.ReleaseSubject(t.Beneficiary);
                    _liberty.Remove(t.Beneficiary);
                    _game.Notify($"{t.Beneficiary.Name} is free of its overlord.", NotificationKind.Diplomacy);
                }
                break;
            case PeaceTermType.ReleaseCountry:
                if (t.State?.Owner is { } owner)
                {
                    var freed = _game.Population.ReleaseCulture(owner, t.Culture);
                    if (freed != null && t.AsSubject)
                        MakeSubject(freed, t.Beneficiary ?? winner, _game.Data.SubjectTypes.Values.First());
                    else if (freed != null) AddOpinion(freed, winner, "Liberated us", 75, 0.5);
                }
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
