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
        float top = Gui.BeginScroll("wars", new Rectangle(x, y, w, r.Height - 46 * S), wars.Count * 150 * S);
        float wy = top;
        foreach (var war in wars)
        {
            bool mine = player != null && war.Involves(player);
            var box = new Rectangle(x, wy, w - 8 * S, 142 * S);
            Card(box, mine ? new Color(64, 36, 30, 255) : new Color(36, 38, 44, 255), mine);
            Gui.Label(war.Name, x + 6, wy + 4, 16, Gui.Gold, FontKind.Bold);
            Gui.Label($"Since {war.StartDate.ToDisplay()}" + (war.CasusBelli != null ? $" - {Localisation.Get(war.CasusBelli.Name)}" : ""), x + 6, wy + 24 * S, 13, Gui.TextDim);
            Gui.Label("Attackers: " + string.Join(", ", war.Attackers.Select(c => c.Tag)), x + 6, wy + 42 * S, 14);
            Gui.Label("Defenders: " + string.Join(", ", war.Defenders.Select(c => c.Tag)), x + 6, wy + 60 * S, 14);
            // war score bar: center = 0
            var bar = new Rectangle(x + 6, wy + 82 * S, w - 24 * S, 14 * S);
            Raylib.DrawRectangleRec(bar, new Color(20, 20, 20, 255));
            float score = (float)war.WarScore / 100f;
            float mid = bar.X + bar.Width / 2;
            var fill = score >= 0 ? new Rectangle(mid, bar.Y, bar.Width / 2 * score, bar.Height) : new Rectangle(mid + bar.Width / 2 * score, bar.Y, -bar.Width / 2 * score, bar.Height);
            Raylib.DrawRectangleRec(fill, score >= 0 ? C(war.AttackerLeader.Color) : C(war.DefenderLeader.Color));
            Raylib.DrawLine((int)mid, (int)bar.Y - 2, (int)mid, (int)(bar.Y + bar.Height + 2), Color.White);
            string ws = mine ? $"War score for us: {war.ScoreFor(player!):0}" : $"War score (attacker): {war.WarScore:0}";
            Gui.Label(ws, x + 6, wy + 98 * S, 14, mine && war.ScoreFor(player!) < 0 ? Gui.Bad : Gui.Text);
            if (Gui.Hover(bar)) Gui.Tooltip($"Occupation and battles decide the war score.\nBattle score (attacker): {war.BattleScore:0.0} (capped at {G.Data.Defines.WarScoreBattleCap:0})");
            if (mine)
            {
                var enemyLeader = war.EnemyLeaderOf(player!);
                if (Gui.Button(new Rectangle(x + 6, wy + 116 * S, 160 * S, 22 * S), "Negotiate peace", true, $"Send a peace offer to {enemyLeader.Name}", false, 14))
                    OpenPeace(war, enemyLeader);
            }
            wy += 150 * S;
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

    private PeaceOffer BuildOffer(PeaceDraft p)
    {
        var player = G.World.Player!;
        var winner = p.Concede ? p.Enemy : player;
        var loser = p.Concede ? player : p.Enemy;
        var offer = new PeaceOffer { War = p.War, From = player, To = p.Enemy, FromIsWinner = !p.Concede };
        if (p.Annex) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Annex, Beneficiary = winner });
        else
        {
            foreach (var s in p.States) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = winner });
            if (p.Subjugate) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Subjugate, Beneficiary = winner, SubjectType = G.Data.SubjectTypes.Keys.First() });
        }
        if (p.Gold > 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.Gold, Amount = p.Gold, Beneficiary = winner });
        if (offer.Terms.Count == 0) offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.WhitePeace });
        _ = loser;
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
        Gui.Label($"War score for us: {score:0}", x, y, 17, score >= 0 ? Gui.Good : Gui.Bad);
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
        float listH = r.Height * 0.42f;
        float top = Gui.BeginScroll("peace", new Rectangle(x, y, w, listH), states.Count * 26 * S);
        float sy = top;
        foreach (var s in states)
        {
            double cost = G.Rules.Diplomacy.PeaceTermCost(G, p.War, new PeaceTerm { Type = PeaceTermType.TransferState, State = s, Beneficiary = winner }, winner, s.Owner!);
            double held = s.Locations.Count(l => l.Controller != null && p.War.SideOf(winner).Contains(l.Controller)) / (double)Math.Max(1, s.Locations.Count);
            string label = $"{s.DisplayName} ({s.Owner!.Tag})  cost {cost:0}  occupied {held:P0}";
            if (Gui.Button(new Rectangle(x, sy, w - 10 * S, 23 * S), label, !p.Annex, null, p.States.Contains(s), 13)) TogglePeaceState(s);
            sy += 26 * S;
        }
        Gui.EndScroll();
        y += listH + 8 * S;
        if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 26 * S), "Subjugate", !p.Annex, $"{loser.Name} becomes a subject of {winner.Name}", p.Subjugate, 14)) p.Subjugate = !p.Subjugate;
        if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 26 * S), "Annex", true, $"{winner.Name} takes everything from {loser.Name}", p.Annex, 14)) p.Annex = !p.Annex;
        y += 32 * S;
        Gui.Label($"Gold: {p.Gold:0}", x, y + 4, 15);
        if (Gui.Button(new Rectangle(x + 120 * S, y, 50 * S, 24 * S), "-50", p.Gold > 0, null, false, 13)) p.Gold = Math.Max(0, p.Gold - 50);
        if (Gui.Button(new Rectangle(x + 176 * S, y, 50 * S, 24 * S), "+50", true, null, false, 13)) p.Gold += 50;
        y += 34 * S;

        offer = BuildOffer(p);
        double total = G.Diplomacy.OfferCost(offer);
        string? invalid = G.Diplomacy.InvalidReason(offer);
        bool accept = invalid == null && (p.Enemy.IsPlayer || G.Diplomacy.AiWouldAccept(offer));
        Gui.Label($"Total cost: {total:0} war score", x, y, 16, total <= Math.Abs(score) || p.Concede ? Gui.Text : Gui.Bad);
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
