using Burger.Core.Model;
using Burger.Core.Scripting;

namespace Burger.Core.Diplomacy;

/// <summary>Opinions, data-driven diplomatic actions, wars, war score and peace deals.</summary>
public sealed partial class DiplomacySystem
{
    private readonly Game _game;
    private readonly HashSet<Country> _aliveLastCheck = new();

    public DiplomacySystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new Burger.Core.Modding.SaveSection { Name = "war_goals", Write = SaveWarGoals, Read = LoadWarGoals, Reset = ResetWarGoals });
    }

    private Relations R => _game.World.Relations;
    private DiplomacyRules Rules => _game.Rules.Diplomacy;

    // =============================================================================================
    // Opinion
    // =============================================================================================

    /// <summary>Opinion <paramref name="from"/> has of <paramref name="to"/> (-200..200).</summary>
    public double Opinion(Country from, Country to)
    {
        if (from == to) return 200;
        return Math.Clamp(Rules.BaseOpinion(_game, from, to) + R.ModifierSum(from, to) + _game.Politics.IdeologyOpinion(from, to) + _game.Population.ReligionOpinion(from, to)
                          + _game.Population.ColonialOpinion(from, to), -200, 200);
    }

    public void AddOpinion(Country from, Country to, string name, double value, double decayPerMonth = -1, int? durationDays = null)
    {
        if (decayPerMonth < 0) decayPerMonth = _game.Data.Defines.OpinionDecayPerMonth;
        GameDate? expires = durationDays.HasValue ? _game.World.Date.AddDays(durationDays.Value) : null;
        R.AddOpinion(from, to, name, value, decayPerMonth, expires);
    }

    public int RelationsUsed(Country c) => R.RelationsUsed(c);
    public int MaxRelations(Country c) => Rules.MaxRelations(_game, c);

    // =============================================================================================
    // Diplomatic actions / character interactions
    // =============================================================================================

    public ScriptContext ActionContext(ActionDef a, Country actor, IScope recipient)
    {
        var ctx = _game.Context(actor);
        ctx.Scopes["actor"] = actor;
        if (recipient is Character ch)
        {
            ctx.Scopes["target"] = ch;
            ctx.Scopes["recipient"] = ch.Country;
        }
        else
        {
            ctx.Scopes["recipient"] = recipient;
            ctx.Scopes["target"] = recipient;
        }
        ctx.Where = $"{a.File}:{a.Line}";
        return ctx;
    }

    public IEnumerable<ActionDef> ActionsFor(ActionTarget target)
        => (target == ActionTarget.Country ? _game.Data.DiplomaticActions : _game.Data.CharacterInteractions).Values;

    /// <summary>Should the action be listed (potential passes)?</summary>
    public bool IsVisible(ActionDef a, Country actor, IScope recipient)
    {
        if (recipient is Country rc && (rc == actor || !rc.Exists)) return false;
        return _game.Script.EvalTrigger(a.Potential, ActionContext(a, actor, recipient));
    }

    /// <summary>Conditions for using the action, each with whether it is met (for tooltips).</summary>
    public List<(string Text, bool Ok)> Requirements(ActionDef a, Country actor, IScope recipient)
    {
        var ctx = ActionContext(a, actor, recipient);
        var list = _game.Script.ExplainTrigger(a.Allow, ctx);
        if (CostOf(a) > 0) list.Add(($"Costs {CostOf(a):0} gold", actor.Gold >= CostOf(a)));
        int cd = CooldownLeft(a, actor, recipient);
        if (cd > 0) list.Add(($"Available again in {cd} days", false));
        return list;
    }

    /// <summary>Gold an action costs (nothing without the economy feature: there is no gold to pay with).</summary>
    public double CostOf(ActionDef a) => _game.Features.Has(Burger.Core.Modding.FeatureSet.Economy) ? a.CostGold : 0;

    public bool CanUse(ActionDef a, Country actor, IScope recipient)
        => IsVisible(a, actor, recipient) && Requirements(a, actor, recipient).All(r => r.Ok);

    private string CooldownKey(ActionDef a, Country actor, IScope recipient) => $"{actor.Tag}:{a.Key}:{recipient.ScopeKey}";

    public int CooldownLeft(ActionDef a, Country actor, IScope recipient)
    {
        if (a.CooldownDays <= 0) return 0;
        if (!_game.World.ActionCooldowns.TryGetValue(CooldownKey(a, actor, recipient), out int day)) return 0;
        return Math.Max(0, day + a.CooldownDays - _game.World.Date.TotalDays);
    }

    /// <summary>AI acceptance score (&gt; 0 means yes).</summary>
    public double Acceptance(ActionDef a, Country actor, IScope recipient)
    {
        var ctx = ActionContext(a, actor, recipient);
        double v = _game.Script.EvalWeight(a.AiAcceptance, ctx, 0);
        var rc = recipient as Country ?? (recipient as Character)?.Country;
        if (rc != null && a.AiAcceptance != null)
        {
            double of = a.AiAcceptance.GetDouble("opinion_factor", 0);
            if (of != 0) v += of * Opinion(rc, actor);
            double sf = a.AiAcceptance.GetDouble("strength_factor", 0);
            if (sf != 0)
            {
                double sa = Rules.MilitaryStrength(_game, actor), sr = Rules.MilitaryStrength(_game, rc);
                v += sf * Math.Clamp((sa + 1) / (sr + 1) - 1, -3, 3);
            }
            v += Rules.AcceptanceModifier(_game, a, actor, rc);
        }
        return v;
    }

    /// <summary>
    /// Uses an action. Returns a short result text for the UI. Actions that need acceptance are sent to
    /// the player as a proposal or decided by the AI immediately.
    /// </summary>
    public string Use(ActionDef a, Country actor, IScope recipient, bool force = false)
    {
        if (!force && !CanUse(a, actor, recipient)) return "Not possible right now";
        actor.Gold -= CostOf(a);
        if (a.CooldownDays > 0) _game.World.ActionCooldowns[CooldownKey(a, actor, recipient)] = _game.World.Date.TotalDays;
        var ctx = ActionContext(a, actor, recipient);
        var recipientCountry = recipient as Country ?? (recipient as Character)?.Country;
        string name = Localisation.Get(a.Name);

        if (a.RequiresAcceptance && recipientCountry != null && recipientCountry != actor)
        {
            if (recipientCountry.IsPlayer)
            {
                _game.PendingProposals.Add(new Proposal
                {
                    Kind = a.TargetType == ActionTarget.Country ? ProposalKind.DiplomaticAction : ProposalKind.CharacterInteraction,
                    Action = a,
                    From = actor,
                    To = recipientCountry,
                    TargetCharacter = recipient as Character,
                    Date = _game.World.Date,
                    Title = name,
                    Text = $"{actor.Name} proposes: {name}" + (recipient is Character c ? $" ({c.DisplayName})" : ""),
                });
                if (_game.Data.Defines.PauseOnEvent) _game.Clock.Paused = true;
                return "Proposal sent";
            }
            bool accepted = Acceptance(a, actor, recipient) > 0;
            Resolve(a, ctx, accepted);
            if (actor.IsPlayer) _game.Notify($"{recipientCountry.Name} {(accepted ? "accepted" : "declined")}: {name}", accepted ? NotificationKind.Good : NotificationKind.Bad);
            return accepted ? "Accepted" : "Declined";
        }
        Resolve(a, ctx, true);
        if (recipientCountry != null && recipientCountry.IsPlayer && actor != recipientCountry)
            _game.Notify($"{actor.Name}: {name}", NotificationKind.Diplomacy);
        return "Done";
    }

    private void Resolve(ActionDef a, ScriptContext ctx, bool accepted)
    {
        _game.Script.Execute(accepted ? a.OnAccept : a.OnDecline, ctx);
        _game.Mods.Invoke(m => m.OnDiplomaticAction(a, (Country)ctx.Root!, ctx.Named("target"), accepted));
        _game.RaiseMapChanged();
    }

    /// <summary>The player answers a proposal.</summary>
    public void Respond(Proposal p, bool accept)
    {
        if (!_game.PendingProposals.Remove(p)) return;
        switch (p.Kind)
        {
            case ProposalKind.DiplomaticAction:
            case ProposalKind.CharacterInteraction:
                if (p.Action == null) return;
                IScope recipient = p.TargetCharacter != null ? (IScope)p.TargetCharacter : p.To;
                Resolve(p.Action, ActionContext(p.Action, p.From, recipient), accept);
                break;
            case ProposalKind.Peace:
                if (p.Peace == null || !_game.World.Wars.Contains(p.Peace.War)) return;
                if (accept) ApplyPeace(p.Peace);
                else _game.Notify($"You refused the peace offer from {p.From.Name}.", NotificationKind.Info);
                break;
            case ProposalKind.CallToArms:
                if (p.War == null || !_game.World.Wars.Contains(p.War)) return;
                if (accept) JoinWar(p.To, p.War, p.War.IsAttacker(p.From));
                else
                {
                    R.SetAlliance(p.To, p.From, false);
                    AddOpinion(p.From, p.To, "Refused call to arms", -50);
                    _game.Notify($"Alliance with {p.From.Name} broken.", NotificationKind.Bad);
                }
                break;
        }
    }

    // =============================================================================================
    // War
    // =============================================================================================

    public CasusBelli? BestCasusBelli(Country attacker, Country defender)
    {
        CasusBelli? best = null;
        foreach (var cb in _game.Data.CasusBelli.Values.OrderByDescending(c => c.AiWeight))
            if (CanUse(attacker, defender, cb)) { best = cb; break; }
        return best;
    }

    /// <summary>Declares war. Returns the war or null (reason is logged/notified).</summary>
    public War? DeclareWar(Country attacker, Country defender, CasusBelli? cb = null, State? goal = null, bool force = false)
    {
        cb ??= BestCasusBelli(attacker, defender);
        if (!force)
        {
            var reason = Rules.CannotDeclareWarReason(_game, attacker, defender, cb);
            if (reason != null)
            {
                if (attacker.IsPlayer) _game.Notify($"Cannot declare war on {defender.Name}: {reason}", NotificationKind.Bad);
                return null;
            }
        }
        else if (_game.World.AtWar(attacker, defender)) return _game.World.WarBetween(attacker, defender);

        // a subject at war is part of its overlord's war: target the overlord instead
        var w = _game.World;
        var war = new War
        {
            Id = w.NextWarId++,
            AttackerLeader = attacker,
            DefenderLeader = defender,
            CasusBelli = cb,
            Goal = goal,
            StartDate = w.Date,
            Name = $"{attacker.Name} - {defender.Name} War",
        };
        w.Wars.Add(war);
        InvalidateCasusBelli();
        war.LastChangeDay = w.Date.TotalDays;
        if (cb != null) war.Goals.Add(new WarGoal { Holder = attacker, Target = defender, CasusBelli = cb });
        CasusBelliUsed[cb?.Key ?? "none"] = CasusBelliUsed.GetValueOrDefault(cb?.Key ?? "none") + 1;
        AddParticipant(war, attacker, true);
        AddParticipant(war, defender, false);
        // the defender fights for its own claims too
        AddJoinGoal(war, defender);

        foreach (var s in R.SubjectsOf(defender).ToList())
            if (JoinsOverlord(s)) JoinWar(s, war, false);
        foreach (var s in R.SubjectsOf(attacker).ToList())
            if (JoinsOverlord(s)) JoinWar(s, war, true);
        var defOverlord = R.OverlordOf(defender);
        if (defOverlord != null && !war.Involves(defOverlord) && defOverlord != attacker) JoinWar(defOverlord, war, false);

        foreach (var ally in R.AlliesOf(defender).Concat(R.GuarantorsOf(defender)).Distinct().ToList())
            CallToArms(war, defender, ally, false);
        foreach (var ally in R.AlliesOf(attacker).ToList())
            CallToArms(war, attacker, ally, true);

        AddOpinion(defender, attacker, "Declared war on us", -100, 2);
        if (cb != null && cb.OthersOpinion != 0)
            foreach (var c in w.Countries.Where(c => c.Exists && c != attacker && c != defender))
                AddOpinion(c, attacker, "Aggression", cb.OthersOpinion, 1);

        _game.Notify($"{attacker.Name} declared war on {defender.Name}!", NotificationKind.War);
        _game.Mods.Invoke(m => m.OnWarDeclared(war));
        _game.Events.FireOnAction("on_war_declared", attacker, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase)
        {
            ["defender"] = defender,
            ["recipient"] = defender,
            ["war"] = war,
        });
        _game.Events.FireOnAction("on_war_declared_on_us", defender, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase)
        {
            ["attacker"] = attacker,
            ["war"] = war,
        });
        _game.RaiseMapChanged();
        return war;
    }

    private void CallToArms(War war, Country caller, Country ally, bool asAttacker)
    {
        if (war.Involves(ally) || !ally.Exists) return;
        var enemyLeader = asAttacker ? war.DefenderLeader : war.AttackerLeader;
        if (_game.World.AtWar(ally, caller) || ally == enemyLeader) return;
        if (ally.IsPlayer)
        {
            _game.PendingProposals.Add(new Proposal
            {
                Kind = ProposalKind.CallToArms,
                From = caller,
                To = ally,
                War = war,
                Date = _game.World.Date,
                Title = "Call to Arms",
                Text = $"{caller.Name} calls us to join the {war.Name}. Refusing will end our alliance.",
            });
            if (_game.Data.Defines.PauseOnEvent) _game.Clock.Paused = true;
            return;
        }
        if (Rules.WillJoinWar(_game, ally, war, asAttacker, caller)) JoinWar(ally, war, asAttacker);
        else if (R.AreAllied(ally, caller))
        {
            R.SetAlliance(ally, caller, false);
            AddOpinion(caller, ally, "Refused call to arms", -50);
            _game.NotifyPlayer($"{ally.Name} refused the call to arms of {caller.Name}.", NotificationKind.Diplomacy, caller, enemyLeader);
        }
    }

    private void AddParticipant(War war, Country c, bool attacker)
    {
        (attacker ? war.Attackers : war.Defenders).Add(c);
        foreach (var enemy in (attacker ? war.Defenders : war.Attackers))
        {
            R.SetAccess(c, enemy, false);
            R.SetAccess(enemy, c, false);
            R.SetAlliance(c, enemy, false);
        }
    }

    public void JoinWar(Country c, War war, bool attackerSide)
    {
        if (war.Involves(c)) return;
        AddParticipant(war, c, attackerSide);
        AddJoinGoal(war, c);
        _game.NotifyPlayer($"{c.Name} joined the {war.Name}.", NotificationKind.War, war.Attackers.Concat(war.Defenders).ToArray());
        _game.RaiseMapChanged();
    }

    /// <summary>Removes a country from a war (separate peace). Ends the war if a side becomes empty.</summary>
    public void LeaveWar(Country c, War war)
    {
        bool wasAttacker = war.IsAttacker(c);
        var enemies = war.EnemiesOf(c).ToList();
        war.Attackers.Remove(c);
        war.Defenders.Remove(c);
        var until = _game.World.Date.AddYears(_game.Data.Defines.TruceYears);
        foreach (var e in enemies) R.AddTruce(c, e, until);
        InvalidateCasusBelli();
        CleanUpAfterPeace(new[] { c }, enemies);
        if (war.Attackers.Count == 0 || war.Defenders.Count == 0) { EndWar(war); return; }
        if (war.AttackerLeader == c) war.AttackerLeader = war.Attackers[0];
        if (war.DefenderLeader == c) war.DefenderLeader = war.Defenders[0];
        _ = wasAttacker;
    }

    /// <summary>Ends a war completely (truces, occupied land returned).</summary>
    public void EndWar(War war)
    {
        if (!_game.World.Wars.Remove(war)) return;
        InvalidateCasusBelli();
        var until = _game.World.Date.AddYears(_game.Data.Defines.TruceYears);
        foreach (var a in war.Attackers)
            foreach (var d in war.Defenders)
                R.AddTruce(a, d, until);
        CleanUpAfterPeace(war.Attackers, war.Defenders);
        _game.Notify($"The {war.Name} has ended.", NotificationKind.War);
        _game.RaiseMapChanged();
    }

    /// <summary>Returns occupied locations, ends battles and sends trespassing divisions home.</summary>
    private void CleanUpAfterPeace(IEnumerable<Country> sideA, IEnumerable<Country> sideB)
    {
        var a = sideA.ToHashSet();
        var b = sideB.ToHashSet();
        var w = _game.World;
        foreach (var l in w.Locations)
        {
            if (l.Owner == null || l.Controller == null || l.Controller == l.Owner) continue;
            bool pair = (a.Contains(l.Owner) && b.Contains(l.Controller)) || (b.Contains(l.Owner) && a.Contains(l.Controller));
            if (pair && !w.AtWar(l.Owner, l.Controller)) _game.Warfare.SetController(l, l.Owner, silent: true);
        }
        _game.Warfare.EndInvalidBattles();
        _game.Warfare.EvacuateTrespassers();
    }

    /// <summary>Recomputes war score for all wars (called daily).</summary>
    public void UpdateWarScores()
    {
        foreach (var war in _game.World.Wars) war.WarScore = ComputeWarScore(war);
    }

    /// <summary>Peace cost of one term (for the peace window).</summary>
    public double TermCost(War war, PeaceTerm t, Country winner, Country loser) => Rules.PeaceTermCost(_game, war, t, winner, loser);

    public double ComputeWarScore(War war)
    {
        double occAtt = 0, occDef = 0, totAtt = 0, totDef = 0;
        var att = war.Attackers.ToHashSet();
        var def = war.Defenders.ToHashSet();
        foreach (var l in _game.World.Locations)
        {
            var owner = l.Owner;
            if (owner == null || l.IsWater) continue;
            bool ownerAtt = att.Contains(owner), ownerDef = def.Contains(owner);
            if (!ownerAtt && !ownerDef) continue;
            double v = Rules.LocationWarValue(_game, l);
            if (ownerDef)
            {
                totDef += v;
                if (l.Controller != null && att.Contains(l.Controller)) occAtt += v;
            }
            else
            {
                totAtt += v;
                if (l.Controller != null && def.Contains(l.Controller)) occDef += v;
            }
        }
        double score = (totDef > 0 ? 100 * occAtt / totDef : 0) - (totAtt > 0 ? 100 * occDef / totAtt : 0);
        double cap = _game.Data.Defines.WarScoreBattleCap;
        score += Math.Clamp(war.BattleScore, -cap, cap);
        score += war.TickingScore;
        return Math.Clamp(score, -100, 100);
    }

    // =============================================================================================
    // Peace
    // =============================================================================================

    public (Country Winner, Country Loser) WinnerLoser(PeaceOffer o) => o.FromIsWinner ? (o.From, o.To) : (o.To, o.From);

    public double OfferCost(PeaceOffer o)
    {
        var (winner, loser) = WinnerLoser(o);
        return o.Terms.Sum(t => Rules.PeaceTermCost(_game, o.War, t, winner, loser));
    }

    /// <summary>Validates a peace offer; returns a reason if invalid.</summary>
    public string? InvalidReason(PeaceOffer o)
    {
        var war = o.War;
        if (!_game.World.Wars.Contains(war)) return "The war is over";
        if (war.Vars.HasFlag("civil_war")) return "A civil war ends only when one side is defeated";
        if (!war.Involves(o.From) || !war.Involves(o.To) || war.IsAttacker(o.From) == war.IsAttacker(o.To)) return "Not enemies in this war";
        bool fromLeader = war.LeaderOf(o.From) == o.From, toLeader = war.LeaderOf(o.To) == o.To;
        if (!toLeader) return "Peace must be offered to the enemy war leader";
        var (winner, loser) = WinnerLoser(o);
        if (!fromLeader && o.FromIsWinner && o.Terms.Any(t => t.Type != PeaceTermType.WhitePeace))
            return "Only the war leader can make demands";
        foreach (var t in o.Terms)
        {
            switch (t.Type)
            {
                case PeaceTermType.TransferState:
                    if (t.State == null || t.State.Owner == null) return "Invalid state";
                    if (!war.SideOf(loser).Contains(t.State.Owner)) return $"{t.State.DisplayName} is not owned by the losing side";
                    if (t.Beneficiary == null || !war.SideOf(winner).Contains(t.Beneficiary)) return "Invalid receiver";
                    break;
                case PeaceTermType.Subjugate:
                case PeaceTermType.Annex:
                    if (t.Beneficiary == null || !war.SideOf(winner).Contains(t.Beneficiary)) return "Invalid overlord";
                    break;
                case PeaceTermType.Gold:
                    if (t.Amount <= 0) return "Invalid amount";
                    break;
                default:
                    if (InvalidNewTerm(war, t, winner, loser) is { } why) return why;
                    break;
            }
        }
        if (o.FromIsWinner && o.Terms.Any(t => t.Type != PeaceTermType.WhitePeace) && OfferCost(o) > MaxDemands(war, winner) + 0.01)
            return $"Demands are limited to {MaxDemands(war, winner):0} by the war goals";
        if (o.Terms.Count(t => t.Type is PeaceTermType.Annex) > 1) return "Only one annexation";
        return null;
    }

    public bool AiWouldAccept(PeaceOffer o)
    {
        if (InvalidReason(o) != null) return false;
        return Rules.AiAcceptsPeace(_game, o, OfferCost(o));
    }

    /// <summary>Sends a peace offer. The AI answers immediately; the player gets a proposal.</summary>
    public string SendPeaceOffer(PeaceOffer o)
    {
        var reason = InvalidReason(o);
        if (reason != null) return reason;
        if (o.To.IsPlayer)
        {
            string terms = o.Terms.Count == 0 ? "White peace" : string.Join("\n", o.Terms.Select(t => "- " + t));
            _game.PendingProposals.Add(new Proposal
            {
                Kind = ProposalKind.Peace,
                From = o.From,
                To = o.To,
                Peace = o,
                War = o.War,
                Date = _game.World.Date,
                Title = "Peace Offer",
                Text = $"{o.From.Name} offers peace in the {o.War.Name}:\n{terms}",
            });
            if (_game.Data.Defines.PauseOnEvent) _game.Clock.Paused = true;
            return "Offer sent";
        }
        if (AiWouldAccept(o))
        {
            ApplyPeace(o);
            return "Accepted";
        }
        if (o.From.IsPlayer) _game.Notify($"{o.To.Name} refused our peace offer.", NotificationKind.Bad);
        return "Refused";
    }

    /// <summary>Applies a peace deal and ends the war (or removes a separate-peace participant).</summary>
    public void ApplyPeace(PeaceOffer o)
    {
        var war = o.War;
        var (winner, loser) = WinnerLoser(o);
        if (o.Terms.Count == 0) o.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
        PeaceDeals++;
        if (o.Terms.All(t => t.Type == PeaceTermType.WhitePeace)) WhitePeaces++;
        foreach (var t in o.Terms)
        {
            switch (t.Type)
            {
                case PeaceTermType.TransferState:
                    if (t.State != null && t.Beneficiary != null) TransferState(t.State, t.Beneficiary);
                    break;
                case PeaceTermType.Subjugate:
                    if (t.Beneficiary != null)
                    {
                        var type = _game.Data.SubjectTypes.TryGetValue(t.SubjectType, out var st) ? st : _game.Data.SubjectTypes.Values.First();
                        R.SetSubject(loser, t.Beneficiary, type);
                    }
                    break;
                case PeaceTermType.Annex:
                    if (t.Beneficiary != null) Annex(t.Beneficiary, loser);
                    break;
                case PeaceTermType.Gold:
                    {
                        double amount = Math.Min(t.Amount, Math.Max(0, loser.Gold));
                        loser.Gold -= amount;
                        (t.Beneficiary ?? winner).Gold += amount;
                        break;
                    }
                default:
                    ApplyNewTerm(t, winner, loser);
                    break;
            }
        }
        _game.Notify($"Peace: {string.Join(", ", o.Terms)} ({winner.Name} / {loser.Name})", NotificationKind.War);
        _game.Events.FireOnAction("on_peace", winner, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["loser"] = loser, ["war"] = war });
        _game.Mods.Invoke(m => m.OnPeace(o));

        bool leadersPeace = war.LeaderOf(o.From) == o.From && war.LeaderOf(o.To) == o.To;
        if (!_game.World.Wars.Contains(war)) return; // e.g. annexation ended it
        if (leadersPeace) EndWar(war);
        else LeaveWar(war.LeaderOf(o.From) == o.From ? o.To : o.From, war);
        _game.RaiseMapChanged();
    }

    /// <summary>Gives a state to a new owner (control of its locations goes with it).</summary>
    public void TransferState(State s, Country newOwner)
    {
        var old = s.Owner;
        if (old == newOwner) return;
        s.Owner = newOwner;
        s.Claims.Remove(newOwner);
        foreach (var l in s.Locations) _game.Warfare.SetController(l, newOwner, silent: true);
        _game.World.RefreshOwnership();
        if (old != null && old.Capital != null && old.Capital.State == s)
            old.Capital = old.OwnedStates.SelectMany(x => x.Locations).FirstOrDefault(l => l.IsLand);
        newOwner.Capital ??= s.Locations.FirstOrDefault(l => l.IsLand);
        _game.Events.FireOnAction("on_state_transferred", newOwner, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["state"] = s, ["old_owner"] = old });
        _game.Mods.Invoke(m => m.OnStateOwnerChanged(s, old, newOwner));
        _game.RaiseMapChanged();
    }

    /// <summary>Annexes a whole country.</summary>
    public void Annex(Country annexer, Country target)
    {
        foreach (var s in target.OwnedStates.ToList()) TransferState(s, annexer);
        foreach (var ch in _game.World.Characters.Where(c => c.Alive && c.Country == target)) ch.Country = annexer;
        target.Ruler = null;
        target.Heir = null;
        CheckEliminated();
    }

    // =============================================================================================
    // Ticks
    // =============================================================================================

    public void DailyTick()
    {
        UpdateWarScores();
        CheckEliminated();
    }

    public void MonthlyTick()
    {
        R.MonthlyUpdate(_game.World.Date);
        MonthlyWars();
        MonthlyExhaustion();
        MonthlyLiberty();
        _goalStates.Clear();
    }

    /// <summary>Removes countries that lost all their land from wars and relations.</summary>
    public void CheckEliminated()
    {
        var w = _game.World;
        foreach (var c in w.Countries)
        {
            if (c.Exists) { _aliveLastCheck.Add(c); continue; }
            // gone since the last check, or without land but still with forces, wars or a player (e.g. right after loading)
            bool leftovers = c.IsPlayer || w.Wars.Any(x => x.Involves(c)) || w.Divisions.Any(d => d.Owner == c)
                             || _game.Navy.FleetsOf(c).Any() || _game.Air.WingsOf(c).Any();
            if (!_aliveLastCheck.Remove(c) && !leftovers) continue;
            // c just ceased to exist
            foreach (var war in w.Wars.Where(x => x.Involves(c)).ToList()) LeaveWar(c, war);
            R.RemoveCountry(c);
            foreach (var d in w.Divisions.Where(d => d.Owner == c).ToList()) _game.Warfare.DestroyDivision(d);
            foreach (var army in w.Armies.Where(a => a.Owner == c).ToList()) w.Armies.Remove(army);
            foreach (var l in w.Locations.Where(l => l.Controller == c)) l.Controller = l.Owner;
            c.Recruitment.Clear();
            // nothing of it keeps acting: fleets, air wings, lend-lease, factories and building queues go too
            foreach (var f in _game.Navy.FleetsOf(c).ToList()) _game.Navy.DisbandFleet(f);
            foreach (var wing in _game.Air.WingsOf(c).ToList()) _game.Air.Disband(wing);
            _game.Support.Deals.RemoveAll(d => d.From == c || d.To == c);
            c.ProductionLines.Clear();
            c.Construction.Clear();
            _game.Notify($"{c.Name} has ceased to exist.", NotificationKind.War);
            if (c.IsPlayer)
            {
                // the player's country is gone: keep watching as an observer
                _game.SetPlayer(null);
                _game.Notify($"{c.Name} has fallen. You are now an observer.", NotificationKind.War);
            }
            _game.Mods.Invoke(m => m.OnCountryEliminated(c));
            _game.Events.FireOnAction("on_country_eliminated", c);
        }
    }
}
