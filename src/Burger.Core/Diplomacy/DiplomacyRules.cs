using Burger.Core.Model;

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

    /// <summary>Does a country answer a call to arms? <paramref name="caller"/> is the war leader asking.</summary>
    public virtual bool WillJoinWar(Game game, Country ally, War war, bool asAttacker, Country caller)
    {
        var enemyLeader = asAttacker ? war.DefenderLeader : war.AttackerLeader;
        if (game.World.Relations.AreAllied(ally, enemyLeader)) return false;
        if (game.World.Relations.HasPact(ally, enemyLeader, game.World.Date)) return false;
        // worn-out countries stay home
        if (game.Diplomacy.WarExhaustion(ally) >= game.Data.Defines.Get("exhausted_refuse_war", 12)) return false;
        double opinion = game.Diplomacy.Opinion(ally, caller);
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
                    // states someone on the winning side fights for (war goals, also allies') are cheaper
                    double goal = game.Diplomacy.GoalMultiplier(war, term.State, term.Beneficiary ?? winner);
                    return Math.Min(100, value / total * 100 * d.PeaceStateCostMultiplier * mult * goal);
                }
            case PeaceTermType.ReleaseCountry:
                {
                    if (term.State == null) return 0;
                    var owner = term.State.Owner ?? loser;
                    var freed = game.Diplomacy.ReleaseStates(owner, term.Culture);
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
        "stability_target" => -0.5 * exhaustion,
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
        double ss = MilitaryStrength(game, subject), os = MilitaryStrength(game, overlord);
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

    /// <summary>Extra AI acceptance for a diplomatic action (added to the scripted ai_acceptance).</summary>
    public virtual double AcceptanceModifier(Game game, ActionDef action, Country actor, Country recipient) => 0;

    /// <summary>Rough military power of a country (used by AI and acceptance factors).</summary>
    public virtual double MilitaryStrength(Game game, Country c)
    {
        double s = 0;
        foreach (var d in game.World.Divisions)
            if (d.Owner == c) s += game.Warfare.DivisionPower(d);
        return s;
    }
}
