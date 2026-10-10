using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private bool _showForeignCharacters;

    private void DrawCharactersPanel()
    {
        var player = G.World.Player;
        if (player == null) { Ui.Left = LeftPanel.None; return; }
        var r = LeftRect(2000);
        var country = _showForeignCharacters && Ui.SelectedCountry != null ? Ui.SelectedCountry : player;
        Gui.Panel(r, $"Characters - {country.Name}");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 10 * S, y = r.Y + 34 * S, w = r.Width - 20 * S;
        if (Ui.SelectedCountry != null && Ui.SelectedCountry != player)
        {
            if (Gui.Button(new Rectangle(x, y, w, 24 * S), _showForeignCharacters ? "Show my characters" : $"Show characters of {Ui.SelectedCountry.Name}", true, null, false, 14))
                _showForeignCharacters = !_showForeignCharacters;
            y += 28 * S;
        }
        var now = G.World.Date;
        var chars = G.World.CharactersOf(country)
            .OrderByDescending(c => c.IsRuler).ThenByDescending(c => c.IsHeir).ThenBy(c => c.Family).ThenByDescending(c => c.Age(now)).ToList();
        float listH = r.Height * 0.45f;
        float rowH = 26 * S;
        float top = Gui.BeginScroll("chars", new Rectangle(x, y, w, listH), chars.Count * rowH);
        float ry = top;
        foreach (var c in chars)
        {
            string role = c.IsRuler ? " [Ruler]" : c.IsHeir ? " [Heir]" : G.World.Armies.Any(a => a.General == c) ? " [General]" : "";
            string label = $"{c.DisplayName}, {c.Age(now)}{role}";
            if (Gui.Button(new Rectangle(x, ry, w - 10 * S, rowH - 3 * S), label, true, null, Ui.SelectedCharacter == c, 14))
                Ui.SelectedCharacter = c;
            ry += rowH;
        }
        Gui.EndScroll();
        y += listH + 10 * S;

        var ch = Ui.SelectedCharacter;
        if (ch == null || !ch.Alive) { Gui.Label("Select a character.", x, y, 15, Gui.TextDim); return; }
        Raylib.DrawLine((int)x, (int)y - 4, (int)(x + w), (int)y - 4, Gui.PanelBorder);
        var portrait = Assets.Texture(ch.Portrait);
        float px = x;
        if (portrait != null)
        {
            Gui.Image(portrait.Value, new Rectangle(x, y, 64 * S, 80 * S));
            px += 72 * S;
        }
        Gui.Label(ch.DisplayName, px, y, 18, Gui.Gold, FontKind.Header);
        y += 22 * S;
        Gui.Label($"{(ch.Female ? "Female" : "Male")}, age {ch.Age(now)}, born {ch.BirthDate.ToDisplay()}", px, y, 14, Gui.TextDim);
        y += 20 * S;
        Gui.Label(string.Join("   ", G.Data.Defines.CharacterStats.Select(s => $"{Localisation.GetOrPretty(s)} {ch.GetStat(s):0}")), px, y, 14);
        y += 20 * S;
        Gui.Label($"Loyalty {ch.Loyalty:0}   Health {ch.Health:0}", px, y, 14, Gui.TextDim);
        y += 22 * S;
        if (portrait != null) y = Math.Max(y, r.Y + 0);
        if (ch.Traits.Count > 0)
        {
            float tx = x;
            foreach (var t in ch.Traits)
            {
                string name = Localisation.Get(t.Name);
                float tw = Gui.Measure(name, 14).X + 12;
                if (tx + tw > x + w) { tx = x; y += 24 * S; }
                var tr = new Rectangle(tx, y, tw, 20 * S);
                Raylib.DrawRectangleRec(tr, new Color(60, 52, 40, 255));
                Gui.LabelCentered(name, tr, 14);
                if (Gui.Hover(tr))
                    Gui.Tooltip($"{name}\n{Localisation.Get(t.Description)}\n" + string.Join("\n", t.Modifiers.Select(m => $"{Localisation.GetOrPretty(m.Key)}: {m.Value:+0.##;-0.##}")));
                tx += tw + 4;
            }
            y += 26 * S;
        }
        y += DrawModifierList(ch, x, y, w);
        string family = "";
        if (ch.Spouse != null) family += $"Spouse: {ch.Spouse.DisplayName}{(ch.Spouse.Alive ? "" : " (dead)")}\n";
        if (ch.Father != null) family += $"Father: {ch.Father.DisplayName}\n";
        if (ch.Mother != null) family += $"Mother: {ch.Mother.DisplayName}\n";
        var kids = ch.Children.Where(k => k.Alive).ToList();
        if (kids.Count > 0) family += $"Children: {string.Join(", ", kids.Select(k => k.FirstName))}";
        if (family.Length > 0) y += Gui.Wrapped(family.TrimEnd(), x, y, w, 14, Gui.TextDim) + 4 * S;

        // character interactions (data-driven)
        var actions = G.Diplomacy.ActionsFor(ActionTarget.Character).Where(a => G.Diplomacy.IsVisible(a, player, ch)).ToList();
        if (actions.Count == 0) return;
        Gui.Section("Interactions", x, y, w);
        y += 22 * S;
        foreach (var a in actions)
        {
            if (y > r.Y + r.Height - 30 * S) break;
            var reqs = G.Diplomacy.Requirements(a, player, ch);
            bool can = reqs.All(q => q.Ok);
            string tip = Localisation.Get(a.Desc);
            if (reqs.Count > 0) tip += (tip.Length > 0 ? "\n\n" : "") + string.Join("\n", reqs.Select(q => (q.Ok ? "[OK] " : "[X] ") + q.Text));
            var effects = G.Script.Describe(a.OnAccept, G.Diplomacy.ActionContext(a, player, ch));
            if (effects.Count > 0) tip += "\n\nEffects:\n" + string.Join("\n", effects);
            if (Gui.Button(new Rectangle(x, y, w, 26 * S), Localisation.Get(a.Name), can, tip, false, 15))
                Ui.SetStatus($"{Localisation.Get(a.Name)}: {G.Diplomacy.Use(a, player, ch)}");
            y += 30 * S;
        }
    }
}
