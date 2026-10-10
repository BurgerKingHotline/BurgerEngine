using System.Numerics;
using Burger.Core;
using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Save;
using Raylib_cs;

namespace Burger.Runtime;

public enum AppScreen { Menu, Playing, Error }

/// <summary>Window, main loop, main menu and game loading.</summary>
public sealed class App
{
    public Game? Game { get; private set; }
    public MapRenderer? Renderer { get; private set; }
    public CameraController? Camera { get; private set; }
    public MapOverlay? Overlay { get; private set; }
    public UiState Ui { get; private set; } = new();
    private Hud? _hud;

    private readonly AppOptions _opt;
    private string _gameRoot = "";
    private AppScreen _screen = AppScreen.Menu;
    private string _errorMessage = "";
    private Country? _menuSelection;
    private bool _quit;
    private int _frame;
    private Action? _pending;

    public App(AppOptions options) { _opt = options; }

    public string GameRoot => _gameRoot;
    public string SavesDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\')) ?? ".", "saves");
    public string LogsDir => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_gameRoot).TrimEnd('/', '\\')) ?? ".", "logs");

    public void Run()
    {
        _gameRoot = _opt.GameRoot;
        var flags = ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint;
        if (_opt.Hidden) flags |= ConfigFlags.HiddenWindow;
        Raylib.SetConfigFlags(flags);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(_opt.Width, _opt.Height, "BurgerEngine");
        Raylib.SetWindowMinSize(1024, 640);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(120);
        Assets.Font = Raylib.GetFontDefault();

        LoadGame();
        if (Game != null && _opt.LoadSave != null) DoLoadSave(_opt.LoadSave);
        else if (Game != null && (_opt.Tag != null || _opt.Observe)) StartGame(_opt.Observe ? null : _opt.Tag);

        while (!_quit && !Raylib.WindowShouldClose())
        {
            if (_pending != null)
            {
                var action = _pending;
                _pending = null;
                action();
            }
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            if (Game != null) Gui.CheckTheme(Game.Interface.Theme);
            Gui.Scale = Gui.ScaleFor(Raylib.GetScreenHeight());
            Gui.BeginFrame();
            switch (_screen)
            {
                case AppScreen.Error: FrameError(); break;
                case AppScreen.Menu: FrameMenu(dt); break;
                case AppScreen.Playing: FramePlaying(dt); break;
            }
            _frame++;
            if (_opt.Perf) { PerfSample(); if (Game != null && Game.Started && _opt.Speed > 0) Game.Clock.Paused = false; }
            if (_opt.Screenshot != null && _frame == _opt.ScreenshotFrame)
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, _opt.Screenshot);
                Raylib.UnloadImage(img);
                _quit = true;
            }
        }
        Renderer?.Dispose();
        Raylib.CloseWindow();
    }

    // frame timing (--perf): average frame time, simulation time per frame and in-game days per second
    private double _perfTime, _perfFrames, _perfSim;
    private int _perfDay;
    private void PerfSample()
    {
        _perfTime += Raylib.GetFrameTime();
        _perfFrames++;
        _perfSim += Game?.Clock.LastUpdateMs ?? 0;
        if (_perfTime < 3) return;
        int day = Game?.World.Date.TotalDays ?? 0;
        Log.Info($"PERF fps {_perfFrames / _perfTime:0.0}, frame {_perfTime * 1000 / _perfFrames:0.0} ms, simulation {_perfSim / _perfFrames:0.0} ms/frame, {(day - _perfDay) / _perfTime:0.0} days/s");
        if (Game != null)
        {
            int days = Math.Max(1, day - _perfDay);
            Log.Info("PERF systems (ms/day): " + string.Join(", ", Game.ProfileMs.Where(kv => !kv.Key.StartsWith("Path.")).OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value / days:0.00}")));
            Log.Info("PERF paths (per day): " + string.Join(", ", Game.ProfileMs.Where(kv => kv.Key.StartsWith("Path.")).OrderBy(kv => kv.Key).Select(kv => kv.Key.EndsWith("#") ? $"{kv.Key} {kv.Value / days:0.0}" : $"{kv.Key} {kv.Value / days:0.00}ms")));
            Log.Info($"PERF world: {Game.World.Divisions.Count} divisions, {Game.World.Armies.Count} armies, {Game.World.Wars.Count} wars, {Game.World.Battles.Count} battles");
            Game.ProfileMs.Clear();
            Game.Profiling = true;
        }
        _perfDay = day;
        _perfTime = _perfFrames = _perfSim = 0;
    }

    public void RequestQuit() => _quit = true;

    // ===============================================================================================
    // Loading
    // ===============================================================================================
    private void DrawLoading(string text)
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(16, 16, 20, 255));
        string t = text;
        int fs = 28;
        int w = Raylib.MeasureText(t, fs);
        Raylib.DrawText(t, (Raylib.GetScreenWidth() - w) / 2, Raylib.GetScreenHeight() / 2 - fs, fs, new Color(232, 196, 90, 255));
        Raylib.EndDrawing();
    }

    private void LoadGame()
    {
        DrawLoading("Loading...");
        Renderer?.Dispose();
        Renderer = null;
        Game = null;
        Ui = new UiState();
        Log.Clear();
        Log.SetLogFile(Path.Combine(LogsDir, "error.log"));
        try
        {
            var game = Burger.Core.Game.Load(_gameRoot, compileScripts: true);
            Game = game;
            Raylib.SetWindowTitle(game.Data.Info.Name);
            if (!string.IsNullOrEmpty(game.Data.Info.Icon))
            {
                string icon = game.Data.PathOf(game.Data.Info.Icon);
                if (File.Exists(icon))
                {
                    var img = Raylib.LoadImage(icon);
                    Raylib.SetWindowIcon(img);
                    Raylib.UnloadImage(img);
                }
            }
            Assets.Init(game.Data.Root, game.Interface.Theme);
            Gui.ApplyTheme(game.Interface.Theme);
            DrawLoading("Building map...");
            Renderer = new MapRenderer(game);
            Camera = new CameraController(Renderer);
            Overlay = new MapOverlay(game, Renderer);
            _hud = new Hud(this);
            _menuSelection = game.World.GetCountry(game.Data.Info.DefaultTag) ?? game.World.Countries.FirstOrDefault(c => c.Playable && c.Exists);
            if (_menuSelection?.Capital != null) Camera.LookAt(Renderer.LocationPos(_menuSelection.Capital), Camera.MaxDistance * 0.5f);
            _screen = AppScreen.Menu;
        }
        catch (Exception ex)
        {
            _errorMessage = ex is GameLoadException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
            Log.Error("Game failed to load: " + ex.Message);
            Game = null;
            _screen = AppScreen.Error;
        }
    }

    private void StartGame(string? tag)
    {
        if (Game == null) return;
        Game.StartNewGame(tag);
        EnterPlaying();
    }

    private void EnterPlaying()
    {
        if (Game == null || Renderer == null || Camera == null) return;
        _screen = AppScreen.Playing;
        Game.Clock.Paused = true;
        Renderer.DiplomacyFocus = null;
        Renderer.Dirty = true;
        var p = Game.World.Player;
        if (p?.Capital != null) Camera.LookAt(Renderer.LocationPos(p.Capital), 45);
        foreach (var cmd in _opt.Exec) Log.Info($"exec '{cmd}': {Game.Commands.Execute(cmd)}");
        _opt.Exec.Clear();
        for (int i = 0; i < _opt.TicksBeforeScreenshot; i++) Game.Tick();
        if (_opt.Speed > 0) { Game.Clock.Speed = Math.Clamp(_opt.Speed, 1, 5); Game.Clock.Paused = false; }
        ApplyTestOptions();
    }

    /// <summary>Command line automation (used for testing and screenshots).</summary>
    private void ApplyTestOptions()
    {
        var game = Game!;
        foreach (var cmd in _opt.Exec)
        {
            string result = game.Commands.Execute(cmd);
            Log.Info($"exec '{cmd}': {result}");
        }
        _opt.Exec.Clear();
        if (_opt.MapMode != null && Enum.TryParse<MapMode>(_opt.MapMode, true, out var mode)) { Renderer!.Mode = mode; Renderer.Dirty = true; }
        if (_opt.Zoom != null) Camera!.LookAt(Camera.Target, _opt.Zoom.Value);
        if (_opt.Open != null)
        {
            var parts = _opt.Open.Split(':');
            switch (parts[0].ToLowerInvariant())
            {
                case "military": Ui.Left = LeftPanel.Military; break;
                case "recruit": Ui.Left = LeftPanel.Recruit; Ui.RecruitTemplate = game.Data.Templates.Values.FirstOrDefault(); break;
                case "characters": Ui.Left = LeftPanel.Characters; break;
                case "decisions": Ui.Left = LeftPanel.Decisions; break;
                case "production": Ui.Left = LeftPanel.Production; Ui.ProductionTab = int.TryParse(parts.ElementAtOrDefault(1), out var pt) ? pt : 0; break;
                case "designer": Ui.Left = LeftPanel.Designer; break;
                case "air": Ui.Left = LeftPanel.Air; _hud!.AirTab = int.TryParse(parts.ElementAtOrDefault(1), out var at) ? at : 0; break;
                case "population": Ui.Left = LeftPanel.Population; break;
                case "ledger": // ledger[:world][:metric]
                    Ui.Right = RightPanel.Ledger;
                    _hud!.LedgerWorld = parts.Contains("world");
                    if (parts.Length > 1 && parts[^1] != "world") _hud.LedgerMetric = parts[^1];
                    break;
                case "politics": Ui.Left = LeftPanel.Politics; _hud!.PoliticsTab = int.TryParse(parts.ElementAtOrDefault(1), out var pt2) ? pt2 : 0; break;
                case "navy": Ui.Left = LeftPanel.Navy; _hud!.NavyTab = int.TryParse(parts.ElementAtOrDefault(1), out var nt) ? nt : 0; break;
                case "wars": Ui.Right = RightPanel.Wars; break;
                case "world": Ui.Right = RightPanel.World; break;
                case "errors": Ui.ErrorsOpen = true; break;
                case "editor": Ui.EditorActive = true; Renderer!.Mode = MapMode.States; break;
                case "console": Ui.ConsoleOpen = true; break;
                case "country":
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    break;
                case "declare_war": // declare_war:TAG - the casus belli window
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    if (Ui.SelectedCountry != null) _hud!.OpenSupportFromOutside("declare_war", Ui.SelectedCountry);
                    break;
                case "support": // support:TAG:volunteers|lend_lease
                    Ui.SelectedCountry = game.World.GetCountry(parts.ElementAtOrDefault(1)) ?? game.World.Player;
                    Ui.Left = LeftPanel.Country;
                    if (Ui.SelectedCountry != null) _hud!.OpenSupportFromOutside(parts.ElementAtOrDefault(2) ?? "lend_lease", Ui.SelectedCountry);
                    break;
                case "location":
                    var l = game.World.GetLocation(parts.ElementAtOrDefault(1)) ?? game.World.Player?.Capital;
                    Ui.SelectedLocation = l;
                    Renderer!.Selected = l;
                    Ui.Left = LeftPanel.Location;
                    break;
                case "peace":
                    var p = game.World.Player;
                    var war = p == null ? null : game.World.WarsOf(p).FirstOrDefault();
                    if (war != null) _hud!.OpenPeaceFromOutside(war, war.EnemyLeaderOf(p!));
                    break;
            }
        }
        if (_opt.Screenshot != null) game.Events.PlayerQueue.Clear(); // keep test screenshots clean
    }

    /// <summary>Reloads all game files and shows the main menu (runs between frames).</summary>
    public void ReturnToMainMenu() => _pending = LoadGame;

    /// <summary>Loads a save (runs between frames).</summary>
    public void LoadSave(string path) => _pending = () => DoLoadSave(path);

    // ===============================================================================================
    // Saves
    // ===============================================================================================
    public List<string> ListSaves()
    {
        if (!Directory.Exists(SavesDir)) return new List<string>();
        return Directory.GetFiles(SavesDir, "*.sav").OrderByDescending(File.GetLastWriteTimeUtc).ToList();
    }

    public string SaveAs(string? name)
    {
        if (Game == null || !Game.Started) return "Nothing to save";
        try
        {
            Directory.CreateDirectory(SavesDir);
            name ??= $"{Game.World.Player?.Tag ?? "observer"}_{Game.World.Date.Year}_{Game.World.Date.Month:00}_{Game.World.Date.Day:00}";
            string path = Path.Combine(SavesDir, name + ".sav");
            SaveGame.Save(Game, path);
            return $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Log.Error("Save failed: " + ex.Message);
            return "Save failed: " + ex.Message;
        }
    }

    public string QuickSave()
    {
        string r = SaveAs("quicksave");
        Ui.SetStatus(r);
        return r;
    }

    private void DoLoadSave(string path)
    {
        LoadGame();
        if (Game == null) return;
        try
        {
            SaveGame.Load(Game, path);
            Renderer!.Dirty = true;
            EnterPlaying();
            Ui.SetStatus($"Loaded {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _errorMessage = $"Could not load save '{path}':\n{ex.Message}";
            Log.Error(_errorMessage);
            _screen = AppScreen.Error;
        }
    }

    // ===============================================================================================
    // Frames
    // ===============================================================================================
    private void DrawWorld(Camera3D cam)
    {
        Raylib.ClearBackground(new Color(12, 22, 38, 255));
        Raylib.BeginMode3D(cam);
        Renderer!.Draw(cam, (float)Raylib.GetTime());
        Raylib.EndMode3D();
    }

    private void FramePlaying(float dt)
    {
        var game = Game!;
        var map = Renderer!;
        var cam = Camera!;
        _hud!.HandleKeys();
        // camera input + damped movement are resolved here, before BeginDrawing - never inside the draw loop
        cam.Update(dt, Gui.MouseOverUi, false, !_hud.KeyboardCaptured);
        var hovered = Gui.MouseOverUi ? null : map.Pick(Gui.Mouse, cam.Camera);
        if (hovered != map.Hovered) { map.Hovered = hovered; map.Dirty = true; }

        Raylib.BeginDrawing();
        DrawWorld(cam.Camera);
        Overlay!.DrawLabels(cam.Camera);
        Overlay.DrawOrders(cam.Camera, Ui);
        Overlay.DrawUnits(cam.Camera, Ui, Gui.MouseOverUi);
        Overlay.DrawFleets(cam.Camera, Ui, Gui.MouseOverUi);
        if (Overlay.DrawAirBases(cam.Camera, Gui.MouseOverUi)) Ui.Left = LeftPanel.Air;
        HandleMapLeftMouse(hovered);
        if (!Gui.MouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Right)) _hud.OnMapRightClick(hovered);
        if (hovered != null && !Gui.MouseOverUi) DrawHoverInfo(hovered);
        _hud.Draw();
        Gui.EndFrame();
        Raylib.EndDrawing();

        try
        {
            game.Clock.Update(dt);
        }
        catch (Exception ex)
        {
            game.Clock.Paused = true;
            Log.Error($"Simulation error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Ui.SetStatus("Simulation error - game paused (see F2)");
        }
    }

    // ---------------------------------------------------------------------------------------- click or drag-select
    private Vector2? _dragStart;
    private bool _dragging;

    /// <summary>Left mouse on the map: a click selects a location; a drag draws a box that selects your divisions.</summary>
    private void HandleMapLeftMouse(Location? hovered)
    {
        var mouse = Raylib.GetMousePosition();
        if (!Gui.MouseOverUi && !Gui.LeftClickConsumed && Raylib.IsMouseButtonPressed(MouseButton.Left)) { _dragStart = mouse; _dragging = false; }
        if (_dragStart == null) return;
        if (Raylib.IsMouseButtonDown(MouseButton.Left) && Vector2.Distance(mouse, _dragStart.Value) > 6) _dragging = true;
        if (_dragging)
        {
            var r = BoxOf(_dragStart.Value, mouse);
            Raylib.DrawRectangleRec(r, new Color(255, 230, 140, 40));
            Raylib.DrawRectangleLinesEx(r, 1.5f, new Color(255, 230, 140, 220));
        }
        if (!Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            if (_dragging) SelectInBox(BoxOf(_dragStart.Value, mouse));
            else _hud!.OnMapLeftClick(hovered);
            _dragStart = null;
            _dragging = false;
        }
    }

    private static Rectangle BoxOf(Vector2 a, Vector2 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void SelectInBox(Rectangle box)
    {
        var player = Game?.World.Player;
        if (player == null || Overlay == null) return;
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (!shift) Ui.SelectedDivisions.Clear();
        Ui.SelectedArmy = null;
        Ui.SelectedFleet = null;
        foreach (var (rect, owner, divisions) in Overlay.Counters)
        {
            if (owner != player) continue;
            var c = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            if (!Raylib.CheckCollisionPointRec(c, box)) continue;
            foreach (var d in divisions) if (!Ui.SelectedDivisions.Contains(d)) Ui.SelectedDivisions.Add(d);
        }
        Ui.SetStatus(Ui.SelectedDivisions.Count == 0 ? "No divisions of yours in the box" : $"{Ui.SelectedDivisions.Count} division(s) selected - right-click to move");
    }

    private void DrawHoverInfo(Location l)
    {
        string text = l.DisplayName;
        if (l.IsLand)
        {
            text += l.State != null ? $" ({l.State.DisplayName})" : "";
            if (l.Owner != null) text += $" - {l.Owner.Name}";
            if (l.IsOccupied) text += $" [occupied by {l.Controller!.Name}]";
        }
        Color extraColor = Gui.Good;
        string extra = "";
        if (Ui.Left == LeftPanel.Production && Ui.BuildType != null && Game?.World.Player is { } builder && l.State != null)
        {
            var reason = Game.Production.CannotBuildReason(builder, Ui.BuildType, l.State);
            extra = reason == null ? $"Click to build a {Ui.BuildType.Name} in {l.State.DisplayName}" : $"Cannot build here: {reason}";
            if (reason != null) extraColor = Gui.Bad;
        }
        if (Game != null && Game.Features.HasSupply && Renderer?.Mode == MapMode.Supply && Game.World.Player is { } sp && l.IsLand)
            extra = $"Supply: {Game.Supply.SupplyAt(sp, l):0.0} (a division uses about 0.3 per day)";
        if (Game != null && Game.Features.Has(Burger.Core.Modding.FeatureSet.Air) && Game.World.Player is { } ap && l.IsLand)
        {
            var region = Game.Air.RegionOf(l);
            if (Ui.OrderingWing is { } wing)
            {
                string? reason;
                if (Ui.WingMission is { } mission)
                {
                    reason = region == null ? "No air region here" : Game.Air.CannotAssignReason(wing, region, mission);
                    extra = reason == null ? $"Click: {Burger.Core.Air.AirSystem.MissionName(mission)} over {region!.DisplayName}" : $"Cannot fly here: {reason}";
                }
                else
                {
                    reason = l.State == null ? "No state here" : Game.Air.CannotRebaseReason(wing, l.State);
                    extra = reason == null ? $"Click to move {wing.Name} to {l.State!.DisplayName}" : $"Cannot base here: {reason}";
                }
                if (reason != null) extraColor = Gui.Bad;
            }
            else if (Renderer?.Mode == MapMode.Air && region != null)
            {
                var (share, contested) = Game.Air.Superiority(region, ap);
                extra = $"{region.DisplayName}: " + (contested ? $"your side holds {share:P0} of the sky" : "no air superiority missions") +
                        (l.State != null && l.State.Buildings.GetValueOrDefault("air_base") > 0 ? $"  |  air base {Game.Air.PlanesAt(l.State):0}/{Game.Air.Capacity(l.State):0} planes" : "");
            }
        }
        if (Ui.Tool != PlanTool.None && Ui.PlanArmy is { } planArmy && l.IsLand)
            extra = Ui.Tool switch
            {
                PlanTool.Front => planArmy.FrontLimit.Contains(l) ? "Click to remove from the front" : "Click to add to the front",
                PlanTool.Arrow => $"Click: attack towards {l.DisplayName}",
                _ => planArmy.Fallback.Contains(l) ? "Click to remove from the fallback line" : "Click to add to the fallback line",
            };
        if (Ui.Left == LeftPanel.Recruit && Ui.RecruitTemplate != null && Game?.World.Player is { } player)
        {
            var reason = Game.Rules.Economy.CannotRecruitReason(Game, player, Ui.RecruitTemplate, l);
            extra = reason == null ? $"Click to train {Ui.RecruitTemplate.Name} here" : $"Cannot train here: {reason}";
            if (reason != null) extraColor = Gui.Bad;
        }
        var m = Gui.Measure(text, 15);
        var m2 = extra.Length > 0 ? Gui.Measure(extra, 14) : Vector2.Zero;
        float x = Gui.Mouse.X + 16, y = Gui.Mouse.Y + 18;
        float w = Math.Max(m.X, m2.X), h = m.Y + (extra.Length > 0 ? m2.Y + 2 : 0);
        Raylib.DrawRectangleRec(new Rectangle(x - 6, y - 3, w + 12, h + 6), new Color(10, 10, 14, 200));
        Raylib.DrawRectangleLinesEx(new Rectangle(x - 6, y - 3, w + 12, h + 6), 1, new Color(Gui.PanelInner.R, Gui.PanelInner.G, Gui.PanelInner.B, (byte)220));
        Gui.Label(text, x, y, 15);
        if (extra.Length > 0) Gui.Label(extra, x, y + m.Y + 2, 14, extraColor);
    }

    private void FrameMenu(float dt)
    {
        var game = Game!;
        var map = Renderer!;
        var cam = Camera!;
        cam.Update(dt, Gui.MouseOverUi, false, true);
        var hovered = Gui.MouseOverUi ? null : map.Pick(Gui.Mouse, cam.Camera);
        if (map.Mode != MapMode.Political) { map.Mode = MapMode.Political; map.Dirty = true; }

        Raylib.BeginDrawing();
        DrawWorld(cam.Camera);
        Overlay!.DrawLabels(cam.Camera);
        if (!Gui.MouseOverUi && Raylib.IsMouseButtonPressed(MouseButton.Left) && hovered?.Owner != null && hovered.Owner.Playable)
        {
            _menuSelection = hovered.Owner;
            map.Selected = null;
            map.Dirty = true;
        }
        if (hovered != null && !Gui.MouseOverUi) DrawHoverInfo(hovered);

        float s = Gui.Scale;
        int sh = Raylib.GetScreenHeight();
        var panel = new Rectangle(16 * s, 16 * s, 420 * s, sh - 32 * s);
        Gui.Panel(panel);
        float x = panel.X + 18 * s, y = panel.Y + 16 * s, w = panel.Width - 36 * s;
        Gui.LabelShadow(game.Data.Info.Name, x, y, 32, Gui.Gold, FontKind.Header);
        y += 40 * s;
        string sub = $"Version {game.Data.Info.Version}" + (game.Data.Info.Author.Length > 0 ? $" - by {game.Data.Info.Author}" : "");
        Gui.Label(sub, x, y, 15, Gui.TextDim);
        y += 24 * s;
        if (game.Data.Info.Description.Length > 0) y += Gui.Wrapped(game.Data.Info.Description, x, y, w, 15, Gui.Text) + 6 * s;
        Gui.Label($"Start date: {game.World.Date.ToDisplay()}", x, y, 15, Gui.TextDim);
        y += 26 * s;
        if (Log.ErrorCount > 0)
        {
            if (Gui.Button(new Rectangle(x, y, w, 26 * s), $"{Log.ErrorCount} error(s), {Log.WarningCount} warning(s) - view log", true, "Problems found in the game files", false, 14))
                Ui.ErrorsOpen = !Ui.ErrorsOpen;
            y += 32 * s;
        }

        Gui.Label("Choose a country (or click it on the map):", x, y, 16);
        y += 26 * s;
        var playable = game.World.Countries.Where(c => c.Playable && c.Exists).OrderBy(c => c.Name).ToList();
        float listH = panel.Y + panel.Height - y - 250 * s;
        float top = Gui.BeginScroll("menu-countries", new Rectangle(x, y, w, listH), playable.Count * 40 * s);
        float cy = top;
        foreach (var c in playable)
        {
            var row = new Rectangle(x, cy, w - 10 * s, 36 * s);
            bool sel = c == _menuSelection;
            Gui.ButtonFrame(row, true, Gui.Hover(row), sel);
            Gui.Flag(c, new Rectangle(row.X + 4, row.Y + 4, 42 * s, 28 * s));
            Gui.Label(c.Name, row.X + 54 * s, row.Y + 8 * s, 17, sel ? Color.White : Gui.Text, FontKind.Bold);
            if (Gui.Clicked(row))
            {
                _menuSelection = c;
                if (c.Capital != null) cam.LookAt(map.LocationPos(c.Capital));
            }
            cy += 40 * s;
        }
        Gui.EndScroll();
        y += listH + 10 * s;

        if (_menuSelection != null)
        {
            var c = _menuSelection;
            Gui.Flag(c, new Rectangle(x, y, 60 * s, 40 * s));
            Gui.LabelShadow(c.Name, x + 70 * s, y, 21, Gui.Gold, FontKind.Header);
            Gui.Label($"{c.OwnedStates.Count} states - ruler: {c.Ruler?.DisplayName ?? "none"}", x + 70 * s, y + 24 * s, 14, Gui.TextDim);
            y += 50 * s;
        }
        float bh = 36 * s;
        if (Gui.Button(new Rectangle(x, y, w, bh), _menuSelection != null ? $"Play as {_menuSelection.Name}" : "Select a country", _menuSelection != null, null, false, 18))
        {
            StartGame(_menuSelection!.Tag);
            Gui.EndFrame();
            Raylib.EndDrawing();
            return;
        }
        y += bh + 6 * s;
        float half = (w - 6 * s) / 2;
        if (Gui.Button(new Rectangle(x, y, half, bh), "Observe", true, "Watch the AI play")) { StartGame(null); Gui.EndFrame(); Raylib.EndDrawing(); return; }
        if (Gui.Button(new Rectangle(x + half + 6 * s, y, half, bh), "Load game", ListSaves().Count > 0)) Ui.SaveMenuOpen = !Ui.SaveMenuOpen;
        y += bh + 6 * s;
        if (Gui.Button(new Rectangle(x, y, half, bh), "Reload files", true, "Re-read all game files (after editing them)")) ReturnToMainMenu();
        if (Gui.Button(new Rectangle(x + half + 6 * s, y, half, bh), "Quit")) _quit = true;

        if (Ui.ErrorsOpen) DrawMenuErrors();
        if (Ui.SaveMenuOpen) DrawMenuSaves();
        Gui.EndFrame();
        Raylib.EndDrawing();
    }

    private void DrawMenuErrors()
    {
        float s = Gui.Scale;
        float w = Math.Min(1000 * s, Raylib.GetScreenWidth() - 480 * s), h = Raylib.GetScreenHeight() * 0.7f;
        var r = new Rectangle(460 * s, 30 * s, w, h);
        Gui.Panel(r, "Error log");
        if (Gui.CloseButton(r)) { Ui.ErrorsOpen = false; return; }
        var entries = Log.Entries.Where(e => e.Level != LogLevel.Info).ToList();
        float top = Gui.BeginScroll("menu-errors", new Rectangle(r.X + 8, r.Y + 34 * s, w - 16, h - 42 * s), entries.Count * 19 * s);
        float y = top;
        foreach (var e in entries)
        {
            string t = e.ToString().Replace('\n', ' ');
            if (t.Length > 170) t = t[..167] + "...";
            Gui.Label(t, r.X + 12, y, 14, e.Level == LogLevel.Error ? Gui.Bad : Gui.Gold);
            y += 19 * s;
        }
        Gui.EndScroll();
    }

    private void DrawMenuSaves()
    {
        float s = Gui.Scale;
        float w = 520 * s, h = 460 * s;
        var r = new Rectangle(460 * s, 30 * s, w, h);
        Gui.Panel(r, "Load game");
        if (Gui.CloseButton(r)) { Ui.SaveMenuOpen = false; return; }
        var saves = ListSaves();
        float top = Gui.BeginScroll("menu-saves", new Rectangle(r.X + 10, r.Y + 36 * s, w - 20, h - 46 * s), saves.Count * 40 * s);
        float y = top;
        foreach (var path in saves)
        {
            var (_, date, player) = SaveGame.Peek(path);
            if (Gui.Button(new Rectangle(r.X + 10, y, w - 34, 34 * s), $"{Path.GetFileNameWithoutExtension(path)}  ({player}, {date})", true, path, false, 15))
            {
                Gui.EndScroll();
                Ui.SaveMenuOpen = false;
                LoadSave(path);
                return;
            }
            y += 40 * s;
        }
        Gui.EndScroll();
    }

    private void FrameError()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(22, 16, 16, 255));
        float s = Gui.Scale;
        float w = Raylib.GetScreenWidth() - 80 * s, h = Raylib.GetScreenHeight() - 80 * s;
        var r = new Rectangle(40 * s, 40 * s, w, h);
        Gui.Panel(r, "The game could not be loaded");
        float y = r.Y + 40 * s;
        y += Gui.Wrapped(_errorMessage, r.X + 16, y, w - 32, 17, Gui.Bad) + 10 * s;
        Gui.Label($"Game folder: {Path.GetFullPath(_gameRoot)}", r.X + 16, y, 15, Gui.TextDim);
        y += 26 * s;
        var entries = Log.Entries.Where(e => e.Level == LogLevel.Error).TakeLast(30).ToList();
        foreach (var e in entries)
        {
            Gui.Label(e.ToString().Length > 180 ? e.ToString()[..177] + "..." : e.ToString(), r.X + 16, y, 14, Gui.Gold);
            y += 19 * s;
        }
        if (Gui.Button(new Rectangle(r.X + 16, r.Y + h - 50 * s, 220 * s, 36 * s), "Retry (reload files)") || Raylib.IsKeyPressed(KeyboardKey.F5))
            ReturnToMainMenu();
        if (Gui.Button(new Rectangle(r.X + 250 * s, r.Y + h - 50 * s, 160 * s, 36 * s), "Quit")) _quit = true;
        Gui.EndFrame();
        Raylib.EndDrawing();
    }
}
