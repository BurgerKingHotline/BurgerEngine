using Burger.Core;
using Burger.Core.Mechanics;
using Burger.Core.Model;
using Burger.Core.Scripting;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>UI for data-driven mechanics: decisions, custom panels, resources, modifiers.</summary>
public sealed partial class Hud
{
    private PanelDef? _customPanel;

    private void OpenCustomPanel(PanelDef p)
    {
        if (Ui.Left == LeftPanel.Custom && _customPanel == p) { CloseLeft(); return; }
        _customPanel = p;
        if (Ui.Left != LeftPanel.Custom) ToggleLeft(LeftPanel.Custom);
    }

    private bool PanelVisible(PanelDef p, Country player)
    {
        var ctx = G.Context(player);
        if (p.PotentialFn != null && !p.PotentialFn(ctx)) return false;
        return G.Script.EvalTrigger(p.Potential, ctx);
    }

    private void HandleMechanicKeys()
    {
        var player = G.World.Player;
        if (player == null) return;
        if (Raylib.IsKeyPressed(KeyboardKey.F6)) ToggleLeft(LeftPanel.Decisions);
        foreach (var p in G.Interface.Panels)
        {
            if (p.Hotkey == null || !Enum.TryParse<KeyboardKey>(p.Hotkey, true, out var key)) continue;
            if (Raylib.IsKeyPressed(key) && PanelVisible(p, player)) OpenCustomPanel(p);
        }
    }

    /// <summary>Resources shown in the top bar.</summary>
    private void DrawResources(ref float x, Country player)
    {
        foreach (var d in G.Resources.Defs)
        {
            if (!d.TopBar || d.Scope != ScopeType.Country) continue;
            double v = G.Resources.Get(player, d);
            double ch = G.Resources.MonthlyChange(player, d);
            string fmt = d.Decimals > 0 ? "F" + d.Decimals : "0";
            string tip = $"{Localisation.Get(d.Name)}: {v.ToString(fmt)}" + (d.Min != null || d.Max != null ? $" (range {d.Min?.ToString() ?? "-"} to {d.Max?.ToString() ?? "-"})" : "") +
                         $"\nMonthly change: {(ch >= 0 ? "+" : "")}{ch:0.##}" + (d.Desc.Length > 0 ? "\n\n" + Localisation.Get(d.Desc) : "");
            Stat(ref x, Localisation.Get(d.Name), v.ToString(fmt), $"{(ch >= 0 ? "+" : "")}{ch:0.#}", tip, ch >= 0 ? Gui.Good : Gui.Bad);
        }
    }

    // ===============================================================================================
    private void DrawDecisionsPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(2000);
        Gui.Panel(r, "Decisions");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 36 * S, w = r.Width - 20 * S;
        var list = G.Decisions.Defs.Values.Where(d => G.Decisions.IsVisible(d, player))
            .OrderByDescending(d => G.Decisions.CanTake(d, player)).ThenBy(d => d.Category).ThenBy(d => d.Name).ToList();
        if (list.Count == 0) { Gui.Label("No decisions available right now.", x, y, 15, Gui.TextDim); return; }
        float rowH = 64 * S;
        float top = Gui.BeginScroll("decisions", new Rectangle(x, y, w, r.Height - 46 * S), list.Count * (rowH + 6 * S));
        float dy = top;
        foreach (var d in list)
        {
            var box = new Rectangle(x, dy, w - 8 * S, rowH);
            bool can = G.Decisions.CanTake(d, player);
            Card(box, can ? new Color(48, 58, 40, 255) : new Color(36, 38, 44, 255), can);
            Gui.Label(Localisation.Get(d.Name), x + 8 * S, dy + 5 * S, 16, can ? Gui.Gold : Gui.Text, FontKind.Bold);
            string desc = Localisation.Get(d.Desc);
            if (desc.Length > 70) desc = desc[..67] + "...";
            Gui.Label(desc, x + 8 * S, dy + 26 * S, 13, Gui.TextDim);
            var reqs = G.Decisions.Requirements(d, player);
            var effects = G.Decisions.DescribeEffect(d, player);
            string tip = Localisation.Get(d.Desc) +
                         (reqs.Count > 0 ? "\n\nRequirements:\n" + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text)) : "") +
                         (effects.Count > 0 ? "\n\nEffects:\n" + string.Join("\n", effects) : "");
            if (Gui.Hover(box)) Gui.Tooltip(tip);
            if (Gui.Button(new Rectangle(x + w - 98 * S, dy + rowH - 28 * S, 86 * S, 24 * S), "Take", can, tip, false, 14))
                Ui.SetStatus(G.Decisions.Take(d, player) ? $"Decision taken: {Localisation.Get(d.Name)}" : "Not possible");
            dy += rowH + 6 * S;
        }
        Gui.EndScroll();
    }

    // ===============================================================================================
    private void DrawCustomPanel()
    {
        var player = G.World.Player;
        var p = _customPanel;
        if (player == null || p == null) { Ui.Left = LeftPanel.None; return; }
        float w = p.Width * S;
        var lr = LeftRect(2000);
        var r = new Rectangle(lr.X, lr.Y, w, lr.Height);
        Gui.Panel(r, p.Title);
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        var ctx = G.Context(player);
        float contentH = MeasureElements(p.Elements, ctx, w - 20 * S);
        float y = Gui.BeginScroll("custom:" + p.Id, new Rectangle(r.X + 10 * S, r.Y + 36 * S, w - 20 * S, r.Height - 44 * S), contentH);
        DrawElements(p.Elements, ctx, r.X + 10 * S, ref y, w - 30 * S, 0);
        Gui.EndScroll();
    }

    private bool ElementVisible(PanelElement e, ScriptContext ctx) => G.Script.EvalTrigger(e.Potential, ctx);

    private float MeasureElements(List<PanelElement> elements, ScriptContext ctx, float w)
    {
        float h = 0;
        foreach (var e in elements)
        {
            if (!ElementVisible(e, ctx)) continue;
            switch (e.Kind)
            {
                case PanelElementKind.Text: h += Gui.WrappedHeight(ElementText(e, ctx), w, 15) + 4 * S; break;
                case PanelElementKind.Header: h += 28 * S; break;
                case PanelElementKind.Separator: h += 12 * S; break;
                case PanelElementKind.Button: h += 32 * S; break;
                case PanelElementKind.Bar: h += 40 * S; break;
                case PanelElementKind.List:
                    foreach (var item in ListItems(e, ctx)) h += MeasureElements(e.Children, ctx.Enter(item), w - 12 * S) + 6 * S;
                    break;
            }
        }
        return h + 10 * S;
    }

    private IEnumerable<IScope> ListItems(PanelElement e, ScriptContext ctx)
        => G.Script.Iterate(e.Iterator, ctx).Where(it => G.Script.EvalTrigger(e.Limit, ctx.Enter(it))).Take(e.MaxRows);

    private string ElementText(PanelElement e, ScriptContext ctx) => e.TextFn != null ? e.TextFn(ctx) : G.Events.FormatText(e.Text, ctx);

    private void DrawElements(List<PanelElement> elements, ScriptContext ctx, float x, ref float y, float w, int depth)
    {
        foreach (var e in elements)
        {
            if (!ElementVisible(e, ctx)) continue;
            switch (e.Kind)
            {
                case PanelElementKind.Text:
                    {
                        string t = ElementText(e, ctx);
                        float h = Gui.Wrapped(t, x, y, w, 15);
                        if (e.Tooltip != null && Gui.Hover(new Rectangle(x, y, w, h))) Gui.Tooltip(G.Events.FormatText(e.Tooltip, ctx));
                        y += h + 4 * S;
                        break;
                    }
                case PanelElementKind.Header:
                    Gui.Section(ElementText(e, ctx), x, y + 4 * S, w);
                    y += 28 * S;
                    break;
                case PanelElementKind.Separator:
                    Raylib.DrawLine((int)x, (int)(y + 6 * S), (int)(x + w), (int)(y + 6 * S), Gui.PanelBorder);
                    y += 12 * S;
                    break;
                case PanelElementKind.Button:
                    {
                        bool enabled = e.EnabledFn != null ? e.EnabledFn(ctx) : G.Script.EvalTrigger(e.Allow, ctx);
                        string tip = e.Tooltip != null ? G.Events.FormatText(e.Tooltip, ctx) : "";
                        var reqs = G.Script.ExplainTrigger(e.Allow, ctx);
                        if (reqs.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text));
                        var fx = G.Script.Describe(e.Effect, ctx);
                        if (fx.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + "Effects:\n" + string.Join("\n", fx);
                        if (Gui.Button(new Rectangle(x, y, w, 28 * S), ElementText(e, ctx), enabled, tip.Length > 0 ? tip : null, false, 15))
                        {
                            if (e.OnClick != null) e.OnClick(ctx);
                            else G.Script.Execute(e.Effect, ctx);
                            G.RaiseMapChanged();
                        }
                        y += 32 * S;
                        break;
                    }
                case PanelElementKind.Bar:
                    {
                        double v = e.ValueFn != null ? e.ValueFn(ctx) : e.Value != null ? G.ScriptValues.Evaluate(e.Value, ctx) : 0;
                        float frac = (float)((e.Max - e.Min) == 0 ? 0 : (v - e.Min) / (e.Max - e.Min));
                        string label = e.Text.Length > 0 ? ElementText(e, ctx) : $"{v:0.#}";
                        Gui.Label(label, x, y, 15);
                        var bar = new Rectangle(x, y + 20 * S, w, 12 * S);
                        Gui.Bar(bar, frac, new Color(e.Color.R, e.Color.G, e.Color.B, (byte)255));
                        if (Gui.Hover(bar)) Gui.Tooltip($"{v:0.##} (from {e.Min:0.##} to {e.Max:0.##})" + (e.Tooltip != null ? "\n" + G.Events.FormatText(e.Tooltip, ctx) : ""));
                        y += 40 * S;
                        break;
                    }
                case PanelElementKind.List:
                    foreach (var item in ListItems(e, ctx))
                    {
                        var sub = ctx.Enter(item);
                        float start = y;
                        DrawElements(e.Children, sub, x + 12 * S, ref y, w - 12 * S, depth + 1);
                        Raylib.DrawLine((int)(x + 4 * S), (int)start, (int)(x + 4 * S), (int)y, new Color(90, 84, 70, 255));
                        y += 6 * S;
                    }
                    break;
            }
        }
    }

    /// <summary>Lists the active modifiers of a scope (for location/country/character panels). Returns the height used.</summary>
    private float DrawModifierList(IScope scope, float x, float y, float w)
    {
        var mods = G.Modifiers.Of(scope).Where(m => !m.Def.Hidden).ToList();
        if (mods.Count == 0) return 0;
        float start = y;
        Gui.Label("Modifiers", x, y, 15, Gui.Gold, FontKind.Header);
        y += 20 * S;
        float tx = x;
        foreach (var m in mods)
        {
            string name = Localisation.Get(m.Def.Name);
            float tw = Gui.Measure(name, 13).X + 12;
            if (tx + tw > x + w) { tx = x; y += 22 * S; }
            var tr = new Rectangle(tx, y, tw, 19 * S);
            Raylib.DrawRectangleRec(tr, new Color(52, 46, 70, 255));
            Gui.LabelCentered(name, tr, 13);
            if (Gui.Hover(tr))
                Gui.Tooltip($"{name}\n{Localisation.Get(m.Def.Desc)}\n{G.Modifiers.Describe(m.Def)}" + (m.Expires != null ? $"\nUntil {m.Expires.Value.ToDisplay()}" : ""));
            tx += tw + 4;
        }
        return y + 24 * S - start;
    }
}
