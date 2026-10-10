using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Politics;

/// <summary>Overridable rules for politics: how popularity drifts, who forms coalitions, how movements grow and when civil wars break out. Subclass and assign to <c>Game.Rules.Politics</c> (the politics feature puts a copy in backend/Politics.cs).</summary>
public class PoliticsRules
{
    /// <summary>
    /// Monthly change of each ideology's popularity, in percentage points (they are normalised to 100% afterwards).
    /// Sources: modifiers named <c>IDEOLOGY_drift</c> (laws, ministers, events), the ruling party's propaganda,
    /// lost wars, low stability, ideologies ruling the neighbours, and a little chance.
    /// </summary>
    public virtual Dictionary<string, double> MonthlyDrift(Game game, Country c)
    {
        var pol = game.Politics;
        var p = pol.Of(c);
        var d = game.Data.Defines;
        var drift = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in pol.Ideologies) drift[i.Key] = game.Modifiers.Sum(c, i.Key + "_drift");
        void Add(string ideology, double v) { if (drift.ContainsKey(ideology)) drift[ideology] += v; }

        // propaganda: a ruling party that holds no elections keeps itself popular (plus modifiers named ruling_party_drift)
        if (!pol.HoldsElections(c)) Add(p.Ruling, d.Get("ruling_party_drift", 0.4));
        Add(p.Ruling, game.Modifiers.Sum(c, "ruling_party_drift"));
        // the ruling party is blamed for lost wars; extremists profit
        foreach (var war in game.World.WarsOf(c))
            if (war.ScoreFor(c) < -20)
            {
                Add(p.Ruling, -d.Get("war_loss_drift", 1.0));
                foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling)) Add(i.Key, d.Get("war_loss_drift", 1.0) / 2);
            }
        // unrest: low stability helps extremists, high stability helps the government
        double stability = StabilityOf(game, c);
        if (stability < 30)
        {
            Add(p.Ruling, -0.4);
            foreach (var i in pol.Ideologies.Where(i => i.Extremist && i.Key != p.Ruling)) Add(i.Key, 0.4);
        }
        else if (stability > 70) Add(p.Ruling, 0.2);
        // ideas spread from neighbours (stronger neighbours spread more)
        {
            double own = Math.Max(1, game.Rules.Diplomacy.MilitaryStrength(game, c));
            var fromNeighbours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in Burger.Core.Scripting.Builtins.NeighborCountries(game, c))
            {
                string r = pol.Of(n).Ruling;
                if (r == p.Ruling || r.Length == 0) continue;
                double power = Math.Max(1, game.Rules.Diplomacy.MilitaryStrength(game, n));
                fromNeighbours[r] = fromNeighbours.GetValueOrDefault(r) + d.Get("neighbour_drift", 0.1) * Math.Min(2, power / own);
            }
            foreach (var kv in fromNeighbours) Add(kv.Key, Math.Min(0.6, kv.Value));
        }
        // a ruling party that holds no elections keeps a core of support (repression, patronage, the army)
        if (!pol.HoldsElections(c))
        {
            double floor = d.Get("ruling_party_floor", 0.15), cur = p.Popularity.GetValueOrDefault(p.Ruling);
            if (cur < floor) Add(p.Ruling, (floor - cur) * 100 * 0.15);
        }
        // chance
        foreach (var i in pol.Ideologies) Add(i.Key, (game.Rng.NextDouble() - 0.5) * d.Get("random_drift", 0.4));
        return drift;
    }

    /// <summary>Stability of a country, 0-100 (the 'stability' resource; 50 if the game has none).</summary>
    public virtual double StabilityOf(Game game, Country c)
    {
        var def = game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
        return def == null ? 50 : game.Resources.Get(c, def);
    }

    /// <summary>Share of the vote of each ideology in an election (popularity with a little chance).</summary>
    public virtual Dictionary<string, double> Votes(Game game, Country c)
    {
        var p = game.Politics.Of(c);
        var votes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double noise = game.Data.Defines.Get("election_noise", 0.03);
        foreach (var kv in p.Popularity) votes[kv.Key] = Math.Max(0, kv.Value + (game.Rng.NextDouble() * 2 - 1) * noise);
        double sum = votes.Values.Sum();
        if (sum > 0) foreach (var k in votes.Keys.ToList()) votes[k] /= sum;
        return votes;
    }

    /// <summary>
    /// Votes of minority parties (population features): people of an unaccepted culture vote for its national party
    /// (minority_party_turnout, 70%, of them) and people of a faith other than the state religion that make up at least
    /// religious_party_min_share (10%) vote for a religious party (religious_party_turnout, 40%). Only people outside
    /// colonies vote. Keys: culture:KEY and religion:KEY; values are shares of all votes.
    /// </summary>
    public virtual Dictionary<string, double> MinorityVotes(Game game, Country c)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var pop = game.Population;
        var d = game.Data.Defines;
        var voters = c.OwnedStates.Where(s => pop.HasPops(s) && !pop.IsColony(s)).ToList();
        double total = voters.Sum(pop.TotalOf);
        if (total <= 0) return result;
        double minShare = d.Get("minority_party_min_share", 0.03);
        foreach (var (culture, size) in pop.Breakdown(voters, p => p.Culture))
        {
            double share = size / total;
            if (share >= minShare && !pop.IsAccepted(c, culture)) result["culture:" + culture] = share * d.Get("minority_party_turnout", 0.7);
        }
        if (pop.ReligionEnabled && pop.Of(c).Religion is { } state)
            foreach (var (religion, size) in pop.Breakdown(voters, p => p.Religion))
            {
                double share = size / total;
                if (religion != state && share >= d.Get("religious_party_min_share", 0.1)) result["religion:" + religion] = share * d.Get("religious_party_turnout", 0.4);
            }
        return result;
    }

    /// <summary>True if the parties of these two ideologies govern together (both must accept each other).</summary>
    public virtual bool WillGovernWith(Game game, Country c, IdeologyDef a, IdeologyDef b)
        => a.Key == b.Key || (a.Coalition.Contains(b.Key) && b.Coalition.Contains(a.Key));

    /// <summary>Monthly progress (points of 100) of a movement whose ideology has <paramref name="popularity"/> support.</summary>
    public virtual double MovementProgress(Game game, Country c, Movement m, double popularity)
    {
        double threshold = game.Data.Defines.Get("movement_threshold", 0.25);
        if (popularity < threshold) return -5;
        double stability = StabilityOf(game, c);
        return game.Data.Defines.Get("movement_speed", 0.5) * (popularity - threshold) * 100 * (1.5 - stability / 100.0);
    }

    /// <summary>
    /// Monthly chance that a shut-out ideology with this much support takes power in a coup (no war).
    /// Likelier the more popular it is, the less popular the government is and the lower stability is.
    /// </summary>
    public virtual double CoupChance(Game game, Country c, IdeologyDef ideology, double popularity)
    {
        var d = game.Data.Defines;
        double threshold = d.Get("coup_threshold", 0.4);
        if (popularity < threshold) return 0;
        double ruling = game.Politics.Of(c).Popularity.GetValueOrDefault(game.Politics.Of(c).Ruling);
        double stability = StabilityOf(game, c);
        return d.Get("coup_chance", 0.06) * (1 + (popularity - threshold) * 5) * (1.5 - ruling) * (1.5 - stability / 100.0);
    }

    /// <summary>Monthly chance that a shut-out ideology with this much support starts a civil war (HOI4 style).</summary>
    public virtual double CivilWarChance(Game game, Country c, IdeologyDef ideology, double popularity)
    {
        double threshold = game.Data.Defines.Get("civil_war_threshold", 0.5);
        if (popularity < threshold) return 0;
        return game.Data.Defines.Get("civil_war_chance", 0.08) * (0.5 + (popularity - threshold) * 2);
    }

    /// <summary>
    /// True if a revolt turns into a civil war, false if it is a coup: a government that still has real support
    /// (civil_war_loyal_share, 20%) fights; an unpopular one is simply pushed aside.
    /// </summary>
    public virtual bool RevoltIsCivilWar(Game game, Country c, string ideology, double popularity)
    {
        var p = game.Politics.Of(c);
        double loyal = p.Government.Sum(g => p.Popularity.GetValueOrDefault(g));
        return loyal >= game.Data.Defines.Get("civil_war_loyal_share", 0.2) && popularity < game.Data.Defines.Get("coup_above", 0.75);
    }

    /// <summary>Share of the country (states, divisions, treasury) that joins the rebels: the ideology's support.</summary>
    public virtual double CivilWarShare(Game game, Country c, string ideology, double popularity)
        => Math.Clamp(popularity, 0.1, 0.9);

    /// <summary>Years after a coup or civil war before the next one can start.</summary>
    public virtual double CalmYears(Game game, Country c) => game.Data.Defines.Get("revolt_calm_years", 5);
}
