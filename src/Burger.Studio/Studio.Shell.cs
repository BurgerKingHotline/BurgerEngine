using System.Numerics;
using System.Runtime.InteropServices;
using Burger.Core;
using ImGuiNET;
using Raylib_cs;

namespace Burger.Studio;

/// <summary>Main menu, dock layout, modal dialogs and the project hub.</summary>
public sealed partial class Studio
{
    // =============================================================================================
    // Dock layout (imgui_internal DockBuilder, exported by cimgui)
    // =============================================================================================

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern uint igDockBuilderAddNode(uint nodeId, int flags);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern void igDockBuilderRemoveNode(uint nodeId);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern unsafe uint igDockBuilderSplitNode(uint nodeId, int dir, float ratio, uint* outAtDir, uint* outOpposite);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern void igDockBuilderDockWindow([MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint nodeId);
    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)] private static extern void igDockBuilderFinish(uint nodeId);

    private const int DockNodeFlagsDockSpace = 1 << 10;

    private static unsafe (uint AtDir, uint Other) Split(uint node, ImGuiDir dir, float ratio)
    {
        uint a, b;
        igDockBuilderSplitNode(node, (int)dir, ratio, &a, &b);
        return (a, b);
    }

    private void BuildLayout(uint dock)
    {
        try
        {
            var vp = ImGui.GetMainViewport();
            igDockBuilderRemoveNode(dock);
            igDockBuilderAddNode(dock, DockNodeFlagsDockSpace);
            igDockBuilderSetNodeSize(dock, vp.WorkSize);
            // Viewport in the middle, Inspector on the right, Content Browser + Console at the bottom.
            // The Outliner (View menu) docks with the Inspector when opened.
            var (right, center) = Split(dock, ImGuiDir.Right, 0.26f);
            var (bottom, top) = Split(center, ImGuiDir.Down, 0.30f);
            igDockBuilderDockWindow(WOutliner, right);
            igDockBuilderDockWindow(WViewport, top);
            igDockBuilderDockWindow(WCode, top);
            igDockBuilderDockWindow(WInspector, right);
            igDockBuilderDockWindow(WSettings, right);
            igDockBuilderDockWindow(WContent, bottom);
            igDockBuilderDockWindow(WConsole, bottom);
            igDockBuilderFinish(dock);
            Later(() => ImGui.SetWindowFocus(WViewport));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not build the default layout: " + ex.Message);
        }
    }

    // =============================================================================================
    // Main menu bar
    // =============================================================================================

    private void DrawMainMenu()
    {
        if (!ImGui.BeginMainMenuBar()) return;
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Project hub...")) { if (Store?.AnyDirty == true) AskSaveThen("Close project", CloseProject); else CloseProject(); }
            if (ImGui.MenuItem("Save all", "Ctrl+S", false, Store != null)) SaveAll();
            if (ImGui.MenuItem("Reload files from disk", "", false, Store != null))
                ShowChoice("Reload", "Discard unsaved changes and reload every file from disk?", new[] { "Reload", "Cancel" }, i => { if (i == 0) { Store!.ReloadAll(); LoadWorld(); } });
            ImGui.Separator();
            if (ImGui.MenuItem("Show game folder in Explorer", "", false, Store != null)) Engine.OpenInExplorer(Store!.GameDir);
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) RequestQuit();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Project", Store != null))
        {
            if (ImGui.MenuItem("Play", "F5")) Play(null);
            if (Selected is SelBlock { Ref.Spec: var sp } sb && sp == Specs.Countries && ImGui.MenuItem($"Play as {sb.Ref.Id}")) Play(sb.Ref.Id);
            if (ImGui.MenuItem("Check for errors", "F7")) Check();
            if (ImGui.MenuItem("Check + simulate one year")) { SaveAll(); RunTool("validate", Store!.ProjectDir, "--days", "365"); }
            if (ImGui.MenuItem("Export as EXE...")) Export();
            ImGui.Separator();
            if (ImGui.MenuItem("Features...")) ShowFeatures = true;
            Ui.Tip("Add or remove mechanics: land military, combat models, fog of war, diplomacy, characters, economy, AI...");
            if (ImGui.MenuItem("Project settings...")) { _showSettings = true; ImGui.SetWindowFocus(WSettings); }
            if (ImGui.MenuItem("Reload the world (map, countries...)")) LoadWorld();
            if (ImGui.MenuItem("Name new province colors")) GenerateLocationNames();
            if (ImGui.MenuItem("Write script reference (docs)")) RunTool("docs", Store!.ProjectDir);
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem(WViewport, "", ref _showViewport);
            ImGui.MenuItem(WOutliner, "", ref _showOutliner);
            ImGui.MenuItem(WInspector, "", ref _showInspector);
            ImGui.MenuItem(WContent, "", ref _showContent);
            ImGui.MenuItem(WCode, "", ref _showCode);
            ImGui.MenuItem(WConsole, "", ref _showConsole);
            ImGui.MenuItem(WSettings, "", ref _showSettings);
            ImGui.Separator();
            if (ImGui.MenuItem("Reset layout")) _resetLayout = true;
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("Modding guide")) Engine.OpenInExplorer(Path.Combine(Engine.DocsDir, "MODDING.md"));
            if (ImGui.MenuItem("Script reference")) Engine.OpenInExplorer(Path.Combine(Engine.DocsDir, "SCRIPT_REFERENCE.md"));
            if (ImGui.MenuItem("Engine README")) Engine.OpenInExplorer(Path.Combine(Engine.Root, "README.md"));
            ImGui.Separator();
            ImGui.MenuItem("Viewport: left click select, middle drag / WASD pan, wheel zoom", "", false, false);
            ImGui.MenuItem("Drag files from Windows onto the window to import them", "", false, false);
            ImGui.EndMenu();
        }

        if (Store != null)
        {
            ImGui.Separator();
            if (ImGui.Button("  Play  ")) Play(null);
            Ui.Tip("Save and start the game (F5)");
            if (ImGui.Button("Check")) Check();
            Ui.Tip("Load everything and list errors (F7)");
            if (ImGui.Button(Store.AnyDirty ? "Save*" : "Save")) SaveAll();
            Ui.Tip("Save all changed files (Ctrl+S)");
            if (ImGui.Button("Export")) Export();
            Ui.Tip("Build a standalone EXE players can run and mod");
            ImGui.Separator();
            Ui.T(Store.Name, Ui.Gold);
        }

        // status on the right
        string status = _checking ? "Checking..." : _running.Count > 0 ? "Working... " + _status : _status;
        float w = ImGui.CalcTextSize(status).X + 20;
        ImGui.SameLine(ImGui.GetWindowWidth() - w);
        bool recent = Raylib.GetTime() - _statusTime < 6;
        Ui.T(status, recent ? Ui.Gold : Ui.Dim);
        ImGui.EndMainMenuBar();
    }

    // =============================================================================================
    // Modal dialogs
    // =============================================================================================

    private sealed class Modal
    {
        public string Title = "";
        public string Message = "";
        public string Input = "";
        public bool HasInput;
        public Func<string, string?>? Validate;
        public Action<string>? OnOk;
        public string[]? Buttons;
        public Action<int>? OnButton;
        public string[]? Choices;
        public int Choice;
        public string ChoiceLabel = "";
        public Action<string, int>? OnOkChoice;
        public string Error = "";
        public bool Opened;
    }

    private Modal? _modal;
    private readonly Queue<Modal> _modalQueue = new();

    public void Prompt(string title, string message, string initial, Func<string, string?>? validate, Action<string> onOk)
        => _modalQueue.Enqueue(new Modal { Title = title, Message = message, Input = initial, HasInput = true, Validate = validate, OnOk = onOk });

    public void PromptWithChoice(string title, string message, string initial, Func<string, string?>? validate, string choiceLabel, string[] choices, Action<string, int> onOk)
        => _modalQueue.Enqueue(new Modal { Title = title, Message = message, Input = initial, HasInput = true, Validate = validate, Choices = choices, ChoiceLabel = choiceLabel, OnOkChoice = onOk });

    public void ShowChoice(string title, string message, string[] buttons, Action<int> onButton)
        => _modalQueue.Enqueue(new Modal { Title = title, Message = message, Buttons = buttons, OnButton = onButton });

    public void ShowMessage(string title, string message) => ShowChoice(title, message, new[] { "OK" }, _ => { });

    public void Confirm(string title, string message, Action onYes) => ShowChoice(title, message, new[] { "Yes", "Cancel" }, i => { if (i == 0) onYes(); });

    private void DrawModals()
    {
        if (_modal == null && _modalQueue.Count > 0) _modal = _modalQueue.Dequeue();
        var m = _modal;
        if (m == null) return;
        string id = m.Title + "##modal";
        if (!m.Opened) { ImGui.OpenPopup(id); m.Opened = true; }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(380, 0), new Vector2(900, 700));
        bool open = true;
        if (!ImGui.BeginPopupModal(id, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open) _modal = null;
            return;
        }
        if (m.Message.Length > 0) { ImGui.PushTextWrapPos(560); Ui.T(m.Message); ImGui.PopTextWrapPos(); }
        bool submit = false;
        if (m.HasInput)
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.SetNextItemWidth(420);
            submit = ImGui.InputText("##modalinput", ref m.Input, 512, ImGuiInputTextFlags.EnterReturnsTrue);
            if (m.Choices != null)
            {
                Ui.T(m.ChoiceLabel, Ui.Dim);
                ImGui.SetNextItemWidth(420);
                ImGui.Combo("##modalchoice", ref m.Choice, m.Choices, m.Choices.Length);
            }
            if (m.Error.Length > 0) Ui.T(m.Error, Ui.Red);
            ImGui.Spacing();
            if (ImGui.Button("OK", new Vector2(100, 0)) || submit)
            {
                string v = m.Input.Trim();
                var err = m.Validate?.Invoke(v);
                if (err != null) m.Error = err;
                else
                {
                    ImGui.CloseCurrentPopup();
                    _modal = null;
                    m.OnOk?.Invoke(v);
                    m.OnOkChoice?.Invoke(v, m.Choice);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) { ImGui.CloseCurrentPopup(); _modal = null; }
        }
        else
        {
            ImGui.Spacing();
            var buttons = m.Buttons ?? new[] { "OK" };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                if (ImGui.Button(buttons[i], new Vector2(110, 0)))
                {
                    ImGui.CloseCurrentPopup();
                    _modal = null;
                    m.OnButton?.Invoke(i);
                }
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { ImGui.CloseCurrentPopup(); _modal = null; m.OnButton?.Invoke(buttons.Length - 1); }
        }
        ImGui.EndPopup();
    }

    // =============================================================================================
    // Project hub (no project open)
    // =============================================================================================

    private string _newName = "MyGame";
    private int _newTemplate;
    private Task<int>? _creating;
    private string _browsePath = "";

    private void DrawHub()
    {
        var vp = ImGui.GetMainViewport();
        var size = new Vector2(900, 640);
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("Burger Studio##hub", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings);
        Ui.Header("BURGER STUDIO");
        Ui.T("Make grand strategy games. Open a project or create a new one.", Ui.Dim);
        ImGui.Separator();

        if (ImGui.BeginTable("hubtable", 2, ImGuiTableFlags.None))
        {
            ImGui.TableSetupColumn("a", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("b", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Ui.Header("Projects");
            ImGui.BeginChild("projects", new Vector2(0, 440), ImGuiChildFlags.Borders);
            foreach (var p in Engine.ListProjects())
            {
                string name = Path.GetFileName(p);
                if (ImGui.Selectable(name + "##" + p, false, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0, 26)) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    OpenProject(p);
                if (ImGui.IsItemHovered()) Ui.Tip(p + "\nDouble-click to open");
            }
            var recent = RecentProjects.Get().Where(r => !r.StartsWith(Engine.ProjectsDir, StringComparison.OrdinalIgnoreCase)).ToList();
            if (recent.Count > 0)
            {
                ImGui.Separator();
                Ui.T("Recent (outside projects/):", Ui.Dim);
                foreach (var r in recent)
                    if (ImGui.Selectable(r, false, ImGuiSelectableFlags.AllowDoubleClick) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenProject(r);
            }
            ImGui.EndChild();
            Ui.T("Open a folder:", Ui.Dim);
            ImGui.SetNextItemWidth(-80);
            ImGui.InputTextWithHint("##path", "C:\\path\\to\\MyGame", ref _browsePath, 400);
            ImGui.SameLine();
            if (ImGui.Button("Open"))
            {
                var p = Engine.ResolveProject(_browsePath.Trim().Trim('"'));
                if (p == null) ShowMessage("Not a project", "That folder has no game/ folder or game.txt.");
                else OpenProject(p);
            }

            ImGui.TableNextColumn();
            DrawNewProjectWizard();
            ImGui.EndTable();
        }
        ImGui.End();
    }
}
