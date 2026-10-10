using Burger.Core.Model;

namespace Burger.Core.Population;

/// <summary>Overridable rules for population: growth, capacity, assimilation, conversion, migration, how people turn into tax and manpower, cores and colonies. Subclass and assign to <c>Game.Rules.Population</c> (the population feature puts a copy in backend/Population.cs).</summary>
public class PopulationRules
{
    /// <summary>Yearly growth of a pop before the capacity limit (default 0.016 = 1.6% a year).</summary>
    public virtual double YearlyGrowth(Game game, State s, Pop p)
    {
        double g = game.Data.Defines.Get("pop_growth", 0.016) + game.Modifiers.Sum(s, "pop_growth");
        if (s.Owner != null) g += game.Modifiers.Sum(s.Owner, "pop_growth");
        if (p.Type != null && game.Population.PopTypeByKey.TryGetValue(p.Type, out var t)) g += t.Growth;
        return g;
    }

    /// <summary>How many people the state can hold; growth slows down as it fills and turns negative above it.</summary>
    public virtual double Capacity(Game game, State s, StatePopulation sp)
    {
        double baseCap = Math.Max(game.Data.Defines.Get("min_pop_capacity", 1000), sp.InitialTotal * game.Data.Defines.Get("pop_capacity_factor", 2.0));
        double f = 1 + game.Modifiers.Sum(s, "pop_capacity") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "pop_capacity") : 0);
        return baseCap * Math.Max(0.1, f);
    }

    /// <summary>Share of a foreign-culture pop that takes the owner's culture each year.</summary>
    /// <remarks>Colonies are not assimilated: the colonial power rules them but does not settle or convert them.</remarks>
    public virtual double YearlyAssimilation(Game game, State s, Pop p)
        => game.Population.IsColony(s) ? 0 : game.Data.Defines.Get("assimilation_rate", 0.004) * Math.Max(0, 1 + game.Modifiers.Sum(s, "assimilation") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "assimilation") : 0));

    /// <summary>Share of a pop of another faith that takes the state religion each year (religion feature).</summary>
    public virtual double YearlyConversion(Game game, State s, Pop p)
        => game.Population.IsColony(s) ? 0 : game.Data.Defines.Get("conversion_rate", 0.006) * Math.Max(0, 1 + game.Modifiers.Sum(s, "conversion") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "conversion") : 0));

    /// <summary>Share of a crowded state's people that moves to an emptier state of the same country each month.</summary>
    public virtual double MonthlyMigration(Game game, State from, State to, double fromFill, double toFill)
        => fromFill > 0.85 && fromFill - toFill > 0.15 ? game.Data.Defines.Get("migration_rate", 0.002) * Math.Min(1, (fromFill - toFill) * 2) : 0;

    /// <summary>Is this culture accepted by the country (its own culture, an accepted culture, or one of the same group)?</summary>
    public virtual bool IsAccepted(Game game, Country c, string culture)
    {
        var cp = game.Population.Of(c);
        if (cp.Culture == null) return true;
        if (string.Equals(cp.Culture, culture, StringComparison.OrdinalIgnoreCase) || cp.Accepted.Contains(culture)) return true;
        if (game.Data.Defines.GetBool("culture_group_accepted", true) && game.Population.CultureByKey.TryGetValue(cp.Culture, out var own)
            && game.Population.CultureByKey.TryGetValue(culture, out var other) && own.Group == other.Group) return true;
        return false;
    }

    /// <summary>
    /// Multipliers of a state's tax and manpower: more people than at the start give more; people of unaccepted cultures
    /// or other religions, and states that are not cores (cores_claims feature), give less. Social classes add their modifiers.
    /// </summary>
    public virtual (double Tax, double Manpower) OutputFactors(Game game, State s, StatePopulation sp)
    {
        double total = sp.Total;
        if (total <= 0 || sp.InitialTotal <= 0) return (1, 1);
        var pop = game.Population;
        double popFactor = Math.Clamp(total / sp.InitialTotal, 0.1, 3);
        double accepted = 1, faithful = 1;
        if (s.Owner != null)
        {
            accepted = sp.Pops.Where(p => IsAccepted(game, s.Owner, p.Culture)).Sum(p => p.Size) / total;
            var religion = pop.Of(s.Owner).Religion;
            if (pop.ReligionEnabled && religion != null) faithful = sp.Pops.Where(p => p.Religion == religion).Sum(p => p.Size) / total;
        }
        // culture policy (modifier minority_output): people of unaccepted cultures count as this share of accepted ones
        if (s.Owner != null && accepted < 1)
        {
            double mo = Math.Clamp(game.Modifiers.Sum(s.Owner, "minority_output"), -1, 1);
            accepted = Math.Clamp(accepted + (1 - accepted) * mo, 0, 1);
        }
        double tax = popFactor * (0.5 + 0.5 * accepted) * (pop.ReligionEnabled ? 0.75 + 0.25 * faithful : 1);
        double manpower = popFactor * (0.25 + 0.75 * accepted);
        if (pop.ClassesEnabled)
        {
            double t = 0, m = 0;
            foreach (var p in sp.Pops)
            {
                if (p.Type == null || !pop.PopTypeByKey.TryGetValue(p.Type, out var pt)) continue;
                double share = p.Size / total;
                t += share * pt.Modifier.GetValueOrDefault("tax");
                m += share * pt.Modifier.GetValueOrDefault("manpower");
            }
            tax *= Math.Max(0.1, 1 + t);
            manpower *= Math.Max(0.1, 1 + m);
        }
        if (pop.CoresEnabled && s.Owner != null && !s.Cores.Contains(s.Owner))
        {
            double nc = game.Data.Defines.Get("noncore_output", 0.6);
            tax *= nc;
            manpower *= nc;
        }
        return (tax, manpower);
    }

    /// <summary>
    /// True if the owner makes the state a core (cores_claims): at least half its people (core_culture_share) must be of
    /// cultures the owner accepts, and the owner must have held it for core_years (25; 0 if the owner's culture is the majority).
    /// </summary>
    public virtual bool CanCore(Game game, State s, Country owner, double acceptedShare, double yearsOwned, bool ownCultureMajority)
    {
        var d = game.Data.Defines;
        if (acceptedShare < d.Get("core_culture_share", 0.5)) return false;
        return ownCultureMajority || yearsOwned >= d.Get("core_years", 25);
    }

    /// <summary>Years after which a country loses a core it no longer owns (unless its culture lives there).</summary>
    public virtual double CoreLossYears(Game game, State s, Country former) => game.Data.Defines.Get("core_loss_years", 50);

    /// <summary>Monthly colony progress in percent (colonization feature).</summary>
    public virtual double ColonyProgress(Game game, State s, Country c, double nativeShare)
        => game.Data.Defines.Get("colony_speed", 4) * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "colony_speed")) * (1 - 0.5 * nativeShare);

    /// <summary>
    /// Monthly progress (points of 100) of an independence movement of <paramref name="culture"/> (independence feature).
    /// <paramref name="share"/> is that culture's share of the country's people in the states it would take; colonies are
    /// calmer than conquered land (colonial_independence_factor, 0.6), low stability makes it faster; accepting the culture ends it.
    /// </summary>
    public virtual double IndependenceProgress(Game game, Country c, string culture, double share, bool colonial)
    {
        var d = game.Data.Defines;
        double stability = game.Politics.Enabled ? game.Rules.Politics.StabilityOf(game, c) : 50;
        double atWar = game.World.WarsOf(c).Any(w => w.ScoreFor(c) < -20) ? 1.5 : 1;
        // a national party in parliament gives the movement a voice
        double party = game.Politics.Enabled ? 1 + 2 * game.Politics.SeatShare(c, "culture:" + culture) : 1;
        return d.Get("independence_speed", 0.4) * (0.6 + Math.Min(1, share * 5)) * (colonial ? d.Get("colonial_independence_factor", 0.6) : 1)
               * (1.6 - stability / 100.0) * atWar * party * game.Population.DecolonisationPressure * Math.Max(0, 1 + game.Modifiers.Sum(c, "independence_speed"));
    }

    /// <summary>Opinion between countries because of their state religions (religion feature).</summary>
    public virtual double ReligionOpinion(Game game, ReligionDef? a, ReligionDef? b)
    {
        if (a == null || b == null) return 0;
        var d = game.Data.Defines;
        if (a == b) return d.Get("same_religion_opinion", 10);
        if (a.Group == b.Group) return d.Get("same_religion_group_opinion", 0);
        return d.Get("other_religion_opinion", -10);
    }
}
