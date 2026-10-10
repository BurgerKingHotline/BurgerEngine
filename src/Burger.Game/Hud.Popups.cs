using Burger.Core;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawEvents()
    {
        var queue = G.Events.PlayerQueue;
        if (queue.Count == 0) return;
        var pe = queue[0];
        float w = 560 * S;
        var pic = Assets.Texture(pe.Def.Picture);
        float picH = pic != null ? 200 * S : 0;
        float descH = Gui.WrappedHeight(pe.Desc, w - 32 * S, 17);
        int nOpt = pe.Def.Options.Count;
        float h = 60 * S + picH + descH + nOpt * 40 * S + 30 * S;
        h = Math.Min(h, Raylib.GetScreenHeight() - TopH - 30 * S);
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, Math.Max(TopH + 10 * S, (Raylib.GetScreenHeight() - h) / 2), w, h);
        Gui.Panel(r, pe.Title + (queue.Count > 1 ? $"   (1/{queue.Count})" : ""));
        float y = r.Y + 36 * S;
        if (pic != null)
        {
            Gui.Image(pic.Value, new Rectangle(r.X + 16 * S, y, w - 32 * S, picH - 8 * S));
            y += picH;
        }
        y += Gui.Wrapped(pe.Desc, r.X + 16 * S, y, w - 32 * S, 17) + 10 * S;
        for (int i = 0; i < nOpt; i++)
        {
            bool avail = G.Events.IsOptionAvailable(pe, i);
            var lines = G.Events.DescribeOption(pe, i);
            string tip = lines.Count == 0 ? "No effects" : string.Join("\n", lines);
            if (Gui.Button(new Rectangle(r.X + 16 * S, y, w - 32 * S, 32 * S), G.Events.OptionName(pe, i), avail, tip, pe.Def.Options[i].Highlight, 16))
            {
                G.Events.ChooseOption(pe, i);
                return;
            }
            y += 38 * S;
        }
        Gui.Label($"{pe.FiredOn.ToDisplay()}", r.X + 16 * S, r.Y + r.Height - 22 * S, 13, Gui.TextDim);
    }

    private void DrawProposals()
    {
        var list = G.PendingProposals;
        if (list.Count == 0 || G.Events.PlayerQueue.Count > 0) return;
        var p = list[0];
        float w = 480 * S;
        float textH = Gui.WrappedHeight(p.Text, w - 32 * S, 17);
        float h = 110 * S + textH;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, p.Title + (list.Count > 1 ? $"   (1/{list.Count})" : ""));
        float y = r.Y + 38 * S;
        Gui.Flag(p.From, new Rectangle(r.X + w - 70 * S, y, 54 * S, 36 * S));
        y += Gui.Wrapped(p.Text, r.X + 16 * S, y, w - 100 * S, 17) + 12 * S;
        string? hint = null;
        if (p.Kind == ProposalKind.Peace && p.Peace != null) hint = $"War score for us: {p.Peace.War.ScoreFor(p.To):0}";
        if (p.Action != null)
        {
            var target = p.TargetCharacter != null ? (IScope)p.TargetCharacter : p.To;
            var effects = G.Script.Describe(p.Action.OnAccept, G.Diplomacy.ActionContext(p.Action, p.From, target));
            if (effects.Count > 0) hint = "If accepted:\n" + string.Join("\n", effects);
        }
        float by = r.Y + h - 44 * S;
        if (Gui.Button(new Rectangle(r.X + 16 * S, by, w / 2 - 24 * S, 32 * S), "Accept", true, hint, false, 17)) { G.Diplomacy.Respond(p, true); return; }
        if (Gui.Button(new Rectangle(r.X + w / 2 + 8 * S, by, w / 2 - 24 * S, 32 * S), "Decline", true, null, false, 17)) G.Diplomacy.Respond(p, false);
    }
}
