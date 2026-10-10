using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawWarsPanel()
    {
        var player = G.World.Player;
        var r = RightRect(2000);
        Gui.Panel(r, "Wars");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.None; return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        var wars = G.World.Wars.OrderByDescending(war => player != null && war.Involves(player)).ToList();
        if (wars.Count == 0) Gui.Label("The world is at peace.", x, y, 16, Gui.TextDim);
        float cardH = 200 * S;
        float top = Gui.BeginScroll("wars", new Rectangle(x, y, w, r.Height - 46 * S), wars.Count * (cardH + 8 * S));
        float wy = top;
        foreach (var war in wars)
        {
            bool mine = player != null && war.Involves(player);
            var box = new Rectangle(x, wy, w - 8 * S, cardH);
            Card(box, mine ? new Color(64, 36, 30, 255) : new Color(36, 38, 44, 255), mine);
            Gui.Label(Gui.Fit(war.Name, w - 20 * S, 16, FontKind.Bold), x + 6, wy + 4, 16, Gui.Gold, FontKind.Bold);
            Gui.Label($"Since {war.StartDate.ToDisplay()}", x + 6, wy + 24 * S, 13, Gui.TextDim);
            Gui.Label(Gui.Fit("Attackers: " + string.Join(", ", war.Attackers.Select(c => c.Tag)), w - 20 * S, 14), x + 6, wy + 42 * S, 14);
            Gui.Label(Gui.Fit("Defenders: " + string.Join(", ", war.Defenders.Select(c => c.Tag)), w - 20 * S, 14), x + 6, wy + 60 * S, 14);
            // war goals (the attacker's, then allies' own goals)
            string goals = war.Goals.Count == 0 ? "No war goal" : string.Join("; ", war.Goals.Select(g => $"{g.Holder.Tag}: {Localisation.Get(g.CasusBelli.Name)}"));
            var gr = new Rectangle(x + 6, wy + 78 * S, w - 24 * S, 18 * S);
            Gui.Label(Gui.Fit("Goals: " + goals, gr.Width, 13), gr.X, gr.Y, 13, Gui.Text);
            if (Gui.Hover(gr))
                Gui.Tooltip(string.Join("\n", war.Goals.Select(g => $"{g.Holder.Name} - {Localisation.Get(g.CasusBelli.Name)} against {g.Target.Name}: " +
                    $"{G.Diplomacy.GoalStates(g).Count} goal states, matching demands cost {g.CasusBelli.GoalCostMultiplier:P0}" +
                    (g.CasusBelli.TickingWarScore ? ", holding them adds war score" : ""))) + (war.Goals.Count > 0 ? "\n\nAllies that join add their own war goals." : ""));
            // war score bar: center = 0
            var bar = new Rectangle(x + 6, wy + 100 * S, w - 24 * S, 14 * S);
            Raylib.DrawRectangleRec(bar, new Color(20, 20, 20, 255));
            float score = (float)war.WarScore / 100f;
            float mid = bar.X + bar.Width / 2;
            var fill = score >= 0 ? new Rectangle(mid, bar.Y, bar.Width / 2 * score, bar.Height) : new Rectangle(mid + bar.Width / 2 * score, bar.Y, -bar.Width / 2 * score, bar.Height);
            Raylib.DrawRectangleRec(fill, score >= 0 ? C(war.AttackerLeader.Color) : C(war.DefenderLeader.Color));
            Raylib.DrawLine((int)mid, (int)bar.Y - 2, (int)mid, (int)(bar.Y + bar.Height + 2), Color.White);
            string ws = mine ? $"War score for us: {war.ScoreFor(player!):0}" : $"War score (attacker): {war.WarScore:0}";
            Gui.Label(ws, x + 6, wy + 116 * S, 14, mine && war.ScoreFor(player!) < 0 ? Gui.Bad : Gui.Text);
            if (Gui.Hover(bar)) Gui.Tooltip($"Occupation, battles and holding the war goal decide the war score.\nBattle score (attacker): {war.BattleScore:0.0} (capped at {G.Data.Defines.WarScoreBattleCap:0})\nWar goal held (attacker): {war.TickingScore:+0;-0;0}");
            // exhaustion of the two leaders and stalemate
            int still = (G.World.Date.TotalDays - war.LastChangeDay) / 30;
            string ex = $"Exhaustion: {war.AttackerLeader.Tag} {G.Diplomacy.WarExhaustion(war.AttackerLeader):0.0}  {war.DefenderLeader.Tag} {G.Diplomacy.WarExhaustion(war.DefenderLeader):0.0}" +
                        (still >= 6 ? $"   front still for {still} months" : "");
            var er = new Rectangle(x + 6, wy + 136 * S, w - 24 * S, 18 * S);
            Gui.Label(Gui.Fit(ex, er.Width, 13), er.X, er.Y, 13, still >= 12 ? Gui.Bad : Gui.TextDim);
            if (Gui.Hover(er)) Gui.Tooltip("War exhaustion (0-20) grows while at war, with enemy occupation, losses in battle and a front that does not move.\nIt lowers stability, manpower and political power, and makes countries accept peace sooner.");
            if (mine)
            {
                var enemyLeader = war.EnemyLeaderOf(player!);
                if (Gui.Button(new Rectangle(x + 6, wy + 160 * S, 160 * S, 24 * S), "Negotiate peace", !war.Vars.HasFlag("civil_war"),
                        war.Vars.HasFlag("civil_war") ? "A civil war ends only when one side is defeated" : $"Send a peace offer to {enemyLeader.Name}", false, 14))
                    OpenPeace(war, enemyLeader);
            }
            wy += cardH + 8 * S;
        }
        Gui.EndScroll();
    }

    public void OpenPeaceFromOutside(War war, Country enemy) => OpenPeace(war, enemy);

    private void OpenPeace(War war, Country enemy)
    {
        var player = G.World.Player!;
        Ui.Peace = new PeaceDraft { War = war, Enemy = enemy, Concede = war.ScoreFor(player) < 0 };
        Ui.Right = RightPanel.Peace;
        Map.Mode = MapMode.Political;
        Map.Dirty = true;
        Ui.SetStatus("Click enemy states on the map (or in the list) to demand them");
    }

    private void TogglePeaceState(State s)
    {
        var p = Ui.Peace;
        if (p == null) return;
        var loserSide = p.Concede ? p.War.SideOf(G.World.Player!) : p.War.SideOf(p.Enemy);
        if (s.Owner == null || !loserSide.Contains(s.Owner)) { Ui.SetStatus($"{s.DisplayName} is not owned by the {(p.Concede ? "our" : "enemy")} side"); return; }
        if (!p.States.Remove(s)) p.States.Add(s);
        Map.HighlightState = s;
        Map.Dirty = true;
    }

    /// <summary>Who on the winning side receives a state: the member whose war goal it is, else a claimant, else the war leader.</summary>
    private Country StateReceiver(War war, State s, Country winner)
    {
        var side = war.SideOf(winner);
        foreach (var g in war.Goals) if (side.Contains(g.Holder) && G.Diplomacy.GoalStates(g).Contains(s)) return g.Holder;
        return side.FirstOrDefault(c => s.Cores.Contains(c)) ?? side.FirstOrDefault(c => s.Claims.Contains(c)) ?? winner;
    }

    private PeaceOffer BuildOffer(PeaceDraft p)
    {
        var player = G.World.Player!;
        var winner = p.Concede ? p.Enemy : player;
        var loser = p.Concede ? player : p.Enemy;
        var offer = new PeaceOffer { War = p.War, From = player, To = p.Enemy, FromIsWinner = !p.Concede };
        if (p.Annex) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner });
        else
        {
            foreach (var s in p.States) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = StateReceiver(p.War, s, winner) });
            if (p.Subjugate) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = G.Data.SubjectTypes.Keys.First() });
            foreach (var cu in p.Release)
            {
                var st = p.War.SideOf(loser).SelectMany(c => c.OwnedStates).FirstOrDefault(s => G.Population.MajorityCulture(s) == cu && s != s.Owner?.Capital?.State);
                if (st != null) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.ReleaseCountry, State = st, Culture = cu, Beneficiary = winner });
            }
        }
        if (p.FreeSubjects)
            foreach (var c in p.War.SideOf(winner).Where(c => G.World.Relations.OverlordOf(c) is { } o && p.War.SideOf(loser).Contains(o)))
                offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = c });
        if (p.RegimeChange && G.Politics.Enabled) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.RegimeChange, Beneficiary = winner, Ideology = G.Politics.Of(winner).Ruling });
        if (p.Humiliate) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Humiliate, Beneficiary = winner });
        if (p.Gold > 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Gold, Amount = p.Gold, Beneficiary = winner });
        if (offer.Terms.Count == 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
        return offer;
    }

    private void DrawPeacePanel()
    {
        var p = Ui.Peace;
        var player = G.World.Player;
        if (p == null || player == null || !G.World.Wars.Contains(p.War)) { Ui.Right = RightPanel.Wars; Ui.Peace = null; return; }
        var r = RightRect(2000);
        Gui.Panel(r, $"Peace with {p.Enemy.Name}");
        if (Gui.CloseButton(r)) { Ui.Right = RightPanel.Wars; Ui.Peace = null; Map.HighlightState = null; Map.Dirty = true; return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        double score = p.War.ScoreFor(player);
        Gui.Label($"War score for us: {score:0}   exhaustion {G.Diplomacy.WarExhaustion(player):0.0} / {G.Diplomacy.WarExhaustion(p.Enemy):0.0}", x, y, 17, score >= 0 ? Gui.Good : Gui.Bad);
        y += 26 * S;
        if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 26 * S), "We demand", true, "Ask the enemy to give things up", !p.Concede, 14)) { p.Concede = false; p.States.Clear(); }
        if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 26 * S), "We concede", true, "Offer to give things up to end the war", p.Concede, 14)) { p.Concede = true; p.States.Clear(); }
        y += 34 * S;

        var loserSide = p.Concede ? p.War.SideOf(player) : p.War.SideOf(p.Enemy);
        var states = loserSide.SelectMany(c => c.OwnedStates).OrderBy(s => s.Owner!.Tag).ThenBy(s => s.DisplayName).ToList();
        var offer = BuildOffer(p);
        var (winner, loser) = G.Diplomacy.WinnerLoser(offer);
        Gui.Label("States (click here or on the map):", x, y, 15, Gui.TextDim);
        y += 22 * S;
        float listH = r.Height * 0.34f;
        float top = Gui.BeginScroll("peace", new Rectangle(x, y, w, listH), states.Count * 26 * S);
        float sy = top;
        foreach (var s in states)
        {
            var receiver = StateReceiver(p.War, s, winner);
            double cost = G.Rules.Diplomacy.PeaceTermCost(G, p.War, new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = receiver }, winner, s.Owner!);
            double held = s.Locations.Count(l => l.Controller != null && p.War.SideOf(winner).Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count);
            bool goal = G.Diplomacy.GoalMultiplier(p.War, s, receiver) < 1;
            string label = $"{(goal ? "* " : "")}{s.DisplayName} ({s.Owner!.Tag})  cost {cost:0}  occupied {held:P0}" + (receiver != winner ? $"  to {receiver.Tag}" : "");
            if (Gui.Button(new Rectangle(x, sy, w - 10 * S, 23 * S), label, !p.Annex, null, p.States.Contains(s), 13)) TogglePeaceState(s);
            sy += 26 * S;
        }
        Gui.EndScroll();
        y += listH + 8 * S;
        if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 26 * S), "Subjugate", !p.Annex, $"{loser.Name} becomes a subject of {winner.Name}", p.Subjugate, 14)) p.Subjugate = !p.Subjugate;
        if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 26 * S), "Annex", true, $"{winner.Name} takes everything from {loser.Name}", p.Annex, 14)) p.Annex = !p.Annex;
        y += 32 * S;
        // the other demands: free subjects, regime change, humiliation, release peoples
        float bw3 = (w - 8) / 3;
        bool subjects = p.War.SideOf(winner).Any(c => G.World.Relations.OverlordOf(c) is { } o && p.War.SideOf(loser).Contains(o));
        if (Gui.Button(new Rectangle(x, y, bw3, 26 * S), "Free subjects", subjects, subjects ? "Subjects of the losing side that fight with the winners become independent" : "No subjects of the losing side fight with the winners", p.FreeSubjects, 13)) p.FreeSubjects = !p.FreeSubjects;
        bool regime = G.Politics.Enabled && G.Politics.Of(loser).Ruling != G.Politics.Of(winner).Ruling;
        if (Gui.Button(new Rectangle(x + bw3 + 4, y, bw3, 26 * S), "Regime change", regime, regime ? $"Put the {G.Politics.Ideology(G.Politics.Of(winner).Ruling)?.Name} party in power in {loser.Name}" : "Same ideology (or no politics)", p.RegimeChange, 13)) p.RegimeChange = !p.RegimeChange;
        if (Gui.Button(new Rectangle(x + 2 * (bw3 + 4), y, bw3, 26 * S), "Humiliate", true, $"{loser.Name} loses stability and political power to {winner.Name}", p.Humiliate, 13)) p.Humiliate = !p.Humiliate;
        y += 32 * S;
        if (G.Population.Enabled && !p.Annex)
        {
            var peoples = p.War.SideOf(loser).SelectMany(c => c.OwnedStates.Where(s => s != c.Capital?.State).Select(s => (c, cu: G.Population.MajorityCulture(s))))
                .Where(t => t.cu != null && !G.Population.IsAccepted(t.c, t.cu!)).Select(t => t.cu!).Distinct().Take(4).ToList();
            if (peoples.Count > 0)
            {
                float bx = x;
                Gui.Label("Release:", x, y + 4 * S, 13, Gui.TextDim);
                bx += 60 * S;
                foreach (var cu in peoples)
                {
                    string name = G.Population.Culture(cu)?.Name ?? cu;
                    float bw = Math.Min((x + w - bx) , Math.Max(70 * S, Gui.Measure(name, 12).X + 14 * S));
                    if (bw < 40 * S) break;
                    bool on = p.Release.Contains(cu);
                    if (Gui.Button(new Rectangle(bx, y, bw, 24 * S), name, true, $"Free the {name} people of {loser.Name} as their own country", on, 12)) { if (!p.Release.Remove(cu)) p.Release.Add(cu); }
                    bx += bw + 4 * S;
                }
                y += 30 * S;
            }
        }
        Gui.Label($"Gold: {p.Gold:0}", x, y + 4, 15);
        if (Gui.Button(new Rectangle(x + 120 * S, y, 50 * S, 24 * S), "-50", p.Gold > 0, null, false, 13)) p.Gold = Math.Max(0, p.Gold - 50);
        if (Gui.Button(new Rectangle(x + 176 * S, y, 50 * S, 24 * S), "+50", true, null, false, 13)) p.Gold += 50;
        y += 34 * S;

        offer = BuildOffer(p);
        double total = G.Diplomacy.OfferCost(offer);
        string? invalid = G.Diplomacy.InvalidReason(offer);
        bool accept = invalid == null && (p.Enemy.IsPlayer || G.Diplomacy.AiWouldAccept(offer));
        Gui.Label($"Total cost: {total:0} war score (war goals allow {G.Diplomacy.MaxDemands(p.War, winner):0})", x, y, 16, total <= Math.Abs(score) || p.Concede ? Gui.Text : Gui.Bad);
        if (Gui.Hover(new Rectangle(x, y, w, 20 * S))) Gui.Tooltip("States marked * are war goals of the winning side (cheaper). War exhaustion and a long or stuck war make the AI accept less.");
        y += 22 * S;
        Gui.Label(invalid ?? (accept ? "They will ACCEPT this offer" : "They will REFUSE this offer"), x, y, 15, invalid == null && accept ? Gui.Good : Gui.Bad);
        y += 28 * S;
        if (Gui.Button(new Rectangle(x, y, w, 32 * S), "Send peace offer", invalid == null, string.Join("\n", offer.Terms), false, 17))
        {
            string result = G.Diplomacy.SendPeaceOffer(offer);
            Ui.SetStatus("Peace offer: " + result);
            if (result == "Accepted") { Ui.Peace = null; Ui.Right = RightPanel.Wars; Map.HighlightState = null; Map.Dirty = true; }
        }
    }
}
