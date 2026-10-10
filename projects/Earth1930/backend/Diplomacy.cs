// Diplomacy rules: opinion, diplomatic relations, declaring war, calls to arms, war score and peace costs.
// The diplomatic actions themselves are data (common/diplomatic_actions) and stay moddable.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable diplomacy formulas (Imperator-style by default). Subclass and assign to
/// <c>Game.Rules.Diplomacy</c> from a C# mod to change opinions, war rules, call-to-arms or peace costs.
/// Data-driven diplomatic actions live in <c>common/diplomatic_actions</c>.
/// </summary>
public class GameDiplomacyRules : DiplomacyRules
{
    /// <summary>Opinion that comes from the current relationship (added to opinion modifiers).</summary>
    public override double BaseOpinion(Game game, Country from, Country to)
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
    public override int MaxRelations(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("max_diplomatic_relations", c, out var scripted)) return (int)Math.Floor(scripted);
        var d = game.Data.Defines;
        double stat = c.Ruler?.GetStat(d.RelationsStat) ?? 0;
        return d.BaseDiplomaticRelations + (int)Math.Floor(stat * d.RelationsPerStatPoint + game.Modifiers.Sum(c, "relations"));
    }

    /// <summary>Reason why war cannot be declared, or null if allowed.</summary>
    public override string? CannotDeclareWarReason(Game game, Country attacker, Country defender, CasusBelli? cb)
    {
        var w = game.World;
        if (attacker == defender) return "Cannot declare war on yourself";
        if (!attacker.Exists || !defender.Exists) return "Country does not exist";
        if (w.AtWar(attacker, defender)) return "Already at war";
        if (w.Relations.AreAllied(attacker, defender)) return "You are allied";
        if (w.Relations.HasTruce(attacker, defender, w.Date)) return "There is a truce";
        var overlordType = w.Relations.SubjectTypeOf(attacker);
        if (overlordType != null && !overlordType.CanDeclareWar) return "Subjects cannot declare war";
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

    /// <summary>Does a country answer a call to arms? <paramref name="caller"/> is the war leader asking.</summary>
    public override bool WillJoinWar(Game game, Country ally, War war, bool asAttacker, Country caller)
    {
        var enemyLeader = asAttacker ? war.DefenderLeader : war.AttackerLeader;
        if (game.World.Relations.AreAllied(ally, enemyLeader)) return false;
        double opinion = game.Diplomacy.Opinion(ally, caller);
        if (!asAttacker) return opinion > -50; // defensive calls are usually honoured
        return opinion > 0 && game.Rng.NextDouble() < 0.75;
    }

    /// <summary>War-score value of a location (capitals are worth more).</summary>
    public override double LocationWarValue(Game game, Location l)
    {
        if (game.ScriptValues.TryEval("location_war_value", l, out var scripted)) return scripted;
        double v = 1;
        if (l.Owner != null && l.Owner.Capital == l) v += 4;
        return v;
    }

    /// <summary>Peace cost of one term in war score (0-100).</summary>
    public override double PeaceTermCost(Game game, War war, PeaceTerm term, Country winner, Country loser)
    {
        var d = game.Data.Defines;
        double mult = war.CasusBelli?.PeaceCostMultiplier ?? 1;
        switch (term.Type)
        {
            case PeaceTermType.TransferState:
                {
                    if (term.State == null) return 0;
                    double total = loser.OwnedStates.Sum(s => s.Locations.Sum(l => LocationWarValue(game, l)));
                    double value = term.State.Locations.Sum(l => LocationWarValue(game, l));
                    if (total <= 0) return 100;
                    return Math.Min(100, value / total * 100 * d.PeaceStateCostMultiplier * mult);
                }
            case PeaceTermType.Subjugate: return d.SubjugateCost * mult;
            case PeaceTermType.Annex: return d.AnnexCost;
            case PeaceTermType.Gold:
                {
                    double monthly = Math.Max(1, loser.LastIncome);
                    return Math.Min(100, term.Amount / monthly * 2);
                }
            default: return 0;
        }
    }

    /// <summary>Does an AI country accept a peace offer?</summary>
    public override bool AiAcceptsPeace(Game game, PeaceOffer offer, double cost)
    {
        var aiScore = offer.War.ScoreFor(offer.To);
        if (offer.FromIsWinner)
        {
            // AI is the loser being asked to give things up
            if (offer.Terms.All(t => t.Type == PeaceTermType.WhitePeace))
                return aiScore < 10 || (game.World.Date.TotalDays - offer.War.StartDate.TotalDays) > 365 * 2 && Math.Abs(aiScore) < 25;
            return -aiScore >= cost;
        }
        // AI is the winner being offered concessions
        double wanted = Math.Max(0, aiScore);
        return cost >= wanted * 0.9 - 5;
    }

    /// <summary>Extra AI acceptance for a diplomatic action (added to the scripted ai_acceptance).</summary>
    public override double AcceptanceModifier(Game game, ActionDef action, Country actor, Country recipient) => 0;

    /// <summary>Rough military power of a country (used by AI and acceptance factors).</summary>
    public override double MilitaryStrength(Game game, Country c)
    {
        double s = 0;
        foreach (var d in game.World.Divisions)
            if (d.Owner == c) s += game.Warfare.DivisionPower(d);
        return s;
    }
}

/// <summary>Installs the diplomacy rules.</summary>
public sealed class DiplomacySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Diplomacy = new GameDiplomacyRules();
    }
}
