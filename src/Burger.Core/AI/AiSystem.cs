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
            if ((day + c.Index) % 2 == 1 && Game.Features.Has(FeatureSet.Navy)) Game.P("Ai.Fleets", () => MoveFleets(c));
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
            if (Game.Features.Has(FeatureSet.Politics)) ManagePolitics(c);
            if (Game.Features.Has(FeatureSet.Population)) ManagePopulation(c);
            if (Game.Features.Has(FeatureSet.Diplomacy))
            {
                Game.P("Ai.Diplomacy", () => Diplomacy(c));
                Game.P("Ai.Peace", () => Peace(c));
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

        double ownPower = Game.Diplomacy.Strength(c);
        var strength = new Dictionary<Country, double>();
        double StrengthOf(Country e) => strength.TryGetValue(e, out var v) ? v : strength[e] = Game.Rules.Diplomacy.MilitaryStrength(Game, e);
        for (int i = 0; i < armies.Count; i++)
        {
            var army = armies[i];
            var enemy = army.FrontEnemy != null && bordering.Contains(army.FrontEnemy) ? army.FrontEnemy : bordering[i % bordering.Count];
            double enemyTotal = StrengthOf(enemy);
            double enemyPower = enemyTotal / Math.Max(1, armies.Count);
            double armyPower = army.Divisions.Sum(Game.Warfare.DivisionPower);
            var mode = ChooseArmyMode(c, wars, enemy, armyPower, enemyPower, ownPower, enemyTotal);
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

    /// <summary>
    /// Offensive or hold: attack when the army is a match for its share of the enemy (0.9) or the country is far ahead; the side that started the war,
    /// or that history set against this enemy (an awake ambition), attacks from armyPower >= aggressor_attack_ratio (0.35) of the enemy's share - an
    /// aggressor that sits on its border loses the war it chose.
    /// </summary>
    protected virtual ArmyMode ChooseArmyMode(Country c, List<War> wars, Country enemy, double armyPower, double enemyPower, double ownPower, double enemyTotal)
    {
        if (armyPower > enemyPower * 0.9 || ownPower > enemyTotal * 1.3) return ArmyMode.Offensive;
        bool aggressor = wars.Any(w => w.IsAttacker(c) && w.EnemiesOf(c).Contains(enemy));
        if (aggressor && ownPower > enemyTotal * Game.Data.Defines.Get("aggressor_attack_ratio", 0.35)) return ArmyMode.Offensive;
        return ArmyMode.Hold;
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
        cap = (int)Math.Round(cap * Math.Clamp(Game.AiProfiles.PersonalityOf(c).Trait("land"), 0.4, 3)); // a country's attitude to its army
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

    /// <summary>Laws, ministers and political movements (politics features).</summary>
    public virtual void ManagePolitics(Country c) => Game.Politics.AiMonthly(c);

    /// <summary>Colonies and buildings for its people (population features).</summary>
    public virtual void ManagePopulation(Country c) => Game.Population.AiMonthly(c);

    // ------------------------------------------------------------------------------------------- diplomacy
    public virtual void Diplomacy(Country c)
    {
        // (a country's history is pursued every month, not only on the months its diplomats are busy)
        if (Game.AiProfiles.Enabled && Game.Rules.AiProfile.Pursuit(Game, c, Game.AiProfiles.PersonalityOf(c)) is { } early) { Game.Diplomacy.Use(early.Action, c, early.Target); return; }
        if (Game.AiProfiles.Enabled && Game.Rules.AiProfile.Courtship(Game, c, Game.AiProfiles.PersonalityOf(c)) is { } court) { Game.Diplomacy.Use(court.Action, c, court.Target); return; }
        if (Game.Rng.NextDouble() > Game.Data.Defines.AiDiplomacyChance) return;
        var options = new List<(ActionDef Action, Country Target, double Weight)>();
        var personality = Game.AiProfiles.PersonalityOf(c);
        var profiles = Game.AiProfiles.Enabled ? Game.Rules.AiProfile : null;
        bool commodities = Game.Commodities.Enabled && Game.Commodities.Shortages(c).Count > 0;
        foreach (var a in Game.Data.DiplomaticActions.Values)
        {
            if (a.AiWillDo == null && profiles == null) continue;
            foreach (var t in Game.World.Countries)
            {
                if (t == c || !t.Exists) continue;
                // cheap checks first: scripted weight, then the personality's reading of it
                double weight = 0;
                bool visible = Game.Diplomacy.IsVisible(a, c, t);
                if (!visible) continue;
                if (a.AiWillDo != null) weight = Game.Script.EvalWeight(a.AiWillDo, Game.Diplomacy.ActionContext(a, c, t), 0);
                if (profiles != null)
                {
                    if (weight > 0) weight *= profiles.ActionFactor(Game, c, a.Key, t, personality);
                    weight += profiles.Motive(Game, c, a.Key, t, personality);
                }
                // what it lacks - oil, steel, rubber - is a reason to want the land that has it, or friends who sell it
                if (commodities && profiles != null) weight += profiles.ResourceMotive(Game, c, a.Key, t, personality);
                if (weight <= 0 || !Game.Diplomacy.CanUse(a, c, t)) continue;
                options.Add((a, t, weight));
            }
        }
        if (options.Count == 0) return;
        double total = options.Sum(o => o.Weight);
        double r = Game.Rng.NextDouble() * total;
        if (Environment.GetEnvironmentVariable("BURGER_AI_LOG") is { } logTag && logTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase) && Game.World.Date.Day == 1)
            Console.WriteLine($"AI {Game.World.Date.ToDisplay()} {c.Tag}: total {total:0.0}, top " + string.Join(", ", options.OrderByDescending(o => o.Weight).Take(5).Select(o => $"{o.Action.Key}>{o.Target.Tag} {o.Weight:0.0}")) +
                              "; wars " + string.Join(", ", options.Where(o => o.Action.Key == "declare_war").OrderByDescending(o => o.Weight).Take(4).Select(o => $"{o.Target.Tag} {o.Weight:0.00}")));
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
    public virtual void Peace(Country c)
    {
        var w = Game.World;
        var D = Game.Diplomacy;
        foreach (var war in w.WarsOf(c).ToList())
        {
            if (war.LeaderOf(c) != c) continue;
            if (war.Vars.HasFlag("civil_war")) continue;
            if (D.IsCapitulated(war, c)) continue; // (a capitulated country has no say: its faction decides)
            var enemy = war.EnemyLeaderOf(c);
            bool total = D.IsTotal(war), enemyDone = war.EnemiesOf(c).All(e => D.IsCapitulated(war, e));
            double exhausted = total ? Game.Data.Defines.Get("total_war_exhaustion", 14) : 0;
            double score = war.ScoreFor(c);
            int months = (w.Date.TotalDays - war.StartDate.TotalDays) / 30;
            if (Game.PendingProposals.Any(p => p.Kind == ProposalKind.Peace && p.War == war)) continue;
            double tired = Game.Rules.Diplomacy.PeaceWillingness(Game, war, c);

            // winning: demand what the war score pays for (a worn-out winner settles for less, sooner)
            if (score >= Math.Max(8, 25 - tired * 0.5) && months >= 2 && (!total || enemyDone))
            {
                var offer = BuildDemands(war, c, enemy, score);
                // (a winner that has taken nothing worth the war yet - only demilitarized zones, a humiliation, gold - fights on while it is not worn out)
                bool substantive = offer.Terms.Any(t => t.Type is PeaceTermType.TransferState or PeaceTermType.Annex or PeaceTermType.Subjugate or PeaceTermType.Independence or PeaceTermType.RegimeChange);
                // (a winner that started the war to take land fights until it holds the loser's capital or half a year has gone)
                bool settled = enemy.Capital?.Controller != null && war.SideOf(c).Contains(enemy.Capital.Controller) || months >= Game.Data.Defines.Get("conquest_war_months", 6);
                if (offer.Terms.Count > 0 && (substantive && settled || months >= Game.Data.Defines.Get("conquest_war_months", 6) + 3 || tired >= 4 && months >= 6)) { D.SendPeaceOffer(offer); continue; }
            }
            // losing badly (or worn out and losing): offer the enemy what it could take anyway
            if (score <= -40 + tired * 0.5 && score < -5 && (!total || D.WarExhaustion(c) >= exhausted))
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
            if (total && D.WarExhaustion(c) < exhausted) continue; // (a total war is not ended by tiredness alone)
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
    /// goals), then other occupied land; never more than the war goals allow (max_demands). Nobody gets land it cannot
    /// reach from its own (landlocked and far away), land of a people goes to a friendly country of that people if there
    /// is one, and no people is set up as a subject twice.
    /// </summary>
    public virtual PeaceOffer BuildDemands(War war, Country winner, Country loser, double score)
    {
        var D = Game.Diplomacy;
        var rules = Game.Rules.Diplomacy;
        var offer = new PeaceOffer { War = war, From = winner, To = loser, FromIsWinner = true };
        bool aim = Game.AiProfiles.Enabled && Game.Rules.AiProfile.IsAwake(Game, winner, loser, Game.AiProfiles.PersonalityOf(winner));
        var winnerSide = war.SideOf(winner).ToHashSet();
        var loserSide = war.SideOf(loser);
        double budget = Math.Min(score, D.MaxDemands(war, winner));
        double used = 0;
        bool Try(PeaceTerm t)
        {
            var (wn, ls) = (winner, t.State?.Owner ?? loser);
            double cost = rules.PeaceTermCost(Game, war, t, wn, ls);
            // (a war fought for an ambition of history takes what it was fought for at half the price in war score)
            if (aim && (t.State == null || t.State.Claims.Contains(winner) || t.State.Cores.Contains(winner) || Game.Rules.Population.HasCulturalTies(Game, winner, t.State))) cost *= Game.Data.Defines.Get("war_aim_cost", 0.5); // (only for land it has a claim, a core or a people on)
            if (used + cost > budget) return false;
            offer.Terms.Add(t);
            if (D.InvalidReason(offer) != null) { offer.Terms.Remove(t); return false; }
            used += cost;
            return true;
        }
        var goals = war.Goals.Where(g => winnerSide.Contains(g.Holder)).ToList();
        // freeing subjects of the losing side that fight with us for independence
        foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Independence))
            if (!offer.Terms.Any(t => t.Type == PeaceTermType.Independence && t.Beneficiary == g.Holder))
                Try(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = g.Holder });
        // annexation when everything is held
        var held = loserSide.SelectMany(l => l.OwnedStates)
            .Select(s => (State: s, Held: s.Locations.Count(l => l.Controller != null && winnerSide.Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count)))
            .Where(x => x.Held >= 0.5).ToList();
        // (a country another power is also at war with is partitioned along the historical lines, not swallowed whole)
        bool partition = Game.World.Wars.Any(w2 => w2 != war && w2.EnemiesOf(loser).Any(e => e != winner && !winnerSide.Contains(e) && loser.OwnedStates.Any(st => st.Cores.Contains(e))));
        if (loser.OwnedStates.Count > 0 && rules.AllowsAnnex(Game, winner, loser) && !partition && held.Count(x => x.State.Owner == loser) == loser.OwnedStates.Count)
        {
            var annex = new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner };
            if (Try(annex)) return offer;
        }
        // regime change for ideological wars
        // (a government is only replaced by a winner that stands in its capital)
        if (Game.Politics.Enabled && loser.Capital?.Controller != null && winnerSide.Contains(loser.Capital.Controller) && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Ideology) && Game.Politics.Of(loser).Ruling != Game.Politics.Of(winner).Ruling)
            Try(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = Game.Politics.Of(winner).Ruling });
        var pop = Game.Population;
        var rel = Game.World.Relations;
        // setting subjects of the losing side free (democracies, and subjects of another people that want out)
        foreach (var sub in loserSide.SelectMany(l => rel.SubjectsOf(l)).Distinct().Where(x => x.Exists && rules.PrefersLiberation(Game, winner, x))
                     .OrderByDescending(x => D.LibertyDesire(x)).Take(2).ToList())
            if (!offer.Terms.Any(t => t.Type == PeaceTermType.Independence && t.Beneficiary == sub))
                Try(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = sub });
        // land of a people goes to a country of that people the winner likes (its own subject first), if it can reach it
        var gained = new Dictionary<Country, HashSet<State>>();
        var kinOf = new Dictionary<State, Country?>();
        Country? Kin(State s)
        {
            if (!kinOf.TryGetValue(s, out var k)) kinOf[s] = k = D.KinFor(war, winner, s);
            return k != null && D.CanReach(k, s, gained.GetValueOrDefault(k)) ? k : null;
        }
        // liberating peoples (those without a friendly country of their own to join)
        if (Game.Population.Enabled && goals.Any(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
            foreach (var g in goals.Where(g => g.CasusBelli.WarGoal == WarGoalType.Liberation))
                foreach (var st in D.GoalStates(g).Where(s => held.Any(h => h.State == s) && !(pop.Enabled && pop.IsColony(s)) && Kin(s) == null).Take(3))
                    if (Game.Population.MajorityCulture(st) is { } mc && !offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc) && MakesNewNation(st, mc, false))
                        Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = st, Culture = mc, Beneficiary = winner });
        // democracies (peace_puppets) set up friendly subjects instead of taking foreign land: the defeated country itself
        // becomes a subject when we hold much of its own people's land, other peoples in the land we hold are released as
        // subjects; we take only land of our own (accepted) peoples, cores and claims
        // a release term is only for a people that gets a country of its own: where the land would go to a country of that
        // people or to the winner, the land terms below say so plainly
        bool MakesNewNation(State st, string culture, bool asSubject)
            => D.ReleaseMakesNewNation(war, st.Owner!, culture, winner, asSubject);
        // (a subject that wins sets up subjects for its overlord: subjects have none of their own)
        bool puppets = rules.PrefersPuppets(Game, winner);
        var patron = D.TopOverlord(winner);
        bool subjugated = false;
        // colonies are taken directly by the winner, never split off or handed on
        bool OursToTake(State s) => !puppets || !pop.Enabled || pop.IsColony(s) || s.Cores.Contains(winner) || s.Claims.Contains(winner)
                                    || pop.MajorityCulture(s) is not { } mc || pop.IsAccepted(winner, mc);
        // sometimes a change of government is enough: the defeated country stays independent, nobody is made a subject
        bool regimeOnly = puppets && loser.Capital?.Controller != null && winnerSide.Contains(loser.Capital.Controller) && rules.PrefersRegimeChange(Game, war, winner, loser)
                          && (offer.Terms.Any(t => t.Type == PeaceTermType.RegimeChange)
                              || Try(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = Game.Politics.Of(winner).Ruling }));
        if (puppets && pop.Enabled && !regimeOnly)
        {
            string loserCulture = pop.Of(loser).Culture ?? "";
            var homeHeld = held.Where(h => h.State.Owner == loser && pop.MajorityCulture(h.State) == loserCulture && !OursToTake(h.State)).ToList();
            int home = loser.OwnedStates.Count(s => pop.MajorityCulture(s) == loserCulture);
            // (never a second subject of one people: their land joins the subject we have)
            if (rel.OverlordOf(loser) == null && homeHeld.Count > 0 && homeHeld.Count * 3 >= Math.Max(1, home) && !D.HoldsHomelandOf(patron, loser)
                && D.SubjectOfCulture(winner, loserCulture) == null)
                subjugated = Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
            if (!subjugated)
                foreach (var (state, _) in held.OrderByDescending(x => x.Held).ThenByDescending(x => x.State.Tax))
                {
                    if (OursToTake(state) || pop.IsColony(state) || pop.MajorityCulture(state) is not { } mc) continue;
                    if (mc == (pop.Of(state.Owner!).Culture ?? "")) continue; // the owner's own people: not split off
                    if (D.SubjectOfCulture(winner, mc) != null || Kin(state) != null) continue; // they have a country to join
                    if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.Culture == mc) || !MakesNewNation(state, mc, true)) continue;
                    if (offer.Terms.Count(t => t.Type == PeaceTermType.ReleaseCountry) >= 3) break;
                    Try(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = state, Culture = mc, Beneficiary = winner, AsSubject = true });
                }
        }
        // land: goal states first (to whoever fights for them), then the rest of the held land. Allies and subjects only get
        // land that touches their own (or land they get in this deal): no far-away pieces for puppets; the rest goes to the
        // winner - if it can reach it (it borders its land or what it takes here, or both have a coast).
        bool Touches(Country x, State s) => D.Touches(x, s, gained.GetValueOrDefault(x));
        Country BeneficiaryOf(State s)
        {
            if (s.Cores.Contains(winner)) return winner; // its own core land is never given away
            foreach (var g in goals)
                if (D.GoalStates(g).Contains(s) && (g.Holder == winner || Touches(g.Holder, s))) return g.Holder;
            if (s.Claims.Contains(winner)) return winner;
            if (pop.Enabled && pop.IsColony(s) && D.CanReach(winner, s, gained.GetValueOrDefault(winner))) return winner; // colonies: taken directly
            if (Kin(s) is { } kin) return kin;
            // (land goes back to its rightful owner on the winning side wherever it lies: Britain does not take the Chinese land Japan holds)
            if (winnerSide.FirstOrDefault(x => x != winner && s.Cores.Contains(x)) is { } rightful) return rightful;
            var claimant = winnerSide.FirstOrDefault(x => x != winner && s.Cores.Contains(x) && Touches(x, s))
                           ?? winnerSide.FirstOrDefault(x => x != winner && s.Claims.Contains(x) && Touches(x, s));
            return claimant ?? winner;
        }
        bool IsGoal(State s) => goals.Any(g => D.GoalStates(g).Contains(s));
        if (!subjugated)
        {
            // enclaves inside our own land first, then what the war is about, colonies, the rest
            var wanted = held.OrderByDescending(x => D.IsEnclaveOf(x.State, winnerSide)).ThenByDescending(x => IsGoal(x.State))
                .ThenByDescending(x => pop.Enabled && pop.IsColony(x.State)).ThenByDescending(x => x.Held).ThenByDescending(x => x.State.Tax).Select(x => x.State).ToList();
            // in rounds: land that cannot be reached yet may be reachable once its neighbour is taken
            for (bool progress = true; progress;)
            {
                progress = false;
                foreach (var state in wanted.ToList())
                {
                    if (offer.Terms.Any(t => t.State == state)) { wanted.Remove(state); continue; }
                    // states released as a people go with that people
                    if (offer.Terms.Any(t => t.Type == PeaceTermType.ReleaseCountry && t.State?.Owner == state.Owner && pop.Enabled && pop.MajorityCulture(state) == t.Culture)) continue;
                    // never take a country's last state through cession - that is annexation
                    if (state.Owner!.OwnedStates.Count - offer.Terms.Count(x => x.Type == PeaceTermType.TransferState && x.State?.Owner == state.Owner) <= 1) continue;
                    var to = BeneficiaryOf(state);
                    if (to == winner && !OursToTake(state)) continue;
                    // (a partition keeps to the historical lines: land another country at war with the loser holds the cores on is left for that country)
                    if (to == winner && !state.Cores.Contains(winner) && Game.World.Wars.Any(w2 => w2 != war && w2.EnemiesOf(loser).Any(e => e != winner && !winnerSide.Contains(e) && state.Cores.Contains(e)))) continue;
                    if (!D.CanReach(to, state, gained.GetValueOrDefault(to))) continue;
                    wanted.Remove(state);
                    if (Try(new PeaceTerm { Type = PeaceTermType.TransferState, State = state, Beneficiary = to }))
                    {
                        if (!gained.TryGetValue(to, out var set)) gained[to] = set = new HashSet<State>();
                        set.Add(state);
                        progress = true;
                    }
                }
            }
        }
        // never cut the loser's land in two: what the cessions would leave without a link to its capital goes in the same deal
        if (!subjugated && loser.Capital?.State is { } loserCapital && loserCapital.Owner == loser)
        {
            var linked = pop.Reachable(loserCapital, loser.OwnedStates);
            var ceded = offer.Terms.Where(t => t.Type == PeaceTermType.TransferState && t.State?.Owner == loser).Select(t => t.State!).ToHashSet();
            if (ceded.Count > 0 && !ceded.Contains(loserCapital))
            {
                var stillHome = pop.Reachable(loserCapital, loser.OwnedStates.Where(s => !ceded.Contains(s)));
                foreach (var cut in linked.Where(s => !ceded.Contains(s) && !stillHome.Contains(s)).OrderBy(s => s.Index).ToList())
                {
                    var to = BeneficiaryOf(cut);
                    if (!D.CanReach(to, cut, gained.GetValueOrDefault(to))) continue;
                    if (Try(new PeaceTerm { Type = PeaceTermType.TransferState, State = cut, Beneficiary = to }))
                    {
                        if (!gained.TryGetValue(to, out var set)) gained[to] = set = new HashSet<State>();
                        set.Add(cut);
                    }
                }
            }
        }
        // a wary winner has states next to its land demilitarized
        {
            int demil = 0;
            foreach (var st in loser.OwnedStates.OrderBy(x => x.Index).ToList())
            {
                if (offer.Terms.Any(t => t.State == st)) continue; // (land that changes hands is not also demilitarized)
                if (rules.WantsDemilitarized(Game, winner, loser, st, demil) && Try(new PeaceTerm { Type = PeaceTermType.Demilitarize, State = st, Beneficiary = winner })) demil++;
            }
        }
        if (offer.Terms.Count == 0 && !regimeOnly && loser.Capital?.Controller != null && winnerSide.Contains(loser.Capital.Controller) && rel.OverlordOf(loser) == null && !D.HoldsHomelandOf(patron, loser)
            && D.SubjectOfCulture(winner, pop.Enabled ? pop.Of(loser).Culture ?? "" : "") == null)
            Try(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = Game.Data.SubjectTypes.Keys.First() });
        if (offer.Terms.Count == 0) Try(new PeaceTerm { Type = PeaceTermType.Humiliate, Beneficiary = winner });
        if (offer.Terms.Count == 0 && loser.Gold > 10)
            Try(new PeaceTerm { Type = PeaceTermType.Gold, Beneficiary = winner, Amount = Math.Min(loser.Gold, Math.Max(1, loser.LastIncome) * score / 4) });
        return offer;
    }
}
