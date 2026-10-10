using System.Collections.Concurrent;
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Runtime;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Burger.Studio;

public abstract class Selection { }
public sealed class SelBlock : Selection { public BlockRef Ref = null!; }
public sealed class SelLocation : Selection { public Location Loc = null!; }
public sealed class SelState : Selection { public State State = null!; }
public sealed class SelFile : Selection { public string Rel = ""; }

/// <summary>
/// Burger Studio: the editor, built into the engine with Dear ImGui. Dockable panels like Unity/Unreal:
/// Outliner, Viewport (the live 3D map), Inspector, Content Browser, Code, Console and Project Settings.
/// </summary>
public sealed partial class Studio
{
    public const string WOutliner = "Outliner", WViewport = "Viewport", WInspector = "Inspector", WContent = "Content Browser",
        WConsole = "Console", WCode = "Code", WSettings = "Project Settings";

    private readonly StudioOptions _opt;
    public ProjectStore? Store { get; private set; }
    public ProjectIndex? Index { get; private set; }
    public Catalog? Catalog { get; private set; }
    public Selection? Selected;

    private bool _quit;
    private bool _focusViewport;
    private int _frame;
    private bool _resetLayout;
    private string _status = "Ready";
    private double _statusTime;
    private readonly ConcurrentQueue<string> _outputQueue = new();
    private readonly List<string> _output = new();
    private readonly List<Task> _running = new();
    private string _hoveredWindow = "";
    private Action? _nextFrame;

    // window visibility (View menu)
    private bool _showOutliner = false, _showViewport = true, _showInspector = true, _showContent = true, _showConsole = true, _showCode = true, _showSettings;

    public Studio(StudioOptions opt) { _opt = opt; }

    /// <summary>Monospace font for the code editor (if found).</summary>
    public static ImFontPtr CodeFont;
    public static bool HasCodeFont;

    public string GameDir => Store!.GameDir;

    // =============================================================================================
    // Main loop
    // =============================================================================================

    public void Run()
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory); // imgui.ini lives next to the exe
        // rebuild the default layout on first run, or when the default layout changed in a new version
        const string layoutVersion = "2";
        _resetLayout = !File.Exists("imgui.ini") || !File.ReadAllText("imgui.ini").Contains("[Docking]")
                       || !File.Exists("layout_version.txt") || File.ReadAllText("layout_version.txt").Trim() != layoutVersion;
        try { File.WriteAllText("layout_version.txt", layoutVersion); } catch { /* read-only folder */ }

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(_opt.Width, _opt.Height, "Burger Studio");
        Raylib.SetWindowMinSize(1024, 640);
        Raylib.SetExitKey(KeyboardKey.Null);
        Raylib.SetTargetFPS(60);
        if (_opt.Windowed) Raylib.SetWindowPosition(0, 32);
        else if (_opt.Screenshot == null) Raylib.MaximizeWindow();

        rlImGui.LoadFontAwesome = false;
        rlImGui.SetupUserFonts = io =>
        {
            foreach (var f in new[] { @"C:\Windows\Fonts\segoeui.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf" })
            {
                if (!File.Exists(f)) continue;
                var font = io.Fonts.AddFontFromFileTTF(f, 17f);
                unsafe { io.NativePtr->FontDefault = font.NativePtr; }
                break;
            }
            foreach (var f in new[] { @"C:\Windows\Fonts\consola.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf" })
            {
                if (!File.Exists(f)) continue;
                CodeFont = io.Fonts.AddFontFromFileTTF(f, 16f);
                HasCodeFont = true;
                break;
            }
        };
        rlImGui.Setup(true, true);
        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        ImGui.GetIO().ConfigWindowsMoveFromTitleBarOnly = true;
        Ui.ApplyTheme();

        if (_opt.Project != null)
        {
            var p = Engine.ResolveProject(_opt.Project);
            if (p != null) OpenProject(p);
            else SetStatus($"Project '{_opt.Project}' not found");
        }

        while (!_quit)
        {
            if (Raylib.WindowShouldClose()) RequestQuit();
            if (_nextFrame != null) { var a = _nextFrame; _nextFrame = null; a(); }
            while (_outputQueue.TryDequeue(out var line)) { _output.Add(line); if (_output.Count > 4000) _output.RemoveRange(0, 1000); }
            _running.RemoveAll(t => t.IsCompleted);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(20, 21, 25, 255));
            // very quick clicks (touchpad taps) can press and release within one frame; tell ImGui about them
            var io0 = ImGui.GetIO();
            for (int b = 0; b < 3; b++)
                if (Raylib.IsMouseButtonPressed((MouseButton)b) && !Raylib.IsMouseButtonDown((MouseButton)b)) io0.AddMouseButtonEvent(b, true);
            rlImGui.Begin();
            try { Frame(); }
            catch (Exception ex)
            {
                Log.Error("Studio: " + ex.Message + "\n" + ex.StackTrace);
                SetStatus("Error: " + ex.Message);
            }
            DrawModals();
            rlImGui.End();
            Raylib.EndDrawing();

            _frame++;
            if (_opt.Screenshot != null && _frame == _opt.ScreenshotFrame)
            {
                var img = Raylib.LoadImageFromScreen();
                Raylib.ExportImage(img, _opt.Screenshot);
                Raylib.UnloadImage(img);
                _quit = true;
            }
        }
        DisposeWorld();
        Textures.Clear();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    private void Frame()
    {
        HandleShortcuts();
        HandleDroppedFiles();
        DrawMainMenu();
        if (Store == null)
        {
            DrawHub();
            return;
        }
        uint dock = ImGui.GetID("BurgerDockSpace");
        if (_resetLayout)
        {
            BuildLayout(dock);
            _resetLayout = false;
        }
        ImGui.DockSpaceOverViewport(dock, ImGui.GetMainViewport(), ImGuiDockNodeFlags.None);

        if (_showViewport) DrawViewport();
        if (_showOutliner) DrawOutliner();
        if (_showInspector) DrawInspector();
        if (_showContent) DrawContentBrowser();
        if (_showConsole) DrawConsole();
        if (_showCode) DrawCode();
        if (_showSettings) DrawSettings();
        DrawFeaturesWindow();
        ApplyTestOptions();
    }

    private void RequestQuit()
    {
        if (Store != null && Store.AnyDirty)
        {
            AskSaveThen("Quit", () => _quit = true);
            return;
        }
        _quit = true;
    }

    /// <summary>Runs an action at the start of the next frame (outside any ImGui window).</summary>
    public void Later(Action a)
    {
        var prev = _nextFrame;
        _nextFrame = prev == null ? a : () => { prev(); a(); };
    }

    public void SetStatus(string s)
    {
        _status = s;
        _statusTime = Raylib.GetTime();
    }

    public void Output(string line) => _outputQueue.Enqueue(line);

    /// <summary>Remembers which panel the mouse is over (for dropped files).</summary>
    private void TrackHover(string window)
    {
        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) _hoveredWindow = window;
    }

    // =============================================================================================
    // Project
    // =============================================================================================

    public void OpenProject(string projectDir)
    {
        DisposeWorld();
        Store = new ProjectStore(projectDir);
        Index = new ProjectIndex(Store);
        Catalog = new Catalog(Store, Index);
        Store.Changed += OnStoreChanged;
        Selected = null;
        _openFiles.Clear();
        _browseDir = "";
        Raylib.SetWindowTitle($"Burger Studio - {Store.Name}");
        RecentProjects.Add(projectDir);
        Later(() => { LoadWorld(); _focusViewport = true; });
        SetStatus($"Opened {Store.Name}");
    }

    private void CloseProject()
    {
        DisposeWorld();
        Store = null;
        Index = null;
        Catalog = null;
        Selected = null;
        _openFiles.Clear();
        Raylib.SetWindowTitle("Burger Studio");
    }

    public void SaveAll()
    {
        if (Store == null) return;
        try
        {
            var dirty = Store.DirtyFiles.ToList();
            int n = Store.SaveAll();
            SetStatus(n == 0 ? "Nothing to save" : $"Saved {n} file(s)");
            if (dirty.Any(f => f.StartsWith("map/", StringComparison.OrdinalIgnoreCase) && !_worldOwnFiles.Contains(f))) _worldStale = true;
            _worldOwnFiles.Clear();
        }
        catch (Exception ex)
        {
            ShowMessage("Could not save", ex.Message);
        }
    }

    private void AskSaveThen(string what, Action then)
    {
        var files = string.Join("\n", Store!.DirtyFiles.Take(15));
        ShowChoice(what, "Save changes first?\n\n" + files, new[] { "Save", "Don't save", "Cancel" }, i =>
        {
            if (i == 0) { SaveAll(); then(); }
            else if (i == 1) { Store.ReloadAll(); then(); }
        });
    }

    // =============================================================================================
    // Commands
    // =============================================================================================

    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        bool ctrl = io.KeyCtrl;
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.S, false)) SaveAll();
        if (Store != null && ImGui.IsKeyPressed(ImGuiKey.F5, false)) Play(null);
        if (Store != null && ImGui.IsKeyPressed(ImGuiKey.F7, false)) Check();
    }

    public void Play(string? tag)
    {
        if (Store == null) return;
        SaveAll();
        Engine.StartGame(Store.GameDir, Output, tag);
        SetStatus("Game started in its own window (it reads the saved files)");
    }

    public void Export()
    {
        if (Store == null) return;
        string title = Store.Parse("game.txt").GetString("name") ?? Store.Name;
        string def = new(title.Where(char.IsLetterOrDigit).ToArray());
        if (def.Length == 0) def = "Game";
        Prompt("Export as EXE", "Name of the EXE:", def, v => v.Length == 0 || !v.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') ? "Use letters, digits, _ and -" : null, name =>
        {
            SaveAll();
            RunTool("export", Store.ProjectDir, "--name", name);
        });
    }

    public void RunTool(params string[] args)
    {
        _consoleTab = 2;
        _showConsole = true;
        Output("> burger " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
        SetStatus($"Running burger {args[0]}...");
        var t = Engine.RunToolAsync(args, Output).ContinueWith(r =>
        {
            Output(r.Result == 0 ? $"burger {args[0]}: done" : $"burger {args[0]} failed (exit code {r.Result})");
            Output("");
        });
        _running.Add(t);
    }

    /// <summary>Saves, loads the whole game (with C# scripts) and lists every problem.</summary>
    public void Check()
    {
        if (Store == null) return;
        SaveAll();
        SetStatus("Checking the project...");
        _checking = true;
        Later(() => Later(DoCheck)); // let one frame draw the "checking" message first
    }

    private bool _checking;
    public Game? LastCheckedGame { get; private set; }

    private void DoCheck()
    {
        _checking = false;
        Log.Clear();
        _problems.Clear();
        try
        {
            LastCheckedGame = Game.Load(Store!.GameDir, compileScripts: true, seed: 1);
        }
        catch (Exception ex)
        {
            _problems.Add(new LogEntry(LogLevel.Error, ex.Message, "", DateTime.Now));
        }
        _problems.AddRange(Log.Entries.Where(e => e.Level != LogLevel.Info).OrderBy(e => e.Level == LogLevel.Error ? 0 : 1));
        int errors = _problems.Count(p => p.Level == LogLevel.Error), warnings = _problems.Count - errors;
        _consoleTab = 0;
        _showConsole = true;
        _focusConsole = true;
        SetStatus(errors + warnings == 0 ? "No problems found" : $"{errors} error(s), {warnings} warning(s)");
        // the check replaced the global localisation; reload the world's so names stay right
        if (_world != null) Localisation.Clear();
        if (_world != null) Localisation.LoadDirectory(Path.Combine(Store.GameDir, "localisation"), Store.GameDir);
    }

    // =============================================================================================
    // Dropped files (drag from Windows Explorer onto the window)
    // =============================================================================================

    private void HandleDroppedFiles()
    {
        if (!Raylib.IsFileDropped()) return;
        var files = Raylib.GetDroppedFiles();
        if (Store == null || files.Length == 0) return;
        if (_hoveredWindow == WInspector && TryDropOnInspector(files)) return;
        ImportFiles(files, _browseDir.Length > 0 ? _browseDir : "gfx");
    }

    /// <summary>Copies files into a folder of the game.</summary>
    public void ImportFiles(IEnumerable<string> files, string folderRel)
    {
        int n = 0;
        foreach (var f in files)
        {
            try
            {
                if (Directory.Exists(f)) continue;
                string dest = Store!.Abs(folderRel + "/" + Path.GetFileName(f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
                n++;
            }
            catch (Exception ex) { Log.Warn($"Could not import {f}: {ex.Message}"); }
        }
        _browseDir = ProjectStore.Norm(folderRel);
        _showContent = true;
        SetStatus($"Imported {n} file(s) into {folderRel}");
    }

    // =============================================================================================
    // Test options (automated screenshots)
    // =============================================================================================

    private bool _testApplied;

    private void ApplyTestOptions()
    {
        if (_testApplied || _frame < 3) return;
        if (Store == null)
        {
            // a self-test without a project must not wait on the start screen forever
            if (_opt.SelfTest && _frame > 30)
            {
                _testApplied = true;
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "studio_selftest.txt"), $"[FAIL] project not found: {_opt.Project}\n1 FAILED\n");
                _quit = true;
            }
            return;
        }
        _testApplied = true;
        if (_opt.SelfTest) { RunSelfTest(); _quit = true; return; }
        if (_opt.ShowFeatures) ShowFeatures = true;
        if (_opt.Browse != null) _browseDir = ProjectStore.Norm(_opt.Browse);
        if (_opt.OpenFile != null)
        {
            // file or file:line
            int c = _opt.OpenFile.LastIndexOf(':');
            if (c > 1 && int.TryParse(_opt.OpenFile[(c + 1)..], out int ln)) OpenFile(_opt.OpenFile[..c], ln);
            else OpenFile(_opt.OpenFile, 0);
        }
        if (_opt.ShowSettings) { _showSettings = true; ImGui.SetWindowFocus(WSettings); }
        if (_opt.Select != null)
        {
            // kind:id   e.g.  countries:VAL   resources:prestige   location:pelastad
            var parts = _opt.Select.Split(':', 2);
            if (parts.Length == 2)
            {
                if (parts[0] == "location" && _world?.World.GetLocation(parts[1]) is { } loc) { Selected = new SelLocation { Loc = loc }; FocusOn(loc); }
                else
                {
                    var kind = Catalog.Kinds.FirstOrDefault(k => k.Title.Replace(" ", "").Replace("&", "").Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    var b = kind != null ? Catalog!.Find(kind, parts[1]) : null;
                    if (b != null) Selected = new SelBlock { Ref = b };
                }
            }
        }
    }
}

/// <summary>Recently opened projects (stored next to the exe).</summary>
public static class RecentProjects
{
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "recent_projects.txt");

    public static List<string> Get()
    {
        try { return File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(Directory.Exists).Distinct().Take(10).ToList() : new List<string>(); }
        catch { return new List<string>(); }
    }

    public static void Add(string dir)
    {
        try
        {
            var list = Get();
            list.RemoveAll(d => string.Equals(Path.GetFullPath(d), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
            list.Insert(0, dir);
            File.WriteAllLines(FilePath, list.Take(10));
        }
        catch { /* not important */ }
    }
}

/// <summary>Textures for previews (flags, portraits, thumbnails), reloaded when the file changes.</summary>
public static class Textures
{
    private static readonly Dictionary<string, (Texture2D Tex, DateTime Time)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static Texture2D? Get(string absPath, int maxSize = 256)
    {
        try
        {
            if (!File.Exists(absPath)) return null;
            var t = File.GetLastWriteTimeUtc(absPath);
            string key = absPath + "|" + maxSize;
            if (_cache.TryGetValue(key, out var e) && e.Time == t) return e.Tex;
            if (_cache.Count > 300) Clear();
            var img = Raylib.LoadImage(absPath);
            if (img.Width == 0) return null;
            if (img.Width > maxSize || img.Height > maxSize)
            {
                float s = Math.Min(maxSize / (float)img.Width, maxSize / (float)img.Height);
                Raylib.ImageResize(ref img, Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s)));
            }
            var tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            if (_cache.TryGetValue(key, out var old)) Raylib.UnloadTexture(old.Tex);
            _cache[key] = (tex, t);
            return tex;
        }
        catch { return null; }
    }

    public static void Clear()
    {
        foreach (var e in _cache.Values) Raylib.UnloadTexture(e.Tex);
        _cache.Clear();
    }

    /// <summary>Draws a texture fitted into a box.</summary>
    public static void Show(Texture2D tex, float maxW, float maxH)
    {
        float s = Math.Min(maxW / tex.Width, maxH / tex.Height);
        rlImGui.ImageSize(tex, new Vector2(tex.Width * s, tex.Height * s));
    }
}
