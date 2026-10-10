using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Script;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// In-game map editor (F10): move locations between states, create states, set owners and cores,
/// then write the state files back to map/states.
/// </summary>
public sealed partial class Hud
{
    private State? _editorBrush;
    private bool _editorPaint;
    private bool _editorDirty;
    private const string EditorNewStatesFile = Burger.Core.Map.StateWriter.NewStatesFile;

    private void EditorClick(Location? loc)
    {
        Map.Selected = loc;
        Ui.SelectedLocation = loc;
        Map.Dirty = true;
        if (loc == null) return;
        if (_editorPaint && _editorBrush != null && loc.IsLand && loc.State != _editorBrush)
        {
            MoveLocationToState(loc, _editorBrush);
            return;
        }
        if (!_editorPaint && loc.State != null)
        {
            _editorBrush = loc.State;
            Map.HighlightState = _editorBrush;
        }
    }

    private void MoveLocationToState(Location loc, State target)
    {
        var old = loc.State;
        old?.Locations.Remove(loc);
        loc.State = target;
        target.Locations.Add(loc);
        loc.Controller = target.Owner;
        G.World.RefreshOwnership();
        _editorDirty = true;
        G.RaiseMapChanged();
    }

    private void DrawEditor()
    {
        var r = LeftRect(Raylib.GetScreenHeight() - TopH - 20 * S);
        Gui.Panel(r, "Map editor (F10 to leave)");
        if (Gui.CloseButton(r)) { ToggleEditor(); return; }
        float x = r.X + 12 * S, y = r.Y + 36 * S, w = r.Width - 24 * S, lh = 21 * S;
        Gui.Wrapped("Click a location to pick its state as the brush. Turn on Paint, then click locations to move them into the brush state. Save writes map/states.", x, y, w, 14, Gui.TextDim);
        y += 60 * S;

        var loc = Ui.SelectedLocation;
        if (loc != null)
        {
            Gui.Label($"Location: {loc.Key}", x, y, 16, Gui.Gold); y += lh;
            Gui.Label($"Name: {loc.DisplayName}", x, y, 15); y += lh;
            Gui.Label($"Color: {loc.MapColor.ToHex()}   Pixels: {loc.PixelCount}", x, y, 15); y += lh;
            Gui.Label($"Terrain: {loc.Terrain.Key}   State: {loc.State?.Key ?? "none"}", x, y, 15); y += lh;
            Gui.Label($"Neighbors: {loc.Neighbors.Count}", x, y, 15, Gui.TextDim); y += lh + 4 * S;
            if (loc.IsLand && Gui.Button(new Rectangle(x, y, w, 26 * S), "New state from this location", true, "Creates a new state containing only this location", false, 14))
            {
                string key = loc.Key + "_state";
                int n = 2;
                while (G.World.StateByKey.ContainsKey(key)) key = $"{loc.Key}_state{n++}";
                var st = new State { Key = key, Index = G.World.States.Count, Owner = loc.Owner, SourceFile = EditorNewStatesFile, Manpower = 100, Tax = 1 };
                if (loc.Owner != null) st.Cores.Add(loc.Owner);
                G.World.States.Add(st);
                G.World.StateByKey[key] = st;
                MoveLocationToState(loc, st);
                _editorBrush = st;
                Map.HighlightState = st;
            }
            y += 32 * S;
        }

        Raylib.DrawLine((int)x, (int)y, (int)(x + w), (int)y, Gui.PanelBorder);
        y += 8 * S;
        var b = _editorBrush;
        Gui.Label(b == null ? "Brush: (click a state)" : $"Brush state: {b.Key}", x, y, 16, Gui.Gold);
        y += lh + 4 * S;
        if (Gui.Button(new Rectangle(x, y, w, 28 * S), _editorPaint ? "Paint: ON (click locations)" : "Paint: OFF", b != null, null, _editorPaint, 15)) _editorPaint = !_editorPaint;
        y += 34 * S;
        if (b != null)
        {
            Gui.Label($"{b.Locations.Count} locations, tax {b.Tax:0.#}, manpower {b.Manpower:0}", x, y, 14, Gui.TextDim);
            y += lh;
            var countries = G.World.Countries;
            Gui.Label($"Owner: {b.Owner?.Tag ?? "none"}", x, y + 4, 15);
            if (Gui.Button(new Rectangle(x + 150 * S, y, 40 * S, 24 * S), "<", true, "Previous country", false, 14)) SetBrushOwner(b, Step(countries, b.Owner, -1));
            if (Gui.Button(new Rectangle(x + 194 * S, y, 40 * S, 24 * S), ">", true, "Next country", false, 14)) SetBrushOwner(b, Step(countries, b.Owner, 1));
            if (Gui.Button(new Rectangle(x + 238 * S, y, 100 * S, 24 * S), "Selected", Ui.SelectedCountry != null, "Use the country selected in the country panel", false, 13)) SetBrushOwner(b, Ui.SelectedCountry);
            y += 30 * S;
            Gui.Label("Cores: " + (b.Cores.Count == 0 ? "-" : string.Join(" ", b.Cores.Select(c => c.Tag))), x, y, 15);
            y += lh;
            if (b.Owner != null && Gui.Button(new Rectangle(x, y, w, 24 * S), b.Cores.Contains(b.Owner) ? $"Remove core of {b.Owner.Tag}" : $"Add core of {b.Owner.Tag}", true, null, false, 14))
            {
                if (!b.Cores.Remove(b.Owner)) b.Cores.Add(b.Owner);
                _editorDirty = true;
            }
            y += 30 * S;
            if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 24 * S), "Tax -1", b.Tax >= 1, null, false, 13)) { b.Tax -= 1; _editorDirty = true; }
            if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 24 * S), "Tax +1", true, null, false, 13)) { b.Tax += 1; _editorDirty = true; }
            y += 28 * S;
            if (Gui.Button(new Rectangle(x, y, w / 2 - 4, 24 * S), "Manpower -50", b.Manpower >= 50, null, false, 13)) { b.Manpower -= 50; _editorDirty = true; }
            if (Gui.Button(new Rectangle(x + w / 2 + 4, y, w / 2 - 4, 24 * S), "Manpower +50", true, null, false, 13)) { b.Manpower += 50; _editorDirty = true; }
            y += 34 * S;
        }
        if (Gui.Button(new Rectangle(x, y, w, 32 * S), _editorDirty ? "Save state files *" : "Save state files", true,
                "Writes the states back to map/states (comments are kept)", false, 16))
        {
            int files = SaveStates();
            Ui.SetStatus($"Saved {G.World.States.Count} states to {files} file(s)");
            _editorDirty = false;
        }
        y += 40 * S;
        Gui.Wrapped("Tip: Locations are defined in map/locations.txt (HEX = name). Run 'burger generate-locations' to name every color of provinces.png automatically.", x, y, w, 13, Gui.TextDim);
    }

    private static Country? Step(List<Country> list, Country? cur, int dir)
    {
        int i = cur == null ? -1 : list.IndexOf(cur);
        i += dir;
        if (i < -1) i = list.Count - 1;
        if (i >= list.Count) i = -1;
        return i < 0 ? null : list[i];
    }

    private void SetBrushOwner(State s, Country? owner)
    {
        s.Owner = owner;
        foreach (var l in s.Locations) l.Controller = owner;
        G.World.RefreshOwnership();
        _editorDirty = true;
        G.RaiseMapChanged();
    }

    /// <summary>Writes every state back to the file it came from. Returns the number of files written.</summary>
    private int SaveStates() => Burger.Core.Map.StateWriter.Save(G);
}
