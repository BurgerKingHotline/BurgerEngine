using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The in-game interface. Split over several files (Hud.*.cs) by panel.</summary>
public sealed partial class Hud
{
    private readonly App _app;
    private Game G => _app.Game!;
    private MapRenderer Map => _app.Renderer!;
    private CameraController Cam => _app.Camera!;
    private UiState Ui => _app.Ui;

    private float S => Gui.Scale;
    private float TopH => 46 * S;

    public Hud(App app) { _app = app; }

    private static Color C(Rgb c, int a = 255) => new(c.R, c.G, c.B, (byte)a);

    /// <summary>Is a feature (game/features.txt) installed? Menus of missing features are hidden.</summary>
    private bool Has(string feature) => G.Features.Has(feature);

    /// <summary>True while a text field (console) has keyboard focus.</summary>
    public bool KeyboardCaptured => Ui.ConsoleOpen || Gui.TextFocused;

    // ===============================================================================================
    // Keyboard shortcuts
    // ===============================================================================================
    public void HandleKeys()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Grave) || Raylib.IsKeyPressed(KeyboardKey.F12))
        {
            Ui.ConsoleOpen = !Ui.ConsoleOpen;
            _consoleSkipChar = true;
            return;
        }
        if (Ui.ConsoleOpen || Gui.TextFocused) return;
        var clock = G.Clock;
        if (Raylib.IsKeyPressed(KeyboardKey.Space)) clock.Paused = !clock.Paused;
        for (int i = 1; i <= 5; i++)
            if (Raylib.IsKeyPressed(KeyboardKey.Zero + i)) { clock.Speed = i; clock.Paused = false; }
        if (Raylib.IsKeyPressed(KeyboardKey.KpAdd) || Raylib.IsKeyPressed(KeyboardKey.Equal)) clock.Speed = Math.Min(5, clock.Speed + 1);
        if (Raylib.IsKeyPressed(KeyboardKey.KpSubtract) || Raylib.IsKeyPressed(KeyboardKey.Minus)) clock.Speed = Math.Max(1, clock.Speed - 1);
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleMapMode();
        if (G.World.Player != null)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.F1) && Has(FeatureSet.Military)) ToggleLeft(LeftPanel.Military);
            if (Raylib.IsKeyPressed(KeyboardKey.R) && Has(FeatureSet.Military)) ToggleLeft(LeftPanel.Recruit);
            if (Raylib.IsKeyPressed(KeyboardKey.F3) && Has(FeatureSet.Characters)) ToggleLeft(LeftPanel.Characters);
            if (Raylib.IsKeyPressed(KeyboardKey.P) && Has(FeatureSet.Production)) ToggleLeft(LeftPanel.Production);
            if (Raylib.IsKeyPressed(KeyboardKey.N) && Has(FeatureSet.Navy)) ToggleLeft(LeftPanel.Navy);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F2)) Ui.ErrorsOpen = !Ui.ErrorsOpen;
        if (Raylib.IsKeyPressed(KeyboardKey.F4) && Has(FeatureSet.Diplomacy)) ToggleRight(RightPanel.Wars);
        if (Raylib.IsKeyPressed(KeyboardKey.F5)) _app.QuickSave();
        if (Raylib.IsKeyPressed(KeyboardKey.F8)) ToggleRight(RightPanel.World);
        if (Raylib.IsKeyPressed(KeyboardKey.F10)) ToggleEditor();
        HandleMechanicKeys();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (Ui.SaveMenuOpen) Ui.SaveMenuOpen = false;
            else if (Ui.ChoosingGeneralFor != null) Ui.ChoosingGeneralFor = null;
            else if (Ui.RecruitTemplate != null && Ui.Left == LeftPanel.Recruit) Ui.RecruitTemplate = null;
            else if (Ui.BuildType != null && Ui.Left == LeftPanel.Production) Ui.BuildType = null;
            else if (Ui.SelectedDivisions.Count > 0) Ui.ClearSelection();
            else if (Ui.SelectedFleet != null) Ui.SelectedFleet = null;
            else if (Ui.OrderingWing != null) Ui.OrderingWing = null;
            else if (Ui.Tool != PlanTool.None) { Ui.Tool = PlanTool.None; Ui.PlanArmy = null; }
            else if (Ui.Right != RightPanel.None) Ui.Right = RightPanel.None;
            else if (Ui.Left != LeftPanel.None) CloseLeft();
            else if (Ui.ErrorsOpen) Ui.ErrorsOpen = false;
            else Ui.PauseMenuOpen = !Ui.PauseMenuOpen;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace))
            foreach (var d in Ui.SelectedDivisions) G.Warfare.StopDivision(d);
    }

    private void ToggleRight(RightPanel p) => Ui.Right = Ui.Right == p ? RightPanel.None : p;

    private void ToggleLeft(LeftPanel p)
    {
        if (Ui.Left == p) CloseLeft();
        else
        {
            Ui.Left = p;
            if (p != LeftPanel.Location && Map.Selected != null) { Map.Selected = null; Map.Dirty = true; }
        }
    }

    private void CloseLeft()
    {
        Ui.Left = LeftPanel.None;
        Ui.RecruitTemplate = null;
        Ui.ChoosingGeneralFor = null;
        if (Map.Selected != null) { Map.Selected = null; Map.Dirty = true; }
    }

    private void CycleMapMode()
    {
        Map.CustomMode = null;
        Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Diplomacy && !Has(FeatureSet.Diplomacy)) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Supply && !G.Features.HasSupply) Map.Mode = (MapMode)(((int)Map.Mode + 1) % Enum.GetValues<MapMode>().Length);
        if (Map.Mode == MapMode.Air && !Has(FeatureSet.Air)) Map.Mode = MapMode.Political;
        Map.Dirty = true;
    }

    private void ToggleEditor()
    {
        Ui.EditorActive = !Ui.EditorActive;
        if (Ui.EditorActive)
        {
            G.Clock.Paused = true;
            Map.Mode = MapMode.States;
        }
        else
        {
            _editorBrush = null;
            Map.HighlightState = null;
        }
        Map.Dirty = true;
    }

    // ===============================================================================================
    // Map clicks (called by App when the mouse is not over the UI)
    // ===============================================================================================
    public void OnMapLeftClick(Location? loc)
    {
        if (Ui.EditorActive) { EditorClick(loc); return; }
        // construction mode: clicking one of your states queues the chosen building there
        if (Ui.Left == LeftPanel.Production && Ui.BuildType != null && G.World.Player != null)
        {
            if (loc?.State != null)
            {
                var err = G.Production.QueueBuilding(G.World.Player, Ui.BuildType, loc.State);
                Ui.SetStatus(err != null ? $"Cannot build in {loc.State.DisplayName}: {err}" : $"{Ui.BuildType.Name} queued in {loc.State.DisplayName}");
            }
            return;
        }
        // battle plan drawing (front segment, offensive arrow, fallback line)
        if (Ui.Tool != PlanTool.None && Ui.PlanArmy != null)
        {
            if (loc != null) PlanMapClick(Ui.PlanArmy, loc);
            return;
        }
        // air orders: clicking the map gives the wing a region (with its mission) or a new air base
        if (Ui.OrderingWing != null && Has(FeatureSet.Air))
        {
            if (loc != null) WingMapOrder(Ui.OrderingWing, loc);
            return;
        }
        // recruit mode: clicking one of your locations trains the chosen template there
        if (Ui.Left == LeftPanel.Recruit && Ui.RecruitTemplate != null && G.World.Player != null)
        {
            if (loc != null) RecruitAt(G.World.Player, Ui.RecruitTemplate, loc);
            return;
        }
        Ui.SelectedDivisions.Clear();
        Ui.SelectedArmy = null;
        Ui.SelectedFleet = null;
        Map.Selected = loc;
        Map.Dirty = true;
        Ui.SelectedLocation = loc;
        if (loc == null) { if (Ui.Left is LeftPanel.Location or LeftPanel.Country) Ui.Left = LeftPanel.None; return; }
        if (Ui.Right == RightPanel.Peace && Ui.Peace != null && loc.State != null)
        {
            TogglePeaceState(loc.State);
            return;
        }
        Ui.Left = LeftPanel.Location;
    }

    public void OnMapRightClick(Location? loc)
    {
        if (Ui.EditorActive) return;
        if (Ui.RecruitTemplate != null) { Ui.RecruitTemplate = null; Ui.SetStatus("Recruiting cancelled"); return; }
        if (Ui.BuildType != null) { Ui.BuildType = null; Ui.SetStatus("Construction mode ended"); return; }
        if (Ui.OrderingWing != null) { Ui.OrderingWing = null; Ui.SetStatus("Air order cancelled"); return; }
        if (Ui.Tool != PlanTool.None) { Ui.Tool = PlanTool.None; Ui.PlanArmy = null; Ui.SetStatus("Done drawing"); return; }
        if (loc == null) return;
        if (Ui.SelectedFleet != null && Has(FeatureSet.Navy))
        {
            var f = Ui.SelectedFleet;
            if (!G.Navy.Fleets.Contains(f)) { Ui.SelectedFleet = null; return; }
            if (f.Battle != null) Ui.SetStatus($"{f.Name} is in battle");
            else if (G.Navy.OrderMove(f, loc)) Ui.SetStatus($"{f.Name} sailing to {loc.DisplayName}");
            else Ui.SetStatus(loc.IsLand ? $"{f.Name} cannot dock at {loc.DisplayName} (not a friendly port or no sea route)" : $"No sea route to {loc.DisplayName}");
            return;
        }
        if (Ui.SelectedDivisions.Count > 0)
        {
            int ok = 0;
            foreach (var d in Ui.SelectedDivisions)
            {
                if (d.Army != null && d.Army.FrontEnemy != null) d.FrontPost = null;
                if (G.Warfare.OrderMove(d, loc)) ok++;
            }
            Ui.SetStatus(ok > 0 ? $"{ok} division(s) moving to {loc.DisplayName}" : $"No path to {loc.DisplayName}");
        }
        else if (Ui.SelectedArmy != null)
        {
            G.Warfare.MoveArmy(Ui.SelectedArmy, loc);
            Ui.SetStatus($"{Ui.SelectedArmy.Name} moving to {loc.DisplayName}");
        }
        else if (loc.Owner != null)
        {
            Ui.SelectedCountry = loc.Owner;
            Ui.Left = LeftPanel.Country;
        }
    }

    // ===============================================================================================
    // Frame
    // ===============================================================================================
    private int _lastDay = -1;

    private MapMode? _modeBeforeCountryView;

    public void Draw()
    {
        // the state of the selected location is highlighted on the map
        if (!Ui.EditorActive)
        {
            var hs = Ui.Left == LeftPanel.Location ? Ui.SelectedLocation?.State : null;
            if (Map.HighlightState != hs) { Map.HighlightState = hs; Map.Dirty = true; }
        }
        // viewing a country: the map shows its territory, allies (blue), enemies (red) and subjects
        bool viewing = Ui.Left == LeftPanel.Country && Ui.SelectedCountry != null && Has(FeatureSet.Diplomacy) && !Ui.EditorActive;
        if (viewing && Map.DiplomacyFocus != Ui.SelectedCountry)
        {
            if (_modeBeforeCountryView == null) _modeBeforeCountryView = Map.Mode;
            Map.DiplomacyFocus = Ui.SelectedCountry;
            Map.Mode = MapMode.Diplomacy;
            Map.CustomMode = null;
            Map.Dirty = true;
        }
        else if (!viewing && _modeBeforeCountryView != null)
        {
            Map.Mode = _modeBeforeCountryView.Value;
            _modeBeforeCountryView = null;
            Map.DiplomacyFocus = null;
            Map.Dirty = true;
        }
        // map modes that change every day (supply) refresh daily
        if (G.World.Date.TotalDays != _lastDay) { _lastDay = G.World.Date.TotalDays; if (Map.Mode is MapMode.Supply or MapMode.Air) Map.Dirty = true; }
        DrawTopBar();
        if (Ui.EditorActive) DrawEditor();
        else
        {
            DrawLeftMenu();
            switch (Ui.Left)
            {
                case LeftPanel.Location: DrawLocationPanel(); break;
                case LeftPanel.Country: DrawCountryPanel(); break;
                case LeftPanel.Military: DrawMilitaryPanel(); break;
                case LeftPanel.Recruit: DrawRecruitPanel(); break;
                case LeftPanel.Characters: DrawCharactersPanel(); break;
                case LeftPanel.Decisions: DrawDecisionsPanel(); break;
                case LeftPanel.Custom: DrawCustomPanel(); break;
                case LeftPanel.Production: DrawProductionPanel(); break;
                case LeftPanel.Designer: DrawDesignerPanel(); break;
                case LeftPanel.Navy: DrawNavyPanel(); break;
                case LeftPanel.Air: DrawAirPanel(); break;
            }
            DrawSupportDialog();
        }
        switch (Ui.Right)
        {
            case RightPanel.Wars: DrawWarsPanel(); break;
            case RightPanel.Peace: DrawPeacePanel(); break;
            case RightPanel.World: DrawWorldPanel(); break;
        }
        DrawMapModes();
        DrawSelectedDivisions();
        DrawNotifications();
        DrawStatus();
        if (Ui.ErrorsOpen) DrawErrorLog();
        DrawProposals();
        DrawEvents();
        if (Ui.ConsoleOpen) DrawConsole();
        if (Ui.SaveMenuOpen) DrawSaveMenu();
        if (Ui.PauseMenuOpen) DrawPauseMenu();
    }

    // ===============================================================================================
    // Top bar: your country and resources on the left, world-wide things (wars, world, time) on the right
    // ===============================================================================================
    private void DrawTopBar()
    {
        int sw = Raylib.GetScreenWidth();
        var bar = new Rectangle(0, 0, sw, TopH);
        Gui.Block(bar);
        Gui.TopBarBackground(bar);
        float x = 8 * S;
        var player = G.World.Player;
        if (player != null)
        {
            var flagR = new Rectangle(x, 6 * S, 51 * S, 34 * S);
            Gui.Flag(player, flagR);
            if (Gui.Hover(flagR)) Gui.Tooltip("Open your country (diplomacy, ruler)");
            if (Gui.Clicked(flagR)) { Ui.SelectedCountry = player; Ui.Left = LeftPanel.Country; }
            x += 58 * S;
            Gui.LabelShadow(player.Name, x, 3 * S, 18, Gui.Gold, FontKind.Header);
            Gui.Label(player.Ruler != null ? player.Ruler.DisplayName : "", x, 25 * S, 14, Gui.TextDim);
            x += Math.Max(150 * S, Gui.Measure(player.Name, 18, FontKind.Header).X + 16);
            Separator(ref x);
            if (Has(FeatureSet.Economy))
            {
                double net = player.LastIncome - player.LastExpenses;
                Stat(ref x, "Gold", $"{player.Gold:0}", $"{(net >= 0 ? "+" : "")}{net:0.0}",
                    $"Treasury: {player.Gold:0.0}\nIncome: +{player.LastIncome:0.0}/month\nArmy upkeep: -{player.LastExpenses:0.0}/month", net >= 0 ? Gui.Good : Gui.Bad);
                Stat(ref x, "Manpower", $"{player.Manpower:0}", $"+{player.LastManpowerGain:0}", "Manpower: used to train and reinforce divisions\nMonthly gain shown in green", Gui.Good);
            }
            if (Has(FeatureSet.Military) && !Has(FeatureSet.Economy))
                Stat(ref x, "Manpower", $"{player.Manpower:0}", $"+{player.LastManpowerGain:0}", "Manpower: used to train and reinforce divisions\nMonthly gain shown in green", Gui.Good);
            if (Has(FeatureSet.Production))
            {
                int civ = G.Production.CivilianFactories(player), mil = G.Production.MilitaryFactories(player), dock = G.Production.Dockyards(player);
                Stat(ref x, "Factories", $"{civ} / {mil}" + (dock > 0 ? $" / {dock}" : ""), "",
                    $"Civilian factories: {civ} (construction and consumer goods)\nMilitary factories: {mil} (equipment)" + (dock > 0 ? $"\nDockyards: {dock} (ships)" : "") + "\n\nProduction menu: P", Gui.TextDim);
            }
            if (Has(FeatureSet.Military))
            {
                int divs = G.World.Divisions.Count(d => d.Owner == player);
                Stat(ref x, "Divisions", divs.ToString(), player.Recruitment.Count > 0 ? $"+{player.Recruitment.Count}" : "",
                    $"{divs} divisions, {player.Recruitment.Count} in training\nMilitary menu: F1, recruit: R", Gui.TextDim);
            }
            DrawResources(ref x, player);
        }
        else
        {
            Gui.LabelShadow("Observer mode", x, 10 * S, 20, Gui.Gold, FontKind.Header);
            x += 170 * S;
        }

        // right side: date, speed and the world-wide buttons
        float rx = sw - 8 * S;
        var clock = G.Clock;
        string date = G.World.Date.ToDisplay(G.Clock.Speed <= 1);
        float dw = Math.Max(190 * S, Gui.Measure(date, 18, FontKind.Bold).X + 20 * S);
        rx -= dw;
        var dateR = new Rectangle(rx, 6 * S, dw, 34 * S);
        Gui.ButtonFrame(dateR, true, Gui.Hover(dateR), false);
        if (clock.Paused) Gui.Gradient(new Rectangle(dateR.X + 1, dateR.Y + 1, dateR.Width - 2, dateR.Height - 2), new Color(150, 52, 40, 255), new Color(90, 26, 20, 255));
        Gui.LabelCentered(clock.Paused ? "PAUSED" : date, dateR, 18, clock.Paused ? Color.White : Gui.Text, FontKind.Bold);
        if (Gui.Hover(dateR)) Gui.Tooltip($"{date}\nSpace: pause/unpause, 1-5: speed\nRunning at {clock.ActualDaysPerSecond:0.#} days per second");
        Gui.Block(dateR);
        if (Gui.Clicked(dateR)) clock.Paused = !clock.Paused;
        rx -= 5 * 18 * S + 10 * S;
        for (int i = 1; i <= 5; i++)
        {
            // speed pips: filled up to the current speed
            var r = new Rectangle(rx + (i - 1) * 18 * S, 13 * S, 15 * S, 20 * S);
            bool on = i <= clock.Speed;
            bool hover = Gui.Hover(r);
            Gui.Gradient(r, on ? Gui.Lighter(Gui.Gold, 0.2f) : new Color(60, 58, 54, 255), on ? Gui.Darker(Gui.Gold, 0.35f) : new Color(30, 29, 27, 255));
            Raylib.DrawRectangleLinesEx(r, 1, hover ? Color.White : new Color(0, 0, 0, 200));
            Gui.Block(r);
            if (hover) Gui.Tooltip($"Speed {i}");
            if (Gui.Clicked(r)) { clock.Speed = i; clock.Paused = false; }
        }
        rx -= 8 * S;
        if (player != null && Has(FeatureSet.Diplomacy))
        {
            int wars = G.World.WarsOf(player).Count();
            int allWars = G.World.Wars.Count;
            string wl = allWars > 0 ? $"Wars ({allWars})" : "Wars";
            float ww = Math.Max(78 * S, Gui.Measure(wl, 15, FontKind.Bold).X + 18 * S);
            rx -= ww;
            if (Gui.Button(new Rectangle(rx, 8 * S, ww, 30 * S), wl, true,
                    (wars > 0 ? $"You are in {wars} war(s)." : "You are at peace.") + $"\n{allWars} war(s) in the world.\nWars and peace deals (F4)", Ui.Right is RightPanel.Wars or RightPanel.Peace, 15))
                ToggleRight(RightPanel.Wars);
            if (wars > 0) Badge(new Vector2(rx + ww - 2 * S, 9 * S), wars.ToString(), Gui.Bad);
            rx -= 4 * S;
        }
        float wdw = 78 * S;
        rx -= wdw;
        if (Gui.Button(new Rectangle(rx, 8 * S, wdw, 30 * S), "World", true, "All countries of the world (F8)", Ui.Right == RightPanel.World, 15))
            ToggleRight(RightPanel.World);
    }

    private void Separator(ref float x)
    {
        Raylib.DrawLineEx(new Vector2(x - 6 * S, 7 * S), new Vector2(x - 6 * S, TopH - 7 * S), 1, new Color(Gui.PanelInner.R, Gui.PanelInner.G, Gui.PanelInner.B, (byte)200));
        x += 6 * S;
    }

    private void Stat(ref float x, string label, string value, string sub, string tooltip, Color subColor)
    {
        float w = Math.Max(92 * S, Math.Max(Gui.Measure(label, 13).X, Gui.Measure(value, 18, FontKind.Bold).X + Gui.Measure(sub, 13).X + 6 * S) + 14 * S);
        var r = new Rectangle(x, 4 * S, w, 38 * S);
        Gui.Label(label, x, 4 * S, 13, Gui.TextDim);
        Gui.Label(value, x, 19 * S, 18, Gui.Text, FontKind.Bold);
        if (sub.Length > 0) Gui.Label(sub, x + Gui.Measure(value, 18, FontKind.Bold).X + 5 * S, 22 * S, 13, subColor);
        if (Gui.Hover(r)) Gui.Tooltip(tooltip);
        x += w;
    }

    /// <summary>A small number in a circle (counts on buttons).</summary>
    private void Badge(Vector2 center, string text, Color col)
    {
        float r = Math.Max(8 * S, Gui.Measure(text, 12, FontKind.Bold).X / 2 + 4 * S);
        Raylib.DrawCircleV(center, r + 1, new Color(0, 0, 0, 200));
        Raylib.DrawCircleV(center, r, col);
        var m = Gui.Measure(text, 12, FontKind.Bold);
        Gui.Label(text, center.X - m.X / 2, center.Y - m.Y / 2, 12, Color.White, FontKind.Bold);
    }

    // ===============================================================================================
    // Map modes (bottom right)
    // ===============================================================================================
    private float MapModeBarH => 34 * S;

    private void DrawMapModes()
    {
        var modes = new List<(string Name, MapMode Mode)> { ("Political", MapMode.Political), ("Terrain", MapMode.Terrain), ("States", MapMode.States) };
        if (Has(FeatureSet.Diplomacy)) modes.Add(("Diplomacy", MapMode.Diplomacy));
        if (G.Features.HasSupply && Has(FeatureSet.Military)) modes.Add(("Supply", MapMode.Supply));
        if (Has(FeatureSet.Air)) modes.Add(("Air", MapMode.Air));
        modes.Add(("Locations", MapMode.Locations));
        var custom = G.Interface.MapModes;
        int total = modes.Count + custom.Count;
        float bw = 84 * S, gap = 3 * S, pad = 6 * S;
        float maxW = Raylib.GetScreenWidth() * 0.5f;
        if (total * (bw + gap) + 2 * pad > maxW) bw = Math.Max(54 * S, (maxW - 2 * pad) / total - gap);
        float w = total * (bw + gap) - gap + 2 * pad;
        var r = new Rectangle(Raylib.GetScreenWidth() - w - 8 * S, Raylib.GetScreenHeight() - MapModeBarH - 8 * S, w, MapModeBarH);
        Gui.Block(r);
        Gui.Frame(r, Gui.MenuBar, false);
        float x = r.X + pad, y = r.Y + 5 * S, h = r.Height - 10 * S;
        foreach (var (name, mode) in modes)
        {
            if (Gui.Button(new Rectangle(x, y, bw, h), name, true, $"{name} map mode (Tab cycles)", Map.CustomMode == null && Map.Mode == mode, 14))
            { Map.Mode = mode; Map.CustomMode = null; Map.Dirty = true; }
            x += bw + gap;
        }
        foreach (var m in custom)
        {
            if (Gui.Button(new Rectangle(x, y, bw, h), m.Name, true, $"Map mode: {m.Name}", Map.CustomMode == m, 14)) { Map.CustomMode = m; Map.Dirty = true; }
            x += bw + gap;
        }
    }

    // ===============================================================================================
    // Notifications & status
    // ===============================================================================================
    private void DrawNotifications()
    {
        var list = G.Notifications;
        int n = Math.Min(6, list.Count);
        float w = 420 * S, lineH = 22 * S;
        float x = Raylib.GetScreenWidth() - w - 8 * S;
        float y = Raylib.GetScreenHeight() - MapModeBarH - 16 * S - n * lineH;
        for (int i = list.Count - n; i < list.Count; i++)
        {
            var note = list[i];
            int age = G.World.Date.TotalDays - note.Date.TotalDays;
            if (age > 60) { y += lineH; continue; }
            var col = note.Kind switch
            {
                NotificationKind.Good => Gui.Good,
                NotificationKind.Bad => Gui.Bad,
                NotificationKind.War => new Color(240, 140, 90, 255),
                NotificationKind.Diplomacy => new Color(140, 190, 250, 255),
                NotificationKind.Character => new Color(220, 190, 250, 255),
                _ => Gui.Text,
            };
            var r = new Rectangle(x, y, w, lineH - 2);
            Gui.Gradient(r, new Color(8, 8, 10, 120), new Color(8, 8, 10, 200));
            Raylib.DrawRectangle((int)x, (int)y, (int)(3 * S), (int)(lineH - 2), col);
            Gui.Label(Gui.Fit(note.Text, w - 16 * S, 14), x + 9 * S, y + 3 * S, 14, col);
            if (Gui.Hover(r))
            {
                Gui.Tooltip($"{note.Date.ToDisplay()}\n{note.Text}" + (note.Location != null ? "\n(click to view)" : ""));
                if (note.Location != null && Gui.Clicked(r)) Cam.LookAt(Map.LocationPos(note.Location), 30);
                Gui.Block(r);
            }
            y += lineH;
        }
    }

    private void DrawStatus()
    {
        if (Ui.StatusTime <= 0 || Ui.Status.Length == 0) return;
        Ui.StatusTime -= Raylib.GetFrameTime();
        var m = Gui.Measure(Ui.Status, 18, FontKind.Bold);
        float x = (Raylib.GetScreenWidth() - m.X) / 2, y = TopH + 12 * S;
        var r = new Rectangle(x - 14 * S, y - 5 * S, m.X + 28 * S, m.Y + 10 * S);
        Gui.Frame(r, new Color(14, 14, 18, 220), false);
        Gui.Label(Ui.Status, x, y, 18, Gui.Gold, FontKind.Bold);
    }

    // ===============================================================================================
    // Selected divisions bar
    // ===============================================================================================
    private void DrawSelectedDivisions()
    {
        Ui.SelectedDivisions.RemoveAll(d => !G.World.Divisions.Contains(d));
        if (Ui.SelectedDivisions.Count == 0) return;
        float w = Math.Min(760 * S, Raylib.GetScreenWidth() - 900 * S);
        w = Math.Max(w, 520 * S);
        float h = 118 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, Raylib.GetScreenHeight() - h - 8 * S, w, h);
        Gui.Panel(r, $"{Ui.SelectedDivisions.Count} division(s) selected - right-click the map to move/attack");
        float y = r.Y + 32 * S;
        float x = r.X + 8 * S;
        foreach (var d in Ui.SelectedDivisions.Take(6))
        {
            var row = new Rectangle(x, y, 118 * S, 44 * S);
            Raylib.DrawRectangleRec(row, new Color(36, 40, 48, 255));
            Gui.Label(d.Name.Length > 16 ? d.Name[..15] + "." : d.Name, x + 4, y + 2, 13);
            Gui.Bar(new Rectangle(x + 4, y + 21 * S, 108 * S, 7 * S), (float)d.StrengthFraction, new Color(90, 200, 90, 255));
            Gui.Bar(new Rectangle(x + 4, y + 31 * S, 108 * S, 7 * S), (float)d.OrgFraction, new Color(80, 160, 240, 255));
            if (Gui.Hover(row))
                Gui.Tooltip($"{d.Name} ({d.Template.Name})\nLocation: {d.Location.DisplayName}\nStrength {d.Strength:0}/{d.MaxStrength:0}\nOrganisation {d.Org:0.0}/{d.MaxOrg:0}\n" +
                            $"Army: {d.Army?.Name ?? "none"}\n" + string.Join("\n", d.Template.Stats.Select(kv => $"{Localisation.GetOrPretty(kv.Key)}: {kv.Value:0.##}")));
            x += 122 * S;
        }
        if (Ui.SelectedDivisions.Count > 6) Gui.Label($"+{Ui.SelectedDivisions.Count - 6}", x + 4, y + 12 * S, 16);
        float by = r.Y + h - 34 * S;
        float bx = r.X + 8 * S;
        if (Gui.Button(new Rectangle(bx, by, 90 * S, 28 * S), "Stop", true, "Cancel movement (Delete)")) foreach (var d in Ui.SelectedDivisions) G.Warfare.StopDivision(d);
        bx += 96 * S;
        if (Gui.Button(new Rectangle(bx, by, 150 * S, 28 * S), "New army", true, "Put the selected divisions into a new army"))
        {
            var player = G.World.Player!;
            var army = G.Warfare.CreateArmy(player);
            foreach (var d in Ui.SelectedDivisions) G.Warfare.AssignToArmy(d, army);
            Ui.SelectedArmy = army;
            Ui.Left = LeftPanel.Military;
            Ui.SetStatus($"Created {army.Name}");
        }
        bx += 156 * S;
        var armies = G.World.ArmiesOf(G.World.Player!).ToList();
        foreach (var a in armies.Take(3))
        {
            if (Gui.Button(new Rectangle(bx, by, 130 * S, 28 * S), "To " + (a.Name.Length > 11 ? a.Name[..10] + "." : a.Name), true, $"Add to {a.Name}"))
                foreach (var d in Ui.SelectedDivisions) G.Warfare.AssignToArmy(d, a);
            bx += 134 * S;
        }
    }
}
