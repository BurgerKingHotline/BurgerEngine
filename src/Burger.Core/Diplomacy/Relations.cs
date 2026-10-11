using Burger.Core.Model;

namespace Burger.Core.Diplomacy;

/// <summary>All diplomatic relations between countries: opinions, alliances, access, guarantees, subjects, truces.</summary>
public sealed class Relations
{
    public readonly Dictionary<(Country From, Country To), List<OpinionModifier>> Opinions = new();
    public readonly HashSet<(Country, Country)> Alliances = new();
    /// <summary>(granter, receiver): receiver may move through granter's territory.</summary>
    public readonly HashSet<(Country Granter, Country Receiver)> MilitaryAccess = new();
    /// <summary>(guarantor, guaranteed)</summary>
    public readonly HashSet<(Country Guarantor, Country Guaranteed)> Guarantees = new();
    public readonly Dictionary<Country, (Country Overlord, SubjectType Type)> Subjects = new();
    public readonly Dictionary<(Country, Country), GameDate> Truces = new();
    /// <summary>Non-aggression pacts (pair) and when they run out.</summary>
    public readonly Dictionary<(Country, Country), GameDate> Pacts = new();

    // ------------------------------------------------------------------ non-aggression pacts
    public bool HasPact(Country a, Country b, GameDate now) => a != b && Pacts.TryGetValue(Pair(a, b), out var until) && until > now;
    public void AddPact(Country a, Country b, GameDate until) { if (a != b) Pacts[Pair(a, b)] = until; }
    public void RemovePact(Country a, Country b) => Pacts.Remove(Pair(a, b));

    public static (Country, Country) Pair(Country a, Country b) => a.Index <= b.Index ? (a, b) : (b, a);

    // ------------------------------------------------------------------ alliances
    public bool AreAllied(Country a, Country b) => a != b && Alliances.Contains(Pair(a, b));
    public void SetAlliance(Country a, Country b, bool allied)
    {
        if (a == b) return;
        if (allied) Alliances.Add(Pair(a, b)); else Alliances.Remove(Pair(a, b));
    }
    public IEnumerable<Country> AlliesOf(Country c)
    {
        foreach (var (x, y) in Alliances)
        {
            if (x == c) yield return y;
            else if (y == c) yield return x;
        }
    }

    // ------------------------------------------------------------------ access / guarantees
    public bool HasAccess(Country receiver, Country granter) => MilitaryAccess.Contains((granter, receiver));
    public void SetAccess(Country granter, Country receiver, bool on)
    {
        if (on) MilitaryAccess.Add((granter, receiver)); else MilitaryAccess.Remove((granter, receiver));
    }
    public bool IsGuaranteeing(Country guarantor, Country guaranteed) => Guarantees.Contains((guarantor, guaranteed));
    public void SetGuarantee(Country guarantor, Country guaranteed, bool on)
    {
        if (on) Guarantees.Add((guarantor, guaranteed)); else Guarantees.Remove((guarantor, guaranteed));
    }
    public IEnumerable<Country> GuarantorsOf(Country c) => Guarantees.Where(g => g.Guaranteed == c).Select(g => g.Guarantor);

    // ------------------------------------------------------------------ subjects
    public Country? OverlordOf(Country c) => Subjects.TryGetValue(c, out var s) ? s.Overlord : null;
    public SubjectType? SubjectTypeOf(Country c) => Subjects.TryGetValue(c, out var s) ? s.Type : null;
    public IEnumerable<Country> SubjectsOf(Country c) => Subjects.Where(kv => kv.Value.Overlord == c).Select(kv => kv.Key);
    public void SetSubject(Country subject, Country overlord, SubjectType type) => Subjects[subject] = (overlord, type);
    public void ReleaseSubject(Country subject) => Subjects.Remove(subject);

    // ------------------------------------------------------------------ truces
    public bool HasTruce(Country a, Country b, GameDate now)
        => Truces.TryGetValue(Pair(a, b), out var until) && until > now;
    public void AddTruce(Country a, Country b, GameDate until) => Truces[Pair(a, b)] = until;

    // ------------------------------------------------------------------ opinion
    public List<OpinionModifier> ModifiersOf(Country from, Country to)
    {
        if (!Opinions.TryGetValue((from, to), out var list))
        {
            list = new List<OpinionModifier>();
            Opinions[(from, to)] = list;
        }
        return list;
    }

    public double ModifierSum(Country from, Country to)
        => Opinions.TryGetValue((from, to), out var list) ? list.Sum(m => m.Value) : 0;

    public void AddOpinion(Country from, Country to, string name, double value, double decayPerMonth, GameDate? expires = null)
    {
        if (from == to) return;
        ModifiersOf(from, to).Add(new OpinionModifier { Name = name, Value = value, DecayPerMonth = Math.Abs(decayPerMonth), Expires = expires });
    }

    /// <summary>Monthly decay / expiry of opinion modifiers.</summary>
    public void MonthlyUpdate(GameDate now)
    {
        foreach (var list in Opinions.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var m = list[i];
                if (m.Expires.HasValue && m.Expires.Value <= now) { list.RemoveAt(i); continue; }
                if (m.DecayPerMonth > 0)
                {
                    if (m.Value > 0) m.Value = Math.Max(0, m.Value - m.DecayPerMonth);
                    else m.Value = Math.Min(0, m.Value + m.DecayPerMonth);
                    if (Math.Abs(m.Value) < 0.01) list.RemoveAt(i);
                }
            }
        }
        foreach (var k in Truces.Where(t => t.Value <= now).Select(t => t.Key).ToList()) Truces.Remove(k);
        foreach (var k in Pacts.Where(t => t.Value <= now).Select(t => t.Key).ToList()) Pacts.Remove(k);
    }

    /// <summary>Removes everything involving a country (used when it ceases to exist).</summary>
    public void RemoveCountry(Country c)
    {
        Alliances.RemoveWhere(p => p.Item1 == c || p.Item2 == c);
        MilitaryAccess.RemoveWhere(p => p.Granter == c || p.Receiver == c);
        Guarantees.RemoveWhere(p => p.Guarantor == c || p.Guaranteed == c);
        foreach (var k in Pacts.Keys.Where(k => k.Item1 == c || k.Item2 == c).ToList()) Pacts.Remove(k);
        Subjects.Remove(c);
        foreach (var s in Subjects.Where(kv => kv.Value.Overlord == c).Select(kv => kv.Key).ToList()) Subjects.Remove(s);
    }

    /// <summary>Number of diplomatic relation slots in use (alliances + guarantees given + subjects).</summary>
    public int RelationsUsed(Country c)
        => AlliesOf(c).Count() + Guarantees.Count(g => g.Guarantor == c) + SubjectsOf(c).Count(x => Subjects[x].Type.CountsAsRelation);
}
