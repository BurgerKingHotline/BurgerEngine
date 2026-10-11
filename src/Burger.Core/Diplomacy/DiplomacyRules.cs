using Burger.Core.Model;
using Burger.Core.Modding;

namespace Burger.Core.Diplomacy;

/// <summary>
/// Overridable diplomacy formulas (Imperator-style by default). Subclass and assign to
/// <c>Game.Rules.Diplomacy</c> from a C# mod to change opinions, war rules, call-to-arms or peace costs.
/// Data-driven diplomatic actions live in <c>common/diplomatic_actions</c>.
/// </summary>
public class DiplomacyRules
{
    /// <summary>Opinion that comes from the current relationship (added to opinion modifiers).</summary>
    public virtual double BaseOpinion(Game game, Country from, Country to)
    {
        var r = game.World.Relations;
        double v = 0;
        if (r.AreAllied(from, to)) v += 25;
        if (r.OverlordOf(from) == to || r.OverlordOf(to) == from) v += 10;
        if (r.IsGuaranteeing(to, from)) v += 15;
        if (game.World.AtWar(from, to)) v -= 100;
        if (r.HasTruce(from, to, game.World.Date)) v -= 10;
        // two countries of one people are rivals for the same nation (same_culture_opinion; not an overlord and its subject)
        if (game.Population.Enabled && r.OverlordOf(from) != to && r.OverlordOf(to) != from
            && game.Population.Of(from).Culture is { } people && people == game.Population.Of(to).Culture)
            v += game.Data.Defines.Get("same_culture_opinion", -60);
        v += game.Modifiers.Sum(to, "opinion");
        if (game.ScriptValues.TryEval("base_opinion", from, out var scripted,
                new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["target"] = to, ["recipient"] = to }))
            v += scripted;
        return v;
    }

    /// <summary>How many alliances/guarantees/subjects a country can have.</summary>
    public virtual int MaxRelations(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("max_diplomatic_relations", c, out var scripted)) return (int)Math.Floor(scripted);
        var d = game.Data.Defines;
        double stat = c.Ruler?.GetStat(d.RelationsStat) ?? 0;
        return d.BaseDiplomaticRelations + (int)Math.Floor(stat * d.RelationsPerStatPoint + game.Modifiers.Sum(c, "relations"));
    }

    /// <summary>Reason why war cannot be declared, or null if allowed.</summary>
    public virtual string? CannotDeclareWarReason(Game game, Country attacker, Country defender, CasusBelli? cb)
    {
        var w = game.World;
        if (attacker == defender) return "Cannot declare war on yourself";
        if (!attacker.Exists || !defender.Exists) return "Country does not exist";
        if (w.AtWar(attacker, defender)) return "Already at war";
        if (w.Relations.AreAllied(attacker, defender)) return "You are allied";
        if (w.Relations.HasTruce(attacker, defender, w.Date)) return "There is a truce";
        if (w.Relations.HasPact(attacker, defender, w.Date)) return "We have a non-aggression pact (break it first)";
        var overlordType = w.Relations.SubjectTypeOf(attacker);
        bool independenceWar = cb?.WarGoal == WarGoalType.Independence && w.Relations.OverlordOf(attacker) == defender;
        if (overlordType != null && !overlordType.CanDeclareWar && !independenceWar) return "Subjects cannot declare war";
        if (cb?.WarGoal == WarGoalType.Independence && !independenceWar) return "Only a subject can fight its overlord for independence";
        if (cb != null && NeedsGoalStates(cb.WarGoal) && WarGoalStates(game, attacker, defender, cb).Count == 0)
            return $"No states to take for '{cb.Name}'";
        if (w.Relations.OverlordOf(defender) == attacker) return "Cannot attack your own subject";
        if (cb?.WarGoal != WarGoalType.Independence && !HasWarReach(game, attacker, defender)) return "Too far away to make war";
        if (cb?.Allow != null)
        {
            var ctx = game.Context(attacker);
            ctx.Scopes["actor"] = attacker;
            ctx.Scopes["recipient"] = defender;
            ctx.Scopes["target"] = defender;
            if (!game.Script.EvalTrigger(cb.Allow, ctx)) return $"Casus belli '{cb.Name}' is not valid";
        }
        return null;
    }

    /// <summary>True if a war goal is about taking particular states (they must exist for the casus belli to be valid).</summary>
    public virtual bool NeedsGoalStates(WarGoalType t) => t is WarGoalType.Claims or WarGoalType.Cores or WarGoalType.Culture or WarGoalType.Religion or WarGoalType.Liberation;

    /// <summary>
    /// States of <paramref name="target"/> (and its subjects) a war goal is about: claimed or cored states, states where
    /// the holder's culture or faith is the majority, states of an unaccepted people to liberate, or (conquest, ideology,
    /// holy war...) every state.
    /// </summary>
    public virtual List<State> WarGoalStates(Game game, Country holder, Country target, CasusBelli cb)
    {
        var pop = game.Population;
        var states = target.OwnedStates.Concat(game.World.Relations.SubjectsOf(target).SelectMany(s => s.OwnedStates));
        return cb.WarGoal switch
        {
            WarGoalType.Claims => states.Where(s => s.Claims.Contains(holder)).ToList(),
            WarGoalType.Cores => states.Where(s => s.Cores.Contains(holder)).ToList(),
            WarGoalType.Culture => pop.Enabled && pop.Of(holder).Culture is { } cu ? states.Where(s => pop.MajorityCulture(s) == cu).ToList() : new List<State>(),
            WarGoalType.Religion => pop.ReligionEnabled && pop.Of(holder).Religion is { } re ? states.Where(s => pop.MajorityReligion(s) == re).ToList() : new List<State>(),
            WarGoalType.Liberation => pop.Enabled ? states.Where(s => s.Owner is { } o && s != o.Capital?.State && pop.MajorityCulture(s) is { } mc && !pop.IsAccepted(o, mc) && pop.Of(holder).Culture != mc).ToList() : new List<State>(),
            WarGoalType.Independence or WarGoalType.Subjugation or WarGoalType.Humiliation => new List<State>(),
            _ => states.ToList(),
        };
    }

    /// <summary>
    /// With the define war_reach (1) a country can only go to war with one it can reach: a neighbour (or a neighbour of a neighbour - a sea crossing,
    /// a buffer state), or any country if it is one of the great powers (the war_reach_powers, 8, strongest). Keeps Poland from fighting Persia.
    /// </summary>
    public virtual bool HasWarReach(Game game, Country attacker, Country defender)
    {
        if (game.Data.Defines.Get("war_reach", 0) <= 0) return true;
        var near = Burger.Core.Scripting.Builtins.NeighborCountries(game, attacker);
        if (near.Contains(defender)) return true;
        if (IsGreatPower(game, attacker)) return true;
        foreach (var n in near) if (Burger.Core.Scripting.Builtins.NeighborCountries(game, n).Contains(defender)) return true;
        return false;
    }

    private int _powersMonth = -1;
    private HashSet<Country> _powers = new();

    /// <summary>One of the war_reach_powers (8) strongest countries (recomputed monthly).</summary>
    public virtual bool IsGreatPower(Game game, Country c)
    {
        int month = game.World.Date.Year * 12 + game.World.Date.Month;
        if (month != _powersMonth)
        {
            _powersMonth = month;
            // (the great powers are the industrial ones - factories, else strength: Poland and Yugoslavia have big armies, not big industries)
            double Weight(Country x) => game.Production.Enabled ? game.Production.CivilianFactories(x) + game.Production.MilitaryFactories(x) : game.Diplomacy.Strength(x);
            _powers = game.World.Countries.Where(x => x.Exists).OrderByDescending(Weight).Take((int)game.Data.Defines.Get("war_reach_powers", 8)).ToHashSet();
        }
        return _powers.Contains(c);
    }

    /// <summary>
    /// When the leader of the beaten side makes peace: does the war go on for that side's independent allies (with the define separate_peace, 1)?
    /// Then only the leader (and its subjects) leave, and the strongest ally takes over the side; otherwise the whole war ends.
    /// </summary>
    public virtual bool SeparatePeace(Game game, War war, Country loser)
        => game.Data.Defines.Get("separate_peace", 0) > 0
           && war.SideOf(loser).Any(x => x != loser && x.Exists && game.World.Relations.OverlordOf(x) != loser && game.World.Relations.OverlordOf(x) == null);

    /// <summary>
    /// The strengths a demand is weighed by: each side's own strength plus its allies' and guarantors' (60%, 50%) - counted only as far as they would
    /// really come (AiProfileRules.JoinFactor: friends of a country that have no quarrel with the other side stay home).
    /// </summary>
    public virtual (double Actor, double Recipient) ContestStrength(Game game, Country actor, Country recipient)
    {
        var R = game.World.Relations;
        double Reliable(Country ally, Country enemy, Country caller, bool asAttacker) => game.AiProfiles.Enabled ? game.Rules.AiProfile.JoinFactor(game, ally, enemy, caller, asAttacker) : 1;
        double sa = game.Diplomacy.Strength(actor) + R.AlliesOf(actor).Where(a => a != recipient).Sum(a => game.Diplomacy.Strength(a) * 0.6 * Reliable(a, recipient, actor, true));
        double sr = game.Diplomacy.Strength(recipient) + R.AlliesOf(recipient).Where(a => a != actor).Sum(a => game.Diplomacy.Strength(a) * 0.6 * Reliable(a, actor, recipient, false))
                    + R.GuarantorsOf(recipient).Where(a => a != actor).Sum(a => game.Diplomacy.Strength(a) * 0.5 * Reliable(a, actor, recipient, false));
        return (sa, sr);
    }

    /// <summary>
    /// May the winner annex the loser outright? With the define annex_great_powers_only (1) only a great power may: Yugoslavia does not swallow Bulgaria,
    /// Paraguay does not swallow Bolivia, the Saudis do not swallow Muscat; they take land, make a puppet or change a government.
    /// </summary>
    public virtual bool AllowsAnnex(Game game, Country winner, Country loser)
        => game.Data.Defines.Get("annex_great_powers_only", 0) <= 0 || IsGreatPower(game, winner);

    /// <summary>
    /// A total war is fought to the end: no peace until the beaten side is occupied. With the define total_wars (1) a war is total when at least
    /// total_war_powers (3) great powers fight in it (a world war), or when the same two leaders fight again and the defender is big
    /// (total_war_states, 30 states: the second Sino-Japanese war, not the first). Once total, always total.
    /// </summary>
    public virtual bool IsTotalWar(Game game, War war)
    {
        if (game.Data.Defines.Get("total_wars", 0) <= 0) return false;
        // (the great powers fighting in this war and in the other wars of its belligerents: the Pacific war and the European war are one world war)
        var linked = war.Attackers.Concat(war.Defenders).SelectMany(c => game.World.WarsOf(c)).Distinct().SelectMany(w => w.Attackers.Concat(w.Defenders)).Distinct().Count(c => IsGreatPower(game, c));
        if (linked >= (int)game.Data.Defines.Get("total_war_powers", 3) && war.Attackers.Any(c => IsGreatPower(game, c)) && war.Defenders.Any(c => IsGreatPower(game, c))) return true;
        return game.Diplomacy.WarCount(war.AttackerLeader, war.DefenderLeader) >= 2 && war.DefenderLeader.OwnedStates.Count >= game.Data.Defines.Get("total_war_states", 30);
    }

    /// <summary>
    /// Does a country capitulate: its capital is in the enemy's hands and the enemy controls capitulation_share (50%) of its land. It stays in the war, its
    /// army gone, and is dealt with when its whole faction makes peace - the war goes on for the allies that are still fighting.
    /// </summary>
    public virtual bool Capitulates(Game game, War war, Country c)
    {
        if (game.Data.Defines.Get("capitulation", 0) <= 0 || c.Capital?.Controller == null) return false;
        // (a country in a total war holds out for total_war_min_months, 30: the second Sino-Japanese war is not over in a year)
        if (war.Vars.HasFlag("total") && (game.World.Date.TotalDays - war.StartDate.TotalDays) / 30 < game.Data.Defines.Get("total_war_min_months", 30)) return false;
        var enemies = war.EnemiesOf(c);
        if (!enemies.Contains(c.Capital.Controller)) return false;
        var locs = c.OwnedStates.SelectMany(s => s.Locations).Where(l => !l.IsWater).ToList();
        if (locs.Count == 0) return true;
        return locs.Count(l => l.Controller != null && enemies.Contains(l.Controller)) >= game.Data.Defines.Get("capitulation_share", 0.5) * locs.Count;
    }

    /// <summary>Does a country answer a call to arms? <paramref name="caller"/> is the war leader asking.</summary>
    public virtual bool WillJoinWar(Game game, Country ally, War war, bool asAttacker, Country caller)
    {
        var enemyLeader = asAttacker ? war.DefenderLeader : war.AttackerLeader;
        if (game.World.Relations.AreAllied(ally, enemyLeader)) return false;
        if (game.World.Relations.HasPact(ally, enemyLeader, game.World.Date)) return false;
        // worn-out countries stay home
        if (game.Diplomacy.WarExhaustion(ally) >= game.Data.Defines.Get("exhausted_refuse_war", 12)) return false;
        double opinion = game.Diplomacy.Opinion(ally, caller);
        // a country with a history joins the wars its history cares about (the enemy is one it has an awakened ambition on) - the rest it leaves alone
        if (game.AiProfiles.Enabled && game.Rng.NextDouble() > game.Rules.AiProfile.JoinFactor(game, ally, enemyLeader, caller, asAttacker)) return false;
        if (!asAttacker) return opinion > -50; // defensive calls are usually honoured
        return opinion > 0 && game.Rng.NextDouble() < 0.75;
    }

    /// <summary>War-score value of a location (capitals are worth more).</summary>
    public virtual double LocationWarValue(Game game, Location l)
    {
        if (game.ScriptValues.TryEval("location_war_value", l, out var scripted)) return scripted;
        double v = 1;
        if (l.Owner != null && l.Owner.Capital == l) v += 4;
        return v;
    }

    /// <summary>Peace cost of one term in war score (0-100).</summary>
    public virtual double PeaceTermCost(Game game, War war, PeaceTerm term, Country winner, Country loser)
    {
        var d = game.Data.Defines;
        double mult = war.CasusBelli?.PeaceCostMultiplier ?? 1;
        switch (term.Type)
        {
            case PeaceTermType.TransferState:
                {
                    if (term.State == null) return 0;
                    var owner = term.State.Owner ?? loser;
                    double total = owner.OwnedStates.Sum(s => s.Locations.Sum(l => LocationWarValue(game, l)));
                    double value = term.State.Locations.Sum(l => LocationWarValue(game, l));
                    if (total <= 0) return 100;
                    // states someone on the winning side fights for (war goals, also allies') are cheaper;
                    // so is land handed to a country of its own people outside the war (as cheap as releasing that people)
                    var receiver = term.Beneficiary ?? winner;
                    double goal = war.Involves(receiver) ? game.Diplomacy.GoalMultiplier(war, term.State, receiver)
                                                         : 0.6 * game.Diplomacy.GoalMultiplier(war, term.State, winner);
                    return Math.Min(100, value / total * 100 * d.PeaceStateCostMultiplier * mult * goal);
                }
            case PeaceTermType.ReleaseCountry:
                {
                    if (term.State == null) return 0;
                    var owner = term.State.Owner ?? loser;
                    var plan = game.Diplomacy.PlanRelease(war, owner, term.Culture, winner, term.Beneficiary ?? winner, term.AsSubject);
                    if (plan.Problem != null) return 100;
                    var freed = plan.AllStates;
                    double total = owner.OwnedStates.Sum(s => s.Locations.Sum(l => LocationWarValue(game, l)));
                    double value = freed.Sum(s => s.Locations.Sum(l => LocationWarValue(game, l)));
                    if (total <= 0) return 100;
                    double goal = game.Diplomacy.GoalMultiplierFor(war, winner, WarGoalType.Liberation);
                    if (term.AsSubject) goal = Math.Min(goal, game.Diplomacy.GoalMultiplierFor(war, winner, WarGoalType.Subjugation));
                    return Math.Min(100, value / total * 100 * d.PeaceStateCostMultiplier * 0.6 * goal);
                }
            case PeaceTermType.Independence: return d.Get("independence_peace_cost", 40) * game.Diplomacy.GoalMultiplierFor(war, term.Beneficiary ?? winner, WarGoalType.Independence);
            case PeaceTermType.RegimeChange: return d.Get("regime_change_cost", 50) * game.Diplomacy.GoalMultiplierFor(war, winner, WarGoalType.Ideology);
            case PeaceTermType.Humiliate: return d.Get("humiliate_cost", 20) * game.Diplomacy.GoalMultiplierFor(war, winner, WarGoalType.Humiliation);
            case PeaceTermType.Demilitarize: return d.Get("demilitarize_cost", 6);
            case PeaceTermType.Subjugate: return d.SubjugateCost * mult * game.Diplomacy.GoalMultiplierFor(war, term.Beneficiary ?? winner, WarGoalType.Subjugation);
            case PeaceTermType.Annex: return d.AnnexCost;
            case PeaceTermType.Gold:
                {
                    double monthly = Math.Max(1, loser.LastIncome);
                    return Math.Min(100, term.Amount / monthly * 2);
                }
            default: return 0;
        }
    }

    /// <summary>
    /// The AI winner sets up subjects (puppets) of the peoples it defeats instead of taking foreign land: by default
    /// countries ruled by an ideology with peace_puppets (democracies).
    /// </summary>
    public virtual bool PrefersPuppets(Game game, Country winner)
        => game.Politics.Enabled && game.Politics.RulingIdeology(winner)?.PeacePuppets == true;

    /// <summary>
    /// A winner that prefers puppets does not always make one: in some wars (regime_change_instead_of_puppet, half of them;
    /// decided once per war) it only puts its own ideology in power in the defeated country and leaves it independent.
    /// </summary>
    public virtual bool PrefersRegimeChange(Game game, War war, Country winner, Country loser)
    {
        if (!game.Politics.Enabled || game.Politics.Of(winner).Ruling == game.Politics.Of(loser).Ruling) return false;
        uint h = unchecked((uint)(war.Id * 7919 + winner.Index * 104729 + loser.Index * 1299709) * 2654435761u);
        return (h >> 8) % 1000 < game.Data.Defines.Get("regime_change_instead_of_puppet", 0.5) * 1000;
    }

    /// <summary>Does an AI winner demand that a state of the loser is demilitarized? By default states next to its own land, when it is wary of the loser (a rival, or a defensive outlook), at most two.</summary>
    public virtual bool WantsDemilitarized(Game game, Country winner, Country loser, State s, int already)
    {
        if (!game.Features.Has(FeatureSet.Demilitarization) || s.Demilitarized || already >= 2 || s == loser.Capital?.State) return false;
        if (!game.Diplomacy.Touches(winner, s)) return false;
        var p = game.AiProfiles.PersonalityOf(winner);
        bool rival = p.Profile != null && p.Profile.Rivals.ContainsKey(loser.Tag);
        return rival || p.Trait("defense") >= 1.2;
    }

    /// <summary>
    /// Does an AI winner free a subject of the losing side in a peace deal? By default winners that prefer puppets
    /// (democracies) do it, and any winner when the subject is of another culture group than its overlord and wants out
    /// (liberty desire of at least liberate_liberty, 50).
    /// </summary>
    public virtual bool PrefersLiberation(Game game, Country winner, Country subject)
    {
        if (PrefersPuppets(game, winner)) return true;
        var pop = game.Population;
        var overlord = game.World.Relations.OverlordOf(subject);
        bool foreign = pop.Enabled && overlord != null && pop.Of(subject).Culture is { } a && pop.Of(overlord).Culture is { } b && pop.Culture(a)?.Group != pop.Culture(b)?.Group;
        return foreign && game.Diplomacy.LibertyDesire(subject) >= game.Data.Defines.Get("liberate_liberty", 50);
    }

    /// <summary>
    /// Does an overlord set a subject free on its own in a month? By default overlords that prefer puppets (democracies),
    /// when the subject's liberty desire is over grant_independence_liberty (60), with a chance of overlord_release_chance (3%) a month.
    /// </summary>
    public virtual bool WillGrantIndependence(Game game, Country overlord, Country subject)
    {
        var d = game.Data.Defines;
        if (!PrefersPuppets(game, overlord) || game.World.WarsOf(overlord).Any()) return false;
        return game.Diplomacy.LibertyDesire(subject) >= d.Get("grant_independence_liberty", 60) && game.Rng.NextDouble() < d.Get("overlord_release_chance", 0.03);
    }

    /// <summary>
    /// Would <paramref name="winner"/> hand land of <paramref name="kin"/>'s own people to it in a peace deal instead of
    /// keeping it or setting up another country of that people? By default its own subjects, and countries it likes
    /// (opinion of at least kin_land_opinion, 25).
    /// </summary>
    public virtual bool GivesLandToKin(Game game, Country winner, Country kin)
        => game.World.Relations.OverlordOf(kin) == winner || game.Diplomacy.Opinion(winner, kin) >= game.Data.Defines.Get("kin_land_opinion", 25);

    /// <summary>
    /// Does an AI country accept a peace offer? War exhaustion, a long war and a stalemate make it give in sooner
    /// (<see cref="PeaceWillingness"/> points of war score).
    /// </summary>
    public virtual bool AiAcceptsPeace(Game game, PeaceOffer offer, double cost)
    {
        var war = offer.War;
        var ai = offer.To;
        var aiScore = war.ScoreFor(ai);
        double willing = PeaceWillingness(game, war, ai);
        if (offer.Terms.All(t => t.Type == PeaceTermType.WhitePeace))
        {
            // in a war of independence a white peace means losing the land: the old ruler only gives up when losing or worn out
            if (war.Vars.HasFlag("independence_war") && war.IsDefender(ai))
                return aiScore < -15 + willing * 0.5;
            return aiScore < 10 + willing * 0.5 || (game.World.Date.TotalDays - war.StartDate.TotalDays) > 365 * 2 && Math.Abs(aiScore) < 25;
        }
        if (offer.FromIsWinner)
            return -aiScore + willing >= cost; // AI is the loser being asked to give things up
        // AI is the winner being offered concessions
        double wanted = Math.Max(0, aiScore);
        return cost >= wanted * 0.9 - 5 - willing;
    }

    /// <summary>War score points a country gives up for peace: 2 per point of war exhaustion, 10 after two years, 10 in a stalemate (no land changed hands for a year).</summary>
    public virtual double PeaceWillingness(Game game, War war, Country c)
    {
        double v = game.Diplomacy.WarExhaustion(c) * 2;
        int days = game.World.Date.TotalDays - war.StartDate.TotalDays;
        if (days > 730) v += 10;
        if (game.World.Date.TotalDays - war.LastChangeDay > 365 && days > 365) v += 10;
        return v;
    }

    /// <summary>
    /// War exhaustion a country gains this month (0-20 scale, EU style): being at war, enemies holding its land,
    /// men lost in battle (relative to its army) and wars where nothing moves for a year. At peace it falls.
    /// </summary>
    public virtual double MonthlyWarExhaustion(Game game, Country c, double menLostThisMonth)
    {
        var d = game.Data.Defines;
        var wars = game.World.WarsOf(c).ToList();
        if (wars.Count == 0) return -d.Get("war_exhaustion_recovery", 0.5);
        double v = d.Get("war_exhaustion_monthly", 0.1);
        int land = 0, held = 0;
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
                if (l.IsLand) { land++; if (l.Controller != null && l.Controller != c && game.World.AtWar(c, l.Controller)) held++; }
        if (land > 0) v += d.Get("war_exhaustion_occupation", 2) * held / land;
        double army = game.World.Divisions.Where(x => x.Owner == c).Sum(x => x.Template.Manpower) + 1000;
        v += d.Get("war_exhaustion_casualties", 3) * Math.Min(2, menLostThisMonth / army);
        if (wars.Any(w => game.World.Date.TotalDays - w.LastChangeDay > 365 && game.World.Date.TotalDays - w.StartDate.TotalDays > 365))
            v += d.Get("war_exhaustion_stalemate", 0.3);
        return v * Math.Max(0, 1 + game.Modifiers.Sum(c, "war_exhaustion_gain"));
    }

    /// <summary>Country modifiers from war exhaustion (0-20): stability, manpower and political power fall.</summary>
    public virtual double WarExhaustionModifier(Game game, Country c, double exhaustion, string key) => key switch
    {
        "stability_target" => -game.Data.Defines.Get("war_exhaustion_stability", 1.5) * exhaustion,
        "manpower" => -0.02 * exhaustion,
        "political_power_gain" => -0.1 * exhaustion,
        _ => 0,
    };

    /// <summary>
    /// Liberty desire of a subject (0-100, EU style). At 50 it may fight for independence (casus belli independence) and
    /// stops joining its overlord's wars. Sources: its strength and size compared to the overlord, its opinion of the overlord,
    /// a different ruling ideology or culture group, the overlord losing a war, the tribute it pays, the subject type's
    /// liberty_desire and the modifier liberty_desire.
    /// </summary>
    public virtual double LibertyDesire(Game game, Country subject, Country overlord, SubjectType type)
    {
        double ld = type.LibertyDesire;
        double ss = game.Diplomacy.Strength(subject), os = game.Diplomacy.Strength(overlord);
        ld += Math.Min(30, 30 * ss / (os + 1));
        ld += Math.Min(15, 15 * subject.OwnedStates.Count / (double)Math.Max(1, overlord.OwnedStates.Count));
        ld -= game.Diplomacy.Opinion(subject, overlord) * 0.2;
        if (game.Politics.Enabled && game.Politics.Of(subject).Ruling != game.Politics.Of(overlord).Ruling) ld += 10;
        var pop = game.Population;
        if (pop.Enabled && pop.Of(subject).Culture is { } a && pop.Of(overlord).Culture is { } b && pop.Culture(a)?.Group != pop.Culture(b)?.Group) ld += 15;
        if (game.World.WarsOf(overlord).Any(w => w.ScoreFor(overlord) < -25)) ld += 15;
        ld += type.IncomeShare * 60;
        ld += game.Modifiers.Sum(subject, "liberty_desire");
        return Math.Clamp(ld, 0, 100);
    }

    /// <summary>Naval power of a country: the number of its ships (0 without the navy feature).</summary>
    public virtual double NavalStrength(Game game, Country c)
        => game.Navy.Enabled ? game.Navy.FleetsOf(c).Sum(f => f.Ships.Count) : 0;

    /// <summary>
    /// How far over sea (km, coast to coast) a country can take coastal land in a peace deal: a short hop for anyone with a
    /// coast (peace_sea_range_km, 400), further only with a navy (peace_sea_range_per_ship, 150 km for every ship).
    /// Without the navy feature every coast is in reach.
    /// </summary>
    public virtual double SeaReachKm(Game game, Country c)
        => !game.Navy.Enabled ? double.MaxValue
            : game.Data.Defines.Get("peace_sea_range_km", 400) + NavalStrength(game, c) * game.Data.Defines.Get("peace_sea_range_per_ship", 150);

    /// <summary>Extra AI acceptance for a diplomatic action (added to the scripted ai_acceptance).</summary>
    public virtual double AcceptanceModifier(Game game, ActionDef action, Country actor, Country recipient)
    {
        // alliances, guarantees and pacts follow who the countries are: friends, common enemies, ideologies (rule AiProfile.Affinity)
        if (game.AiProfiles.Enabled && action.Key is "offer_alliance" or "guarantee_independence" or "offer_non_aggression_pact" or "request_military_access" or "improve_relations")
            return game.Rules.AiProfile.Affinity(game, actor, recipient);
        return 0;
    }

    /// <summary>Rough military power of a country (used by AI and acceptance factors).</summary>
    public virtual double MilitaryStrength(Game game, Country c)
    {
        double s = 0;
        foreach (var d in game.World.Divisions)
            if (d.Owner == c) s += game.Warfare.DivisionPower(d);
        return s;
    }
}
