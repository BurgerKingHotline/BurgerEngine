using System.Numerics;
using Burger.Core;
using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Runtime;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Burger.Studio;

/// <summary>The live world in the Viewport: loading, drawing, picking, painting states.</summary>
public sealed partial class Studio
{
    private Game? _world;
    private MapRenderer? _renderer;
    private CameraController? _camera;
    private MapOverlay? _overlay;
    private RenderTexture2D _rt;
    private int _rtW, _rtH;
    private bool _worldStale, _countriesDirty;
    private string? _worldError;
    private readonly HashSet<string> _worldOwnFiles = new(StringComparer.OrdinalIgnoreCase);
    private int _tool; // 0 select, 1 paint
    private int _mapMode;
    private readonly HashSet<State> _pendingStates = new();
    private bool _painting;
    private Location? _hoverLoc;

    private static readonly string[] BaseModes = { "Political", "Terrain", "States", "Diplomacy", "Locations" };

    /// <summary>Loads the world from the saved files (asks to save map files first).</summary>
    public void LoadWorld(bool askSave = true)
    {
        if (Store == null) return;
        var dirty = Store.DirtyFiles.Where(f => !f.EndsWith(".cs")).ToList();
        if (dirty.Count > 0 && askSave)
        {
            ShowChoice("Reload world", "The world is loaded from the saved files. Save changes first?\n\n" + string.Join("\n", dirty.Take(12)),
                new[] { "Save and reload", "Reload without saving", "Cancel" }, i =>
                {
                    if (i == 0) { SaveAll(); LoadWorld(false); }
                    else if (i == 1) LoadWorld(false);
                });
            return;
        }
        FlushPendingStates();
        DisposeWorld();
        SetStatus("Loading the world...");
        try
        {
            Log.Clear();
            var g = Game.Load(Store.GameDir, compileScripts: false, seed: 1);
            Assets.Init(g.Data.Root);
            _world = g;
            _renderer = new MapRenderer(g);
            _camera = new CameraController(_renderer);
            _overlay = new MapOverlay(g, _renderer);
            _worldError = null;
            _worldStale = false;
            _worldOwnFiles.Clear();
            var w = g.World;
            SetStatus($"World loaded: {w.Locations.Count} locations, {w.States.Count} states, {w.Countries.Count} countries" +
                      (Log.ErrorCount > 0 ? $" ({Log.ErrorCount} errors - press Check)" : ""));
            // keep the selection pointing at the new objects
            if (Selected is SelLocation sl) Selected = w.GetLocation(sl.Loc.Key) is { } nl ? new SelLocation { Loc = nl } : null;
            if (Selected is SelState ss) Selected = w.GetState(ss.State.Key) is { } ns ? new SelState { State = ns } : null;
        }
        catch (Exception ex)
        {
            _worldError = ex.Message;
            DisposeWorld();
            SetStatus("The world could not be loaded - see the Viewport");
        }
    }

    private void DisposeWorld()
    {
        _renderer?.Dispose();
        _renderer = null;
        _camera = null;
        _overlay = null;
        _world = null;
        if (_rtW > 0) { Raylib.UnloadRenderTexture(_rt); _rtW = _rtH = 0; }
        _pendingStates.Clear();
    }

    public Game? World => _world;

    private void OnStoreChanged(string rel, object? source)
    {
        if (source == WorldEditSource) { _worldOwnFiles.Add(rel); return; }
        if (rel == "*") { _worldStale = true; return; }
        if (rel.StartsWith("common/countries", StringComparison.OrdinalIgnoreCase)) _countriesDirty = true;
        else if (rel.StartsWith("map/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("common/terrain", StringComparison.OrdinalIgnoreCase)) _worldStale = true;
    }

    /// <summary>Marker passed to the store for edits made by the map tools (they are already applied to the world).</summary>
    public static readonly object WorldEditSource = new();

    /// <summary>Copies country names, colors and flags from the (unsaved) files into the running world.</summary>
    private void SyncCountries()
    {
        _countriesDirty = false;
        if (_world == null || Store == null) return;
        foreach (var f in Store.FilesOf("common/countries"))
            foreach (var n in Store.Parse(f).All)
            {
                var c = _world.World.GetCountry(n.Key);
                if (c == null || n.Children == null) continue;
                if (Rgb.TryParse(n.Get("color"), out var col)) c.Color = c.DefaultColor = col;
                if (n.GetString("name") is { } nm) c.Name = c.DefaultName = nm;
                c.FlagPath = n.GetString("flag") ?? $"gfx/flags/{c.Tag}.png";
            }
        if (_renderer != null) _renderer.Dirty = true;
        _world.RaiseMapChanged();
    }

    public void FocusOn(Location l)
    {
        if (_renderer == null || _camera == null) return;
        _camera.LookAt(_renderer.LocationPos(l), Math.Min(_camera.Distance, 60));
    }

    // =============================================================================================
    // Viewport window
    // =============================================================================================

    private void DrawViewport()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(2, 2));
        if (_focusViewport && _frame >= 3) { ImGui.SetNextWindowFocus(); _focusViewport = false; }
        bool open = ImGui.Begin(WViewport, ref _showViewport, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar();
        if (!open) { ImGui.End(); return; }
        TrackHover(WViewport);
        if (_countriesDirty) SyncCountries();

        // ------------------------------------------------ toolbar
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4);
        var modes = BaseModes.Concat(_world?.Interface.MapModes.Select(m => m.Name) ?? Enumerable.Empty<string>()).ToArray();
        if (_mapMode >= modes.Length) _mapMode = 0;
        ImGui.SetNextItemWidth(140);
        ImGui.Combo("##mapmode", ref _mapMode, modes, modes.Length);
        Ui.Tip("Map mode");
        ImGui.SameLine();
        ImGui.RadioButton("Select", ref _tool, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Paint state", ref _tool, 1);
        Ui.Tip("Click or drag over locations to move them into the selected state");
        ImGui.SameLine();
        if (ImGui.Button("Reload world")) LoadWorld();
        Ui.Tip("Load the map, states and countries again from the saved files");
        if (_worldStale) { ImGui.SameLine(); Ui.T("map files changed - reload to see them", Ui.Yellow); }
        if (_tool == 1)
        {
            var target = PaintTarget();
            ImGui.SameLine();
            Ui.T(target == null ? "select a state first" : $"painting into {target.Key}", target == null ? Ui.Red : Ui.Gold);
        }

        var avail = ImGui.GetContentRegionAvail();
        int w = Math.Max(16, (int)avail.X), h = Math.Max(16, (int)avail.Y);
        if (_world == null || _renderer == null || _camera == null)
        {
            ImGui.Dummy(new Vector2(20, 20));
            if (_worldError != null)
            {
                Ui.T("The world could not be loaded:", Ui.Red);
                Ui.Wrapped(_worldError);
                Ui.T("Fix the problem (Check for errors lists it), then press Reload world.", Ui.Dim);
            }
            else Ui.T("Loading...", Ui.Dim);
            ImGui.End();
            return;
        }

        if (w != _rtW || h != _rtH)
        {
            if (_rtW > 0) Raylib.UnloadRenderTexture(_rt);
            _rt = Raylib.LoadRenderTexture(w, h);
            _rtW = w;
            _rtH = h;
        }

        var origin = ImGui.GetCursorScreenPos();
        Viewport.Origin = origin;
        Viewport.Width = w;
        Viewport.Height = h;
        var io = ImGui.GetIO();
        var mouse = io.MousePos - origin;
        bool inside = mouse.X >= 0 && mouse.Y >= 0 && mouse.X < w && mouse.Y < h;
        bool hovered = inside && ImGui.IsWindowHovered() && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId);
        bool focused = ImGui.IsWindowFocused();

        // ------------------------------------------------ input
        float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
        _camera.Update(dt, !hovered, false, focused && !io.WantTextInput);
        var loc = hovered ? _renderer.Pick(mouse, _camera.Camera) : null;
        _hoverLoc = loc;
        if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            ImGui.SetWindowFocus(WViewport);
            if (_tool == 0)
            {
                FlushPendingStates();
                Selected = loc == null ? null : new SelLocation { Loc = loc };
            }
            else _painting = true;
        }
        if (_painting && Raylib.IsMouseButtonDown(MouseButton.Left) && loc != null && loc.IsLand && PaintTarget() is { } st && loc.State != st)
            MoveToState(loc, st);
        if (_painting && !Raylib.IsMouseButtonDown(MouseButton.Left)) { _painting = false; FlushPendingStates(); }

        // ------------------------------------------------ draw into the texture
        var r = _renderer;
        r.Mode = _mapMode < BaseModes.Length ? (MapMode)_mapMode : MapMode.Political;
        r.CustomMode = _mapMode >= BaseModes.Length ? _world.Interface.MapModes[_mapMode - BaseModes.Length] : null;
        var selLoc = Selected switch { SelLocation sl => sl.Loc, _ => null };
        var selState = Selected switch { SelLocation sl => sl.Loc.State, SelState ss => ss.State, _ => null };
        if (r.Selected != selLoc || r.HighlightState != selState || r.Hovered != loc)
        {
            r.Selected = selLoc;
            r.HighlightState = selState;
            r.Hovered = loc;
            r.Dirty = true;
        }
        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(new Color(12, 22, 38, 255));
        Raylib.BeginMode3D(_camera.Camera);
        r.Draw(_camera.Camera, (float)Raylib.GetTime());
        Raylib.EndMode3D();
        Gui.Scale = 1f;
        _overlay!.DrawLabels(_camera.Camera);
        Raylib.EndTextureMode();
        rlImGui.ImageRenderTexture(_rt);

        if (hovered && loc != null && !_painting && ImGui.BeginTooltip())
        {
            Ui.T(Index!.Pretty(loc.Key), Ui.Gold);
            Ui.T($"{loc.Key}   ({loc.MapColor.ToHex()})", Ui.Dim);
            if (loc.IsWater) Ui.T("Water");
            else
            {
                Ui.T($"State: {loc.State?.Key ?? "none"}");
                Ui.T($"Owner: {(loc.Owner != null ? $"{loc.Owner.Name} ({loc.Owner.Tag})" : "none")}");
                Ui.T($"Terrain: {loc.Terrain.Name}");
            }
            ImGui.EndTooltip();
        }
        ImGui.End();
    }

    private State? PaintTarget() => Selected switch { SelLocation sl => sl.Loc.State, SelState ss => ss.State, _ => null };

    // =============================================================================================
    // State editing (used by the viewport and the inspector)
    // =============================================================================================

    public void MoveToState(Location loc, State target)
    {
        var old = loc.State;
        if (old != null) { old.Locations.Remove(loc); _pendingStates.Add(old); }
        loc.State = target;
        target.Locations.Add(loc);
        loc.Controller = target.Owner;
        _pendingStates.Add(target);
        _world!.World.RefreshOwnership();
        _world.RaiseMapChanged();
        if (_renderer != null) _renderer.Dirty = true;
    }

    public void FlushPendingStates()
    {
        if (_pendingStates.Count == 0) return;
        foreach (var s in _pendingStates.ToList()) WriteState(s);
        _pendingStates.Clear();
    }

    public void WriteState(State s)
    {
        if (Store == null) return;
        if (string.IsNullOrEmpty(s.SourceFile)) s.SourceFile = StateWriter.NewStatesFile;
        string file = s.SourceFile;
        if (!Store.Exists(file)) Store.Set(file, "# States created in Burger Studio.\n", WorldEditSource);
        Store.Edit(file, t => StateWriter.SetState(t, file, s), WorldEditSource);
        SetStatus($"State {s.Key} changed (not saved yet)");
    }

    /// <summary>Country object for a tag (adds a placeholder for countries created but not loaded yet).</summary>
    public Country? CountryOf(string tag)
    {
        tag = tag.Trim().ToUpperInvariant();
        if (tag.Length == 0 || _world == null) return null;
        var c = _world.World.GetCountry(tag);
        if (c != null) return c;
        if (!Country.IsValidTag(tag) || !Index!.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return null;
        c = new Country { Tag = tag, Index = _world.World.Countries.Count, Color = new Rgb(128, 128, 128) };
        _world.World.Countries.Add(c);
        _world.World.CountryByTag[tag] = c;
        _countriesDirty = true;
        return c;
    }

    public void SetStateOwner(State s, Country? c)
    {
        if (s.Owner == c) return;
        s.Owner = c;
        foreach (var l in s.Locations) l.Controller = c;
        if (c != null && s.Cores.Count == 0) s.Cores.Add(c);
        _world!.World.RefreshOwnership();
        _world.RaiseMapChanged();
        if (_renderer != null) _renderer.Dirty = true;
        WriteState(s);
    }

    public void NewStateFromLocation(Location l)
    {
        if (_world == null || !l.IsLand) return;
        var w = _world.World;
        string def = l.Key + "_state";
        for (int n = 2; w.StateByKey.ContainsKey(def); n++) def = $"{l.Key}_state{n}";
        Prompt("New state", $"Id of the new state (it will contain {l.Key}):", def,
            s => s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || "{}=#\"".Contains(c)) ? "No spaces or { } = # \"" : w.StateByKey.ContainsKey(s) ? "That state already exists" : null,
            key =>
            {
                var old = l.State;
                string file = old?.SourceFile is { Length: > 0 } f ? f : (Store!.FilesOf("map/" + w.Map.Settings.StatesFolder).FirstOrDefault() ?? StateWriter.NewStatesFile);
                var st = new State { Key = key, Index = w.States.Count, Owner = l.Owner, SourceFile = file, Manpower = 100, Tax = 1 };
                if (st.Owner != null) st.Cores.Add(st.Owner);
                w.States.Add(st);
                w.StateByKey[key] = st;
                MoveToState(l, st);
                FlushPendingStates();
                Selected = new SelState { State = st };
            });
    }

    public void DeleteState(State s)
    {
        Confirm("Delete state", $"Delete state {s.Key}? Its {s.Locations.Count} locations will have no state until you paint them into another one.", () =>
        {
            FlushPendingStates();
            foreach (var l in s.Locations) { l.State = null; l.Controller = null; }
            s.Locations.Clear();
            _world!.World.States.Remove(s);
            _world.World.StateByKey.Remove(s.Key);
            _world.World.RefreshOwnership();
            _world.RaiseMapChanged();
            if (_renderer != null) _renderer.Dirty = true;
            string file = s.SourceFile;
            if (file.Length > 0) Store!.Edit(file, t => StateWriter.RemoveState(t, file, s.Key), WorldEditSource);
            Selected = null;
            SetStatus($"Deleted state {s.Key} (not saved yet)");
        });
    }

    private void GenerateLocationNames()
    {
        SaveAll();
        try
        {
            var (added, total) = MapTools.GenerateLocationNames(Store!.GameDir);
            Store.ReloadAll();
            if (added == 0) { ShowMessage("Location names", $"All {total} colors of the province map already have a name."); return; }
            ShowMessage("Location names", $"Added {added} new locations to map/locations.txt ({total} colors on the map).\n\n" +
                                          "Sea zones: change a line to  HEX = { name = my_sea type = sea }.\nNew locations have no state yet - paint them into states.");
            LoadWorld(false);
        }
        catch (Exception ex) { ShowMessage("Could not generate names", ex.Message); }
    }
}
