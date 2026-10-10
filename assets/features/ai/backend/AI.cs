// AI: how computer-controlled countries manage armies and fronts, recruit, use diplomacy and make peace.
// Each part only runs when the game has the matching feature (military, diplomacy).
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>How AI countries run their armies, recruit, use diplomacy and make peace.</summary>
public class GameAi : AiSystem
{
    public GameAi(Game game) : base(game) { }

    public override void DailyTick()
    {
        if (!Enabled) return;
        int day = Game.World.Date.TotalDays;
        foreach (var c in Game.World.Countries)
        {
            if (!c.Exists || c.IsPlayer) continue;
            if ((day + c.Index) % 2 == 0 && Game.Features.Has(FeatureSet.Military)) Game.P("Ai.Military", () => ManageMilitary(c));
            if ((day + c.Index) % 2 == 1 && Game.Features.Has(FeatureSet.Navy)) Game.P("Ai.Fleets", () => MoveFleets(c));
            if ((day + c.Index) % 7 == 0 && Game.Features.Has(FeatureSet.Air)) Game.P("Ai.Air", () => AssignAirWings(c));
        }
    }

    public override void MonthlyTick()
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
            if (Game.Features.Has(FeatureSet.Politics)) ManagePolitics(c);
            if (Game.Features.Has(FeatureSet.Population)) ManagePopulation(c);
            if (Game.Features.Has(FeatureSet.Diplomacy))
            {
                Diplomacy(c);
                Peace(c);
            }
        }
    }

    // ------------------------------------------------------------------------------------------- military
    public override void ManageMilitary(Country c)
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
        var strength = new Dictionary<Country, double>();
        double StrengthOf(Country e) => strength.TryGetValue(e, out var v) ? v : strength[e] = Game.Rules.Diplomacy.MilitaryStrength(Game, e);
        for (int i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var enemy = army.FrontEnemy != null && bordering.Contains(army.FrontEnemy) ? army.FrontEnemy : bordering[i % bordering.Count];
            double enemyTotal = StrengthOf(enemy);
            double enemyPower = enemyTotal / Math.Max(1, armies.Count);
            double armyPower = army.Divisions.Sum(Game.Warfare.DivisionPower);
            var mode = armyPower > enemyPower * 0.9 || ownPower > enemyTotal * 1.3 ? ArmyMode.Offensive : ArmyMode.Hold;
            if (army.FrontEnemy != enemy || army.Mode != mode) Game.Warfare.SetFront(army, enemy, mode);
            // battle plans: offensives aim at the enemy capital
            if (Game.Features.Has(FeatureSet.BattlePlans))
                army.Objective = mode == ArmyMode.Offensive && enemy.Capital?.Controller == enemy ? enemy.Capital : null;
        }
    }

    protected override bool Borders(Country a, Country b)
    {
        foreach (var l in Game.World.Locations)
        {
            if (l.Controller != a) continue;
            foreach (var adj in l.Neighbors)
                if (adj.To.Controller == b && adj.Kind != AdjacencyKind.Sea) return true;
        }
        return false;
    }

    protected override void AssignGenerals(Country c, List<Army> armies)
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
    public override void Recruit(Country c)
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
    public override void ManageNavy(Country c) => Game.Navy.AiMonthly(c);

    /// <summary>Fleets: patrol near enemy coasts in war, go home to repair or in peace.</summary>
    public override void MoveFleets(Country c) => Game.Navy.AiDaily(c);

    /// <summary>Lend-lease and volunteers for friendly countries at war.</summary>
    public override void HelpFriends(Country c) => Game.Support.AiMonthly(c);

    /// <summary>Builds planes (production lines or gold) and new air wings.</summary>
    public override void ManageAir(Country c) => Game.Air.AiMonthly(c);

    /// <summary>Air missions (weekly): fighters and close air support where the armies fight, bombers on enemy factories.</summary>
    public override void AssignAirWings(Country c) => Game.Air.AiWeekly(c);

    /// <summary>Production lines for the equipment its divisions need, and new factories.</summary>
    public override void ManageProduction(Country c) => Game.Production.AiMonthly(c);

    /// <summary>Laws, ministers and political movements (politics features).</summary>
    public override void ManagePolitics(Country c) => Game.Politics.AiMonthly(c);

    /// <summary>Colonies and buildings for its people (population features).</summary>
    public override void ManagePopulation(Country c) => Game.Population.AiMonthly(c);

    // ------------------------------------------------------------------------------------------- diplomacy
    public override void Diplomacy(Country c)
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
    /// <summary>
    /// AI peace: a winner makes demands once the war score covers them (sooner when worn out), a loser offers what the
    /// enemy could take anyway, and exhausted countries in a war that goes nowhere look for a white peace.
    /// </summary>
    public override void Peace(Country c)
    {
        var w = Game.World;
        var D = Game.Diplomacy;
        foreach (var war in w.WarsOf(c).ToList())
        {
            if (war.LeaderOf(c) != c) continue;
            if (war.Vars.HasFlag("civil_war")) continue;
            var enemy = war.EnemyLeaderOf(c);
            double score = war.ScoreFor(c);
            int months = (w.Date.TotalDays - war.StartDate.TotalDays) / 30;
            if (Game.PendingProposals.Any(p => p.Kind == ProposalKind.Peace && p.War == war)) continue;
            double tired = Game.Rules.Diplomacy.PeaceWillingness(Game, war, c);

            // winning: demand what the war score pays for (a worn-out winner settles for less, sooner)
            if (score >= Math.Max(8, 25 - tired * 0.5) && months >= 2)
            {
                var offer = BuildDemands(war, c, enemy, score);
                if (offer.Terms.Count > 0) { D.SendPeaceOffer(offer); continue; }
            }
            // losing badly (or worn out and losing): offer the enemy what it could take anyway
            if (score <= -40 + tired * 0.5 && score < -5)
            {
                var demands = BuildDemands(war, enemy, c, -score);
                var concession = new PeaceOffer { War = war, From = c, To = enemy, FromIsWinner = false };
                concession.Terms.AddRange(demands.Terms);
                if (concession.Terms.Count > 0 && (enemy.IsPlayer || D.AiWouldAccept(concession)))
                {
                    D.SendPeaceOffer(concession);
                    continue;
                }
            }
            // a war that goes nowhere, or exhaustion: white peace
            bool stalemate = w.Date.TotalDays - war.LastChangeDay > 365 && months >= 12;
            if (score <= -40 || (months >= Game.Data.Defines.WarExhaustionPeaceMonths && Math.Abs(score) < 15)
                || (stalemate && Math.Abs(score) < 25) || (D.WarExhaustion(c) >= 10 && score < 15))
            {
                var white = new PeaceOffer { War = war, From = c, To = enemy, FromIsWinner = false };
                white.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
                if (enemy.IsPlayer || D.AiWouldAccept(white)) D.SendPeaceOffer(white);
            }
        }
    }

    /// <summary>
    /// Builds the best demands the AI can afford with its war score: first what the war is about (war goal states, freeing
    /// a subject, regime change, liberating a people), each state going to the side member that fights for it (faction war
    /// goals), then other occupied land; never more than the war goals allow (max_demands).
    /// </summary>
    public override PeaceOffer BuildDemands(War war, Country winner, Country loser, double score)
    {
        var D = Game.Diplomacy;
        var rules = Game.Rules.Diplomacy;
        var offer = new PeaceOffer { War = war, From = winner, To = loser, FromIsWinner = true };
        var winnerSide = war.SideOf(winner).ToHashSet();
        var loserSide = war.SideOf(loser);
        double budget = Math.Min(score, D.MaxDemands(war, winner));
        double used = 0;
        bool Try(PeaceTerm t)
        {
            var (wn, ls) = (winner, t.State?.Owner ?? loser);
            double cost = rules.PeaceTermCost(Game, war, t, wn, ls);
            if (used + cost > budget) return false;
            offer.Terms.Add(t);
            if (D.InvalidReason(offer) != null) { offer.Terms.Remove(t); return false; }
            used += cost;
            return true;
        }
        var goals = war.Goals.Where(g => winnerSide.Contains(g.Holder)).ToList();
        // freeing subjects of the losing side that fight with us for independence
        foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Independence))
            Try(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = g.Holder });
        // annexation when everything is held
        var held = loserSide.SelectMany(l => l.OwnedStates)
            .Select(s => (State: s, Held: s.Locations.Count(l => l.Controller != null && winnerSide.Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count)))
            .Where(x => x.Held >= 0.5).ToList();
        if (loser.OwnedStates.Count > 0 && held.Count(x => x.State.Owner == loser) == loser.OwnedStates.Count)
        {
            var annex = new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner };
            if (Try(annex)) return offer;
        }
        // regime change for ideological wars
        if (Game.Politics.Enabled && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Ideology) && Game.Politics.Of(loser).Ruling != Game.Politics.Of(winner).Ruling)
            Try(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = Game.Politics.Of(winner).Ruling });
        // liberating peoples
        if (Game.Population.Enabled && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
            foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
                foreach (var st in D.GoalStates(g).Where(s => held.Any(h => h.State == s)).Take(3))
                    if (Game.Population.MajorityCulture(st) is { } mc && !offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc))
                        Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = st, Culture = mc, Beneficiary = winner });
        // democracies (peace_puppets) set up friendly subjects instead of taking foreign land: the defeated country itself
        // becomes a subject when we hold much of its own people's land, other peoples in the land we hold are released as
        // subjects; we take only land of our own (accepted) peoples, cores and claims
        var pop = Game.Population;
        var rel = Game.World.Relations;
        bool puppets = rules.PrefersPuppets(Game, winner) && rel.OverlordOf(winner) == null; // subjects have no subjects
        bool subjugated = false;
        bool OursToTake(State s) => !puppets || !pop.Enabled || s.Cores.Contains(winner) || s.Claims.Contains(winner)
                                    || pop.MajorityCulture(s) is not { } mc || pop.IsAccepted(winner, mc);
        if (puppets && pop.Enabled)
        {
            string loserCulture = pop.Of(loser).Culture ?? "";
            var homeHeld = held.Where(h => h.State.Owner == loser && pop.MajorityCulture(h.State) == loserCulture && !OursToTake(h.State)).ToList();
            int home = loser.OwnedStates.Count(s => pop.MajorityCulture(s) == loserCulture);
            if (rel.OverlordOf(loser) == null && homeHeld.Count > 0 && homeHeld.Count * 3 >= Math.Max(1, home) && !D.HoldsHomelandOf(winner, loser))
                subjugated = Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
            if (!subjugated)
                foreach (var (state, _) in held.OrderByDescending(x => x.Held).ThenByDescending(x => x.State.Tax))
                {
                    if (OursToTake(state) || pop.MajorityCulture(state) is not { } mc) continue;
                    if (mc == (pop.Of(state.Owner!).Culture ?? "")) continue; // the owner's own people: not split off
                    if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc)) continue;
                    if (offer.Terms.Count(t => t.Type == PeaceTermType.ReleaseCountry) >= 3) break;
                    Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = state, Culture = mc, Beneficiary = winner, AsSubject = true });
                }
        }
        // land: goal states first (to whoever fights for them), then the rest of the held land. Allies and subjects only get
        // land that touches their own (or land they get in this deal): no far-away pieces for puppets; the rest goes to the winner.
        var gained = new Dictionary<Country, HashSet<State>>();
        bool Touches(Country x, State s)
        {
            gained.TryGetValue(x, out var got);
            foreach (var l in s.Locations)
                foreach (var a in l.Neighbors)
                    if (a.Kind != AdjacencyKind.Sea && a.To.IsLand && a.To.State is { } ns && ns != s && (ns.Owner == x || got?.Contains(ns) == true))
                        return true;
            return false;
        }
        Country BeneficiaryOf(State s)
        {
            foreach (var g in goals)
                if (D.GoalStates(g).Contains(s) && (g.Holder == winner || Touches(g.Holder, s))) return g.Holder;
            if (s.Cores.Contains(winner) || s.Claims.Contains(winner)) return winner;
            var claimant = winnerSide.FirstOrDefault(x => x != winner && s.Cores.Contains(x) && Touches(x, s))
                           ?? winnerSide.FirstOrDefault(x => x != winner && s.Claims.Contains(x) && Touches(x, s));
            return claimant ?? winner;
        }
        bool IsGoal(State s) => goals.Any(g => D.GoalStates(g).Contains(s));
        if (!subjugated)
            foreach (var (state, _) in held.OrderByDescending(x => IsGoal(x.State)).ThenByDescending(x => x.Held).ThenByDescending(x => x.State.Tax))
            {
                if (offer.Terms.Any(t => t.State == state)) continue;
                // states released as a people go with that people
                if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.State?.Owner == state.Owner && pop.Enabled && pop.MajorityCulture(state) == t.Culture)) continue;
                // never take a country's last state through cession - that is annexation
                if (state.Owner!.OwnedStates.Count - offer.Terms.Count(x => x.Type == PeaceTermType.TransferState && x.State?.Owner == state.Owner) <= 1) continue;
                var to = BeneficiaryOf(state);
                if (to == winner && !OursToTake(state)) continue;
                if (Try(new PeaceTerm { Type = PeaceTermType.TransferState, State = state, Beneficiary = to }))
                {
                    if (!gained.TryGetValue(to, out var set)) gained[to] = set = new HashSet<State>();
                    set.Add(state);
                }
            }
        if (offer.Terms.Count == 0 && rel.OverlordOf(loser) == null && !D.HoldsHomelandOf(winner, loser))
            Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
        if (offer.Terms.Count == 0) Try(new PeaceTerm { Type = PeaceTermType.Humiliate, Beneficiary = winner });
        if (offer.Terms.Count == 0 && loser.Gold > 10)
            Try(new PeaceTerm { Type = PeaceTermType.Gold, Beneficiary = winner, Amount = Math.Min(loser.Gold, Math.Max(1, loser.LastIncome) * score / 4) });
        return offer;
    }
}

/// <summary>Installs the AI.</summary>
public sealed class AiSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Ai = new GameAi(Game);
    }
}
