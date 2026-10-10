// Population rules: starting people, pop growth and state capacity, assimilation, religious conversion, class promotion,
// migration, how people turn into tax and manpower (accepted cultures, faith, social classes, cores), unrest, accepting
// cultures, core gain/loss and claims, what counts as a colony, colonization (cost, range, speed, natives, settlers),
// independence (who wants to leave, suppression, the war and how it ends, decolonisation) and religious opinion.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>Overridable rules for population: growth, capacity, assimilation, conversion, migration, how people turn into tax and manpower, cores and colonies. Subclass and assign to <c>Game.Rules.Population</c> (the population feature puts a copy in backend/Population.cs).</summary>
public class GamePopulationRules : PopulationRules
{
    /// <summary>Yearly growth of a pop before the capacity limit (default 0.016 = 1.6% a year).</summary>
    public override double YearlyGrowth(Game game, State s, Pop p)
    {
        double g = game.Data.Defines.Get("pop_growth", 0.016) + game.Modifiers.Sum(s, "pop_growth");
        if (s.Owner != null) g += game.Modifiers.Sum(s.Owner, "pop_growth");
        if (p.Type != null && game.Population.PopTypeByKey.TryGetValue(p.Type, out var t)) g += t.Growth;
        return g;
    }

    /// <summary>How many people the state can hold; growth slows down as it fills and turns negative above it.</summary>
    public override double Capacity(Game game, State s, StatePopulation sp)
    {
        double baseCap = Math.Max(game.Data.Defines.Get("min_pop_capacity", 1000), sp.InitialTotal * game.Data.Defines.Get("pop_capacity_factor", 2.0));
        double f = 1 + game.Modifiers.Sum(s, "pop_capacity") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "pop_capacity") : 0);
        return baseCap * Math.Max(0.1, f);
    }

    /// <summary>Share of a foreign-culture pop that takes the owner's culture each year.</summary>
    /// <remarks>Colonies are not assimilated: the colonial power rules them but does not settle or convert them.</remarks>
    public override double YearlyAssimilation(Game game, State s, Pop p)
        => game.Population.IsColony(s) ? 0 : game.Data.Defines.Get("assimilation_rate", 0.004) * Math.Max(0, 1 + game.Modifiers.Sum(s, "assimilation") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "assimilation") : 0));

    /// <summary>Share of a pop of another faith that takes the state religion each year (religion feature).</summary>
    public override double YearlyConversion(Game game, State s, Pop p)
        => game.Population.IsColony(s) ? 0 : game.Data.Defines.Get("conversion_rate", 0.006) * Math.Max(0, 1 + game.Modifiers.Sum(s, "conversion") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "conversion") : 0));

    /// <summary>Share of a crowded state's people that moves to an emptier state of the same country each month.</summary>
    public override double MonthlyMigration(Game game, State from, State to, double fromFill, double toFill)
        => fromFill > 0.85 && fromFill - toFill > 0.15 ? game.Data.Defines.Get("migration_rate", 0.002) * Math.Min(1, (fromFill - toFill) * 2) : 0;

    /// <summary>Is this culture accepted by the country (its own culture, an accepted culture, or one of the same group)?</summary>
    public override bool IsAccepted(Game game, Country c, string culture)
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
    public override (double Tax, double Manpower) OutputFactors(Game game, State s, StatePopulation sp)
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
    public override bool CanCore(Game game, State s, Country owner, double acceptedShare, double yearsOwned, bool ownCultureMajority)
    {
        var d = game.Data.Defines;
        if (acceptedShare < d.Get("core_culture_share", 0.5)) return false;
        return ownCultureMajority || yearsOwned >= d.Get("core_years", 25);
    }

    /// <summary>Years after which a country loses a core it no longer owns (unless its culture lives there); a country that no longer exists loses it after a year, without keeping a claim.</summary>
    public override double CoreLossYears(Game game, State s, Country former) => former.Exists ? game.Data.Defines.Get("core_loss_years", 50) : 1;

    /// <summary>Monthly colony progress in percent (colonization feature).</summary>
    public override double ColonyProgress(Game game, State s, Country c, double nativeShare)
        => game.Data.Defines.Get("colony_speed", 4) * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "colony_speed")) * (1 - 0.5 * nativeShare);

    /// <summary>
    /// Monthly progress (points of 100) of an independence movement of <paramref name="culture"/> (independence feature).
    /// <paramref name="share"/> is that culture's share of the country's people in the states it would take; colonies are
    /// calmer than conquered land (colonial_independence_factor, 0.6), low stability makes it faster; accepting the culture ends it.
    /// </summary>
    public override double IndependenceProgress(Game game, Country c, string culture, double share, bool colonial)
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
    public override double ReligionOpinion(Game game, ReligionDef? a, ReligionDef? b)
    {
        if (a == null || b == null) return 0;
        var d = game.Data.Defines;
        if (a == b) return d.Get("same_religion_opinion", 10);
        if (a.Group == b.Group) return d.Get("same_religion_group_opinion", 0);
        return d.Get("other_religion_opinion", -10);
    }

    // =============================================================================================
    // Pops: starting people, growth curve, classes, migration, unrest
    // =============================================================================================

    /// <summary>People of a state without an entry in history/pops: as many as its manpower suggests (pops_per_manpower, at least min_state_pops).</summary>
    public override double StartingPops(Game game, State s)
        => Math.Max(game.Data.Defines.Get("min_state_pops", 500), Math.Max(s.Manpower, 5) * game.Data.Defines.Get("pops_per_manpower", 100));

    /// <summary>
    /// Change of a pop this month as a share of its size. <paramref name="yearlyRate"/> is <see cref="YearlyGrowth"/>,
    /// <paramref name="room"/> the free share of the state's capacity (negative when it is overcrowded): growth slows as
    /// the state fills; above capacity people die off or leave (slowly).
    /// </summary>
    public override double MonthlyGrowth(Game game, State s, Pop p, double yearlyRate, double room)
        => (room >= 0 ? yearlyRate * room : Math.Max(-0.05, room * 0.1)) / 12;

    /// <summary>Share of a pop that rises into its class's promote_to class each year (social_classes feature).</summary>
    public override double YearlyPromotion(Game game, State s, Pop p, PopTypeDef type)
        => type.PromoteRate * Math.Max(0, 1 + game.Modifiers.Sum(s, "promotion") + (s.Owner != null ? game.Modifiers.Sum(s.Owner, "promotion") : 0));

    /// <summary>
    /// The state people of <paramref name="from"/> move to this month (null = nobody leaves): by default the emptiest
    /// neighbouring state of the same country, once the state is 85% full. <see cref="MonthlyMigration"/> says how many go.
    /// </summary>
    public override State? MigrationTarget(Game game, State from, double fill)
    {
        if (fill < 0.85) return null;
        var pop = game.Population;
        State? best = null;
        double bestFill = fill;
        foreach (var n in pop.NeighbourStates(from))
        {
            if (n.Owner != from.Owner) continue;
            double nf = pop.FillOf(n);
            if (nf < bestFill) { bestFill = nf; best = n; }
        }
        return best;
    }

    /// <summary>
    /// Stability lost (modifier stability_target) because of people of unaccepted cultures and other faiths. The shares are
    /// of the country's people outside its colonies; culture policy laws (modifier culture_tension) weigh cultures more or less.
    /// </summary>
    public override double StabilityPenalty(Game game, Country c, double acceptedShare, double faithfulShare)
    {
        var d = game.Data.Defines;
        double tension = Math.Max(0, 1 + game.Politics.ModifierValue(c, "culture_tension"));
        return (1 - acceptedShare) * d.Get("culture_stability", 10) * tension + (game.Population.ReligionEnabled ? (1 - faithfulShare) * d.Get("religion_stability", 10) : 0);
    }

    // =============================================================================================
    // Accepted cultures, claims and colonies
    // =============================================================================================

    /// <summary>Cost of accepting a culture: political power with politics, else gold with the economy, else free (currency "gold" or the power resource's name).</summary>
    public override (double Amount, string Currency) AcceptCultureCost(Game game, Country c)
    {
        double cost = game.Data.Defines.Get("accept_culture_cost", 100);
        if (game.Politics.PowerResource is { } pr) return (cost, pr.Name);
        if (game.Features.Has(FeatureSet.Economy)) return (cost, "gold");
        return (0, "");
    }

    /// <summary>
    /// Why the country cannot accept a culture it does not accept yet (null = it can; the cost is checked separately):
    /// the people must be accept_culture_share (5%) of its people outside colonies, and it has at most max_accepted_cultures (3).
    /// </summary>
    public override string? CannotAcceptCulture(Game game, Country c, string culture)
    {
        var pop = game.Population;
        // only people outside colonies count: a people that lives only in our colonies cannot be accepted
        var home = pop.HomeStates(c).ToList();
        double homeTotal = home.Sum(pop.TotalOf);
        double share = pop.Breakdown(home, p => p.Culture).Where(x => x.Key == culture).Sum(x => x.Size) / Math.Max(1, homeTotal);
        double need = game.Data.Defines.Get("accept_culture_share", 0.05);
        if (share <= 0 && pop.Breakdown(c.OwnedStates, p => p.Culture).Any(x => x.Key == culture)) return "They live only in our colonies";
        if (share < need) return $"Needs at least {need:P0} of our people outside colonies ({share:P1} now)";
        int max = (int)game.Data.Defines.Get("max_accepted_cultures", 3);
        if (pop.Of(c).Accepted.Count >= max) return $"At most {max} accepted cultures";
        return null;
    }

    /// <summary>Does the country claim this neighbouring state of another country (asked every January, cores_claims)? By default where its own culture is the majority.</summary>
    public override bool ClaimsNeighbour(Game game, Country c, State s)
        => game.Population.Of(c).Culture is { } own && game.Population.MajorityCulture(s) == own;

    /// <summary>
    /// A colony: a state marked <c>category = colony</c>, or (with cores) a state the owner has no core on that is not
    /// connected by land to its capital. Colonies are not assimilated or converted and do not count for the country's culture figures.
    /// </summary>
    public override bool IsColony(Game game, State s)
    {
        if (s.Owner == null) return false;
        if (string.Equals(s.Category, "colony", StringComparison.OrdinalIgnoreCase)) return true;
        if (!game.Population.CoresEnabled || s.Cores.Contains(s.Owner)) return false;
        return !game.Population.HomeOf(s.Owner).Contains(s);
    }

    // =============================================================================================
    // Colonization (colonization feature)
    // =============================================================================================

    /// <summary>How many colonies the country can settle at once (define colonists, modifier colonists).</summary>
    public override int MaxColonies(Game game, Country c)
        => Math.Max(0, (int)Math.Round(game.Data.Defines.Get("colonists", 1) + game.Modifiers.Sum(c, "colonists")));

    /// <summary>Gold for sending colonists (colonize_cost; free without the economy).</summary>
    public override double ColonizeCost(Game game)
        => game.Features.Has(FeatureSet.Economy) ? game.Data.Defines.Get("colonize_cost", 50) : 0;

    /// <summary>How far from its land the country can settle (colony_range_km, modifier colony_range).</summary>
    public override double ColonyRangeKm(Game game, Country c)
        => game.Data.Defines.Get("colony_range_km", 1200) * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "colony_range"));

    /// <summary>Gold a growing colony costs each month (colony_upkeep; free without the economy).</summary>
    public override double ColonyUpkeep(Game game, State s, Country c)
        => game.Features.Has(FeatureSet.Economy) ? game.Data.Defines.Get("colony_upkeep", 1) : 0;

    /// <summary>Colony progress lost this month because natives attacked the colonists (0 = no attack): now and then (native_resistance) where natives live.</summary>
    public override double NativeAttack(Game game, State s, Country c, double nativeShare)
        => nativeShare > 0.05 && game.Rng.NextDouble() < game.Data.Defines.Get("native_resistance", 0.05) * nativeShare ? 15 : 0;

    /// <summary>People of the colonizer's culture that settle a finished colony (colony_settlers).</summary>
    public override double ColonySettlers(Game game, State s, Country c) => game.Data.Defines.Get("colony_settlers", 1000);

    /// <summary>Base tax and manpower of a finished colony that had none (colony_tax, colony_manpower).</summary>
    public override (double Tax, double Manpower) ColonyBaseOutput(Game game, State s, Country c)
        => (game.Data.Defines.Get("colony_tax", 1), game.Data.Defines.Get("colony_manpower", 20));

    /// <summary>AI: how much the country wants to settle a state it can colonize (the highest wins): by default the nearest.</summary>
    public override double AiColonyScore(Game game, Country c, State s) => -game.Population.DistanceKm(c, s);

    // =============================================================================================
    // Independence (independence feature)
    // =============================================================================================

    /// <summary>
    /// The culture that wants to break away with this state (null = none): by default the state's majority culture when
    /// the owner does not accept it; never in the capital state.
    /// </summary>
    public override string? SeparatistCulture(Game game, Country c, State s)
    {
        if (s == c.Capital?.State) return null;
        string? major = game.Population.MajorityCulture(s);
        return major == null || game.Population.IsAccepted(c, major) ? null : major;
    }

    /// <summary>Share of the country's people a culture needs in its separatist states to form a movement (independence_min_share); under half of it the movement fades.</summary>
    public override double IndependenceMinShare(Game game, Country c, string culture) => game.Data.Defines.Get("independence_min_share", 0.02);

    /// <summary>Cost of suppressing a movement: suppress_independence_cost (50), half again more for every earlier suppression.</summary>
    public override double SuppressIndependenceCost(Game game, IndependenceMovement m)
        => game.Data.Defines.Get("suppress_independence_cost", 50) * (1 + 0.5 * m.Suppressed);

    /// <summary>What suppressing does: progress the movement loses and stability it costs the country (with a stability resource).</summary>
    public override (double Progress, double Stability) SuppressIndependenceEffect(Game game, IndependenceMovement m)
        => (game.Data.Defines.Get("suppress_independence_progress", 20), game.Data.Defines.Get("suppress_independence_stability", 3));

    /// <summary>
    /// Decolonisation pressure (1 = none yet) after <paramref name="freed"/> countries won or were granted independence:
    /// decolonisation_step (0.05) each, up to max_decolonisation_pressure (2).
    /// </summary>
    public override double DecolonisationPressure(Game game, int freed)
        => Math.Min(game.Data.Defines.Get("max_decolonisation_pressure", 2), 1 + freed * game.Data.Defines.Get("decolonisation_step", 0.05));

    /// <summary>Opinion <paramref name="from"/> has of <paramref name="to"/> because of its colonies (countries with many colonies themselves do not mind).</summary>
    public override double ColonialOpinion(Game game, Country from, Country to, int fromColonies, int toColonies)
    {
        var d = game.Data.Defines;
        if (toColonies == 0 || fromColonies >= d.Get("colonial_power_colonies", 5)) return 0;
        return -Math.Min(d.Get("max_colonial_opinion", 25), toColonies * d.Get("colonial_opinion_per_colony", 0.5)) * game.Population.DecolonisationPressure;
    }

    /// <summary>Name of a new country of a people that breaks away (culture country_name, default "&lt;Culture&gt; Republic").</summary>
    public override string IndependentCountryName(Game game, Country origin, string culture, List<State> states)
    {
        var def = game.Population.Culture(culture);
        return def?.CountryName ?? $"{def?.Name ?? Localisation.GetOrPretty(culture)} Republic";
    }

    /// <summary>Chance that a division of the old country standing in the rebel land goes over to the rebels (independence_garrison_defect).</summary>
    public override double GarrisonDefectChance(Game game, Country origin, Country rebel) => game.Data.Defines.Get("independence_garrison_defect", 0.5);

    /// <summary>Militia divisions that rise with a declaration of independence (one per independence_people_per_division people, at most 24).</summary>
    public override int IndependenceDivisions(Game game, Country origin, Country rebel, List<State> states)
    {
        double people = states.Sum(game.Population.TotalOf);
        return Math.Clamp((int)(people / game.Data.Defines.Get("independence_people_per_division", 1500000)), Math.Min(states.Count, 12), 24);
    }

    /// <summary>
    /// How a war of independence stands. <paramref name="rebelHeld"/> and <paramref name="originHeld"/> are the shares of
    /// their own land each side still controls: rebels that lost nearly everything (or their capital and most of their land)
    /// are crushed; rebels that still hold half after independence_war_months (24), or whose ruler collapsed, are free.
    /// </summary>
    public override IndependenceWarResult IndependenceWarOutcome(Game game, IndependenceWar iw, double rebelHeld, double originHeld, double months)
    {
        bool capitalLost = iw.Rebel.Capital == null || iw.Rebel.Capital.Controller != iw.Rebel;
        if (rebelHeld <= 0.05 || (capitalLost && rebelHeld <= 0.3)) return IndependenceWarResult.Crushed;
        if (months >= game.Data.Defines.Get("independence_war_months", 24) && rebelHeld >= 0.5 || originHeld <= 0.3) return IndependenceWarResult.Won;
        return IndependenceWarResult.Ongoing;
    }

    /// <summary>AI: what to do about a movement this month: suppress those that get close, let colonies go when it cannot.</summary>
    public override IndependenceResponse AiIndependenceResponse(Game game, Country c, IndependenceMovement m)
    {
        var pop = game.Population;
        if (m.Progress < 70) return IndependenceResponse.None;
        if (pop.CannotSuppressIndependence(m) == null && game.Rng.NextDouble() < 0.35) return IndependenceResponse.Suppress;
        if (m.Progress > 90 && game.Rng.NextDouble() < 0.1 && pop.SeparatistStates(c, m.Culture).All(pop.IsColony)) return IndependenceResponse.Grant;
        return IndependenceResponse.None;
    }
}

/// <summary>Installs the population rules.</summary>
public sealed class PopulationSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Population = new GamePopulationRules();
    }
}
