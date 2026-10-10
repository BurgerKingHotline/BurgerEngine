using Burger.Core.Modding;
using Burger.Core.Model;

namespace Burger.Core.AI;

/// <summary>
/// Default AI. Every method is virtual: subclass it in a C# script and assign <c>Game.Ai = new MyAi(Game);</c>
/// to replace any part of it.
/// </summary>
public class AiSystem
{
    protected readonly Game Game;

    public AiSystem(Game game) { Game = game; }

    /// <summary>Set to false to freeze all AI decisions (debugging).</summary>
    public bool Enabled { get; set; } = true;

    public virtual void DailyTick()
    {
        if (!Enabled) return;
        int day = Game.World.Date.TotalDays;
        foreach (var c in Game.World.Countries)
        {
            if (!c.Exists || c.IsPlayer) continue;
            if ((day + c.Index) % 2 == 0 && Game.Features.Has(FeatureSet.Military)) Game.P("Ai.Military", () => ManageMilitary(c));
            if (Game.Features.Has(FeatureSet.Navy)) Game.P("Ai.Fleets", () => MoveFleets(c));
            if ((day + c.Index) % 7 == 0 && Game.Features.Has(FeatureSet.Air)) Game.P("Ai.Air", () => AssignAirWings(c));
        }
    }

    public virtual void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var c in Game.World.Countries.ToList())
        {
            if (!c.Exists || c.IsPlayer) continue;
            if (Game.Features.Has(FeatureSet.Military)) Recruit(c);
            if (Game.Features.Has(FeatureSet.Air)) ManageAir(c);
            if (Game.Features.Has(FeatureSet.LendLease) && Game.Features.Has(FeatureSet.Military)) HelpFriends(c);
            if (Game.Features.Has(FeatureSet.Production)) ManageProduction(c);
            if (Game.Features.Has(FeatureSet.Navy)) ManageNavy(c);
            Game.Decisions.AiMonthly(c);
            if (Game.Features.Has(FeatureSet.Diplomacy))
            {
                Diplomacy(c);
                Peace(c);
            }
        }
    }

    // ------------------------------------------------------------------------------------------- military
    public virtual void ManageMilitary(Country c)
    {
        var w = Game.World;
        var wars = w.WarsOf(c).ToList();
        var divisions = w.DivisionsOf(c).ToList();
        if (divisions.Count == 0) return;

        // every division belongs to an army
        var armies = w.ArmiesOf(c).ToList();
        if (armies.Count == 0) armies.Add(Game.Warfare.CreateArmy(c));
        foreach (var d in divisions.Where(d => d.Army == null))
            Game.Warfare.AssignToArmy(d, armies.OrderBy(a => a.Divisions.Count).First());
        // split big armies so several fronts can be held
        int wanted = Math.Clamp(divisions.Count / 8 + 1, 1, 4);
        while (armies.Count < wanted)
        {
            var biggest = armies.OrderByDescending(a => a.Divisions.Count).First();
            var army = Game.Warfare.CreateArmy(c);
            foreach (var d in biggest.Divisions.Skip(biggest.Divisions.Count / 2).ToList()) Game.Warfare.AssignToArmy(d, army);
            armies.Add(army);
        }
        AssignGenerals(c, armies);

        if (wars.Count == 0)
        {
            foreach (var a in armies.Where(a => a.FrontEnemy != null)) Game.Warfare.SetFront(a, null, ArmyMode.Hold);
            // walk home from foreign land
            foreach (var d in divisions.Where(d => !d.IsMoving && d.Location.Controller != c && !d.InBattle))
            {
                var home = Game.Warfare.NearestOwnLocation(c, d.Location);
                if (home != null) Game.Warfare.OrderMove(d, home, manual: false);
            }
            return;
        }

        // enemies we actually border (directly or through allies' land)
        var enemies = wars.SelectMany(war => war.EnemiesOf(c)).Distinct().Where(e => e.Exists).ToList();
        var bordering = enemies.Where(e => Borders(c, e)).ToList();
        if (bordering.Count == 0) bordering = enemies.Where(e => e.Exists).ToList();
        if (bordering.Count == 0) return;

        double ownPower = Game.Rules.Diplomacy.MilitaryStrength(Game, c);
        for (int i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var enemy = army.FrontEnemy != null && bordering.Contains(army.FrontEnemy) ? army.FrontEnemy : bordering[i % bordering.Count];
            double enemyPower = Game.Rules.Diplomacy.MilitaryStrength(Game, enemy) / Math.Max(1, armies.Count);
            double armyPower = army.Divisions.Sum(Game.Warfare.DivisionPower);
            var mode = armyPower > enemyPower * 0.9 || ownPower > Game.Rules.Diplomacy.MilitaryStrength(Game, enemy) * 1.3 ? ArmyMode.Offensive : ArmyMode.Hold;
            if (army.FrontEnemy != enemy || army.Mode != mode) Game.Warfare.SetFront(army, enemy, mode);
            // battle plans: offensives aim at the enemy capital
            if (Game.Features.Has(FeatureSet.BattlePlans))
                army.Objective = mode == ArmyMode.Offensive && enemy.Capital?.Controller == enemy ? enemy.Capital : null;
        }
    }

    protected virtual bool Borders(Country a, Country b)
    {
        foreach (var l in Game.World.Locations)
        {
            if (l.Controller != a) continue;
            foreach (var adj in l.Neighbors)
                if (adj.To.Controller == b && adj.Kind != AdjacencyKind.Sea) return true;
        }
        return false;
    }

    protected virtual void AssignGenerals(Country c, List<Army> armies)
    {
        var now = Game.World.Date;
        string stat = Game.Data.Defines.GeneralStat;
        foreach (var army in armies.Where(a => a.General == null || !a.General.Alive))
        {
            var used = Game.World.Armies.Where(a => a.General != null).Select(a => a.General!).ToHashSet();
            var candidate = Game.World.CharactersOf(c)
                .Where(ch => !used.Contains(ch) && ch.Age(now) >= Game.Data.Defines.AdultAge && !ch.IsRuler && !ch.Female)
                .OrderByDescending(ch => ch.IsGeneral).ThenByDescending(ch => ch.GetStat(stat)).FirstOrDefault();
            if (candidate == null && !Game.Features.Has(FeatureSet.Characters)) continue; // no characters: armies have no generals
            candidate ??= Game.Characters.Generate(c, false, 35 + Game.Rng.Next(20));
            Game.Warfare.SetGeneral(army, candidate);
        }
    }

    // ------------------------------------------------------------------------------------------- economy
    public virtual void Recruit(Country c)
    {
        var w = Game.World;
        bool atWar = w.WarsOf(c).Any();
        int divisions = w.DivisionsOf(c).Count() + c.Recruitment.Count;
        int cap = (int)(c.OwnedStates.Count * 1.5) + (atWar ? 3 : 1);
        if (Game.Production.Enabled) cap += Game.Production.MilitaryFactories(c) / 3; // industry supports a bigger army
        if (divisions >= cap) return;
        if (c.LastIncome - c.LastExpenses < 0 && !atWar) return;
        var loc = c.Capital != null && c.Capital.Controller == c ? c.Capital : c.OwnedStates.SelectMany(s => s.Locations).FirstOrDefault(l => l.Controller == c && l.IsLand);
        if (loc == null) return;
        var template = Game.Data.Templates.Values
            .Where(t => Game.Rules.Economy.CannotRecruitReason(Game, c, t, loc) == null)
            .OrderByDescending(t => t.Cost).FirstOrDefault();
        if (template == null) return;
        if (Game.Economy.GoldCosts && c.Gold < template.Cost * (atWar ? 1.2 : 2.5)) return;
        if (Game.Production.Enabled && c.Recruitment.Count(o => o.WaitingForEquipment) > 0) return; // wait for the factories
        Game.Economy.Recruit(c, template, loc);
    }

    /// <summary>Builds ships (production lines or gold).</summary>
    public virtual void ManageNavy(Country c) => Game.Navy.AiMonthly(c);

    /// <summary>Fleets: patrol near enemy coasts in war, go home to repair or in peace.</summary>
    public virtual void MoveFleets(Country c) => Game.Navy.AiDaily(c);

    /// <summary>Lend-lease and volunteers for friendly countries at war.</summary>
    public virtual void HelpFriends(Country c) => Game.Support.AiMonthly(c);

    /// <summary>Builds planes (production lines or gold) and new air wings.</summary>
    public virtual void ManageAir(Country c) => Game.Air.AiMonthly(c);

    /// <summary>Air missions (weekly): fighters and close air support where the armies fight, bombers on enemy factories.</summary>
    public virtual void AssignAirWings(Country c) => Game.Air.AiWeekly(c);

    /// <summary>Production lines for the equipment its divisions need, and new factories.</summary>
    public virtual void ManageProduction(Country c) => Game.Production.AiMonthly(c);

    // ------------------------------------------------------------------------------------------- diplomacy
    public virtual void Diplomacy(Country c)
    {
        if (Game.Rng.NextDouble() > Game.Data.Defines.AiDiplomacyChance) return;
        var options = new List<(ActionDef Action, Country Target, double Weight)>();
        foreach (var a in Game.Data.DiplomaticActions.Values)
        {
            if (a.AiWillDo == null) continue;
            foreach (var t in Game.World.Countries)
            {
                if (t == c || !t.Exists) continue;
                if (!Game.Diplomacy.IsVisible(a, c, t) || !Game.Diplomacy.CanUse(a, c, t)) continue;
                double weight = Game.Script.EvalWeight(a.AiWillDo, Game.Diplomacy.ActionContext(a, c, t), 0);
                if (weight > 0) options.Add((a, t, weight));
            }
        }
        if (options.Count == 0) return;
        double total = options.Sum(o => o.Weight);
        double r = Game.Rng.NextDouble() * total;
        foreach (var o in options)
        {
            r -= o.Weight;
            if (r > 0) continue;
            Game.Diplomacy.Use(o.Action, c, o.Target);
            return;
        }
    }

    // ------------------------------------------------------------------------------------------- peace
    public virtual void Peace(Country c)
    {
        var w = Game.World;
        foreach (var war in w.WarsOf(c).ToList())
        {
            if (war.LeaderOf(c) != c) continue;
            var enemy = war.EnemyLeaderOf(c);
            double score = war.ScoreFor(c);
            int months = (w.Date.TotalDays - war.StartDate.TotalDays) / 30;
            if (Game.PendingProposals.Any(p => p.Kind == ProposalKind.Peace && p.War == war)) continue;

            if (score >= 25 && months >= 2)
            {
                var offer = BuildDemands(war, c, enemy, score);
                if (offer.Terms.Count > 0) { Game.Diplomacy.SendPeaceOffer(offer); continue; }
            }
            if (score <= -40)
            {
                // losing badly: offer the enemy what it could take anyway
                var demands = BuildDemands(war, enemy, c, -score);
                var concession = new PeaceOffer { War = war, From = c, To = enemy, FromIsWinner = false };
                concession.Terms.AddRange(demands.Terms);
                if (concession.Terms.Count > 0 && (enemy.IsPlayer || Game.Diplomacy.AiWouldAccept(concession)))
                {
                    Game.Diplomacy.SendPeaceOffer(concession);
                    continue;
                }
            }
            if (score <= -40 || (months >= Game.Data.Defines.WarExhaustionPeaceMonths && Math.Abs(score) < 15))
            {
                var white = new PeaceOffer { War = war, From = c, To = enemy, FromIsWinner = false };
                white.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
                if (enemy.IsPlayer || Game.Diplomacy.AiWouldAccept(white)) Game.Diplomacy.SendPeaceOffer(white);
            }
        }
    }

    /// <summary>Builds the best demands the AI can afford with its war score.</summary>
    public virtual PeaceOffer BuildDemands(War war, Country winner, Country loser, double score)
    {
        var offer = new PeaceOffer { War = war, From = winner, To = loser, FromIsWinner = true };
        var winnerSide = war.SideOf(winner).ToHashSet();
        var loserSide = war.SideOf(loser);
        var candidates = loserSide.SelectMany(l => l.OwnedStates)
            .Select(s => (State: s, Held: s.Locations.Count(l => l.Controller != null && winnerSide.Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count)))
            .Where(x => x.Held >= 0.5)
            .OrderByDescending(x => x.Held).ThenByDescending(x => x.State.Tax).ToList();
        double used = 0;
        bool annexCovered = false;
        if (loser.OwnedStates.Count > 0 && candidates.Count(x => x.State.Owner == loser) == loser.OwnedStates.Count)
        {
            var annex = new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner };
            if (Game.Rules.Diplomacy.PeaceTermCost(Game, war, annex, winner, loser) <= score) { offer.Terms.Add(annex); return offer; }
        }
        foreach (var (state, _) in candidates)
        {
            if (annexCovered) break;
            var t = new PeaceTerm { Type = PeaceTermType.TransferState, State = state, Beneficiary = winner };
            double cost = Game.Rules.Diplomacy.PeaceTermCost(Game, war, t, winner, state.Owner!);
            if (used + cost > score) continue;
            // never take a country's last state through cession - that is annexation
            if (state.Owner!.OwnedStates.Count - offer.Terms.Count(x => x.State?.Owner == state.Owner) <= 1) continue;
            offer.Terms.Add(t);
            used += cost;
        }
        if (offer.Terms.Count == 0 && score >= Game.Data.Defines.SubjugateCost && Game.World.Relations.OverlordOf(loser) == null)
            offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
        if (offer.Terms.Count == 0 && loser.Gold > 10)
            offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Gold, Beneficiary = winner, Amount = Math.Min(loser.Gold, Math.Max(1, loser.LastIncome) * score / 4) });
        return offer;
    }
}
