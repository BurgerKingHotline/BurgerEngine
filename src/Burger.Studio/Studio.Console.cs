using System.Numerics;
using Burger.Core;
using Burger.Core.Data;
using Burger.Core.Mechanics;
using Burger.Core.Script;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>Console (problems, log, tool output) and Project Settings.</summary>
public sealed partial class Studio
{
    private readonly List<LogEntry> _problems = new();
    private int _consoleTab;
    private bool _focusConsole;
    private bool _logWarningsOnly = true;

    private void DrawConsole()
    {
        if (_focusConsole) { ImGui.SetNextWindowFocus(); _focusConsole = false; }
        if (!ImGui.Begin(WConsole, ref _showConsole)) { ImGui.End(); return; }
        TrackHover(WConsole);
        if (ImGui.BeginTabBar("consoletabs"))
        {
            int errors = _problems.Count(p => p.Level == LogLevel.Error);
            if (Ui.TabItem($"Problems ({errors} errors, {_problems.Count - errors} warnings)###problems", ImGuiTabItemFlags.None | (_consoleTab == 0 ? ImGuiTabItemFlags.SetSelected : 0)))
            {
                if (ImGui.SmallButton("Check again (F7)")) Check();
                ImGui.SameLine();
                Ui.T(_problems.Count == 0 ? "No problems from the last check. Press Check (F7) after changing files." : "Click a problem to open the file at that line.", Ui.Dim);
                ImGui.BeginChild("problist");
                if (ImGui.BeginTable("probs", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 64);
                    ImGui.TableSetupColumn("Problem", ImGuiTableColumnFlags.WidthStretch, 4);
                    ImGui.TableSetupColumn("Where", ImGuiTableColumnFlags.WidthStretch, 1.4f);
                    int i = 0;
                    foreach (var p in _problems)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        Ui.T(p.Level == LogLevel.Error ? "Error" : "Warning", p.Level == LogLevel.Error ? Ui.Red : Ui.Yellow);
                        ImGui.TableNextColumn();
                        if (ImGui.Selectable(p.Message.Split('\n')[0] + "##p" + i++, false, ImGuiSelectableFlags.SpanAllColumns)) OpenProblem(p.Where);
                        Ui.Tip(p.Message);
                        ImGui.TableNextColumn();
                        Ui.T(p.Where ?? "", Ui.Dim);
                    }
                    ImGui.EndTable();
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (Ui.TabItem("Log###log", ImGuiTabItemFlags.None | (_consoleTab == 1 ? ImGuiTabItemFlags.SetSelected : 0)))
            {
                ImGui.Checkbox("Only warnings and errors", ref _logWarningsOnly);
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear")) Log.Clear();
                ImGui.BeginChild("loglist");
                foreach (var e in Log.Entries.Reverse().Take(500))
                {
                    if (_logWarningsOnly && e.Level == LogLevel.Info) continue;
                    Ui.T(e.ToString(), e.Level == LogLevel.Error ? Ui.Red : e.Level == LogLevel.Warning ? Ui.Yellow : Ui.Dim);
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (Ui.TabItem("Output###output", ImGuiTabItemFlags.None | (_consoleTab == 2 ? ImGuiTabItemFlags.SetSelected : 0)))
            {
                if (ImGui.SmallButton("Clear")) _output.Clear();
                ImGui.SameLine();
                Ui.T("Output of Play, Export and the burger tool.", Ui.Dim);
                ImGui.BeginChild("outlist", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
                if (HasCodeFont) ImGui.PushFont(CodeFont);
                foreach (var l in _output) Ui.T(l);
                if (HasCodeFont) ImGui.PopFont();
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20) ImGui.SetScrollHereY(1);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            _consoleTab = -1;
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void OpenProblem(string? where)
    {
        if (string.IsNullOrEmpty(where)) return;
        int colon = where.LastIndexOf(':');
        string file = where;
        int line = 0;
        if (colon > 1 && int.TryParse(where[(colon + 1)..], out var l)) { file = where[..colon]; line = l; }
        if (Path.IsPathRooted(file)) { try { file = Store!.Rel(file); } catch { return; } }
        if (!Store!.Exists(file)) { SetStatus("Not a file of this project: " + where); return; }
        OpenFile(file, line);
    }

    // =============================================================================================
    // Project Settings
    // =============================================================================================

    private string _settingsFilter = "";
    private string _newTextKey = "", _newTextValue = "";

    private void DrawSettings()
    {
        if (!ImGui.Begin(WSettings, ref _showSettings)) { ImGui.End(); return; }
        TrackHover(WSettings);
        if (ImGui.BeginTabBar("settingstabs"))
        {
            if (ImGui.BeginTabItem("Game")) { SettingsGame(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Rules")) { SettingsDefines(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Formulas")) { SettingsFormulas(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Texts")) { SettingsTexts(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void SettingsGame()
    {
        var root = Store!.Parse("game.txt");
        foreach (var (key, label) in new[] { ("name", "Game name"), ("version", "Version"), ("author", "Author"), ("description", "Description"), ("icon", "Window icon (png)") })
        {
            string cur = root.GetString(key) ?? "";
            string v = Buffer("game." + key, cur);
            ImGui.SetNextItemWidth(-140);
            if (key == "description") ImGui.InputTextMultiline(label + "##g", ref v, 4000, new Vector2(-140, 70));
            else ImGui.InputText(label + "##g", ref v, 400);
            SetBuffer("game." + key, v);
            if (ImGui.IsItemDeactivatedAfterEdit() && v.Trim() != cur)
            {
                string val = v.Trim();
                Store.Edit("game.txt", t => val.Length == 0 ? ScriptPatch.Remove(t, "game.txt", new[] { key }) : ScriptPatch.Set(t, "game.txt", new[] { key }, new Node(key, val) { ValueQuoted = true }), this);
            }
        }
        string def = root.GetString("default_country") ?? "";
        ImGui.SetNextItemWidth(-140);
        if (Ui.ComboFilter("Default country##g", ref def, Index!.Tags))
            Store.Edit("game.txt", t => def.Length == 0 ? ScriptPatch.Remove(t, "game.txt", new[] { "default_country" }) : ScriptPatch.SetValue(t, "game.txt", new[] { "default_country" }, def), this);
        ImGui.Separator();
        Ui.Header("Combat");
        var defines = DefinesRoot();
        string model = defines.GetString("combat_model") ?? "hoi4";
        var models = (LastCheckedGame ?? _world)?.Combat.Models.Select(m => m.Id).ToList() ?? new List<string> { "hoi4", "eu4", "imperator" };
        ImGui.SetNextItemWidth(200);
        if (Ui.ComboFilter("Combat model", ref model, models, null)) SetDefine("combat_model", model);
        Ui.Help("hoi4, eu4 and imperator are built in. C# scripts can register more (see C# script > Custom combat model). Press Check to list models from scripts.");
        int occ = defines.GetInt("occupation_hours", 0);
        int occIdx = occ <= 0 ? 0 : occ <= 48 ? 1 : 2;
        string[] occOpts = { "Instant when entered (HOI4)", "Sieges, 24 hours per fort level", "Sieges, 72 hours per fort level" };
        ImGui.SetNextItemWidth(280);
        if (ImGui.Combo("Taking land", ref occIdx, occOpts, 3)) SetDefine("occupation_hours", occIdx switch { 1 => "24", 2 => "72", _ => "0" });
        var speeds = defines.GetList("game_speeds");
        string sp = Buffer("speeds", string.Join(" ", speeds.Count == 5 ? speeds : new List<string> { "0.5", "1.5", "4", "10", "0" }));
        ImGui.SetNextItemWidth(280);
        ImGui.InputText("Game speeds (days/second)", ref sp, 64);
        SetBuffer("speeds", sp);
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var parts = sp.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 5 && parts.All(x => Node.TryParseDouble(x, out _))) SetDefine("game_speeds", "{ " + string.Join(" ", parts) + " }");
            else SetStatus("Game speeds: enter 5 numbers, e.g. 0.5 1.5 4 10 0 (0 = as fast as possible)");
        }
    }

    private Node DefinesRoot()
    {
        var root = Node.Root("defines");
        foreach (var f in Store!.FilesOf("common/defines")) root.Children!.AddRange(Store.Parse(f).All);
        return root;
    }

    private void SetDefine(string key, string value)
    {
        var files = Store!.FilesOf("common/defines");
        var file = files.FirstOrDefault(f => Store.Parse(f).Get(key) != null) ?? files.FirstOrDefault() ?? "common/defines.txt";
        if (!Store.Exists(file)) Store.Set(file, "# Game rules\n", this);
        Store.Edit(file, t => ScriptPatch.SetValue(t, file, new[] { key }, value), this);
        SetStatus($"{key} = {value} (not saved yet)");
    }

    private void SettingsDefines()
    {
        Ui.Wrapped("Numbers and switches the engine uses. Grey rows use the engine default - type a value to set it. Blocks like { a b c } can be typed directly.", Ui.Dim);
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##dfilter", "Search...", ref _settingsFilter, 64);
        var defines = DefinesRoot();
        if (!ImGui.BeginTable("defines", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Rule", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        var known = Defines.Known.Select(k => k.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = Defines.Known.Select(k => (k.Key, k.Default, k.Help)).ToList();
        foreach (var n in defines.All)
            if (n.Op != Op.None && !known.Contains(n.Key) && (n.Value != null || n.Children != null)) rows.Add((n.Key, "", "(custom - read it from C# with Defines.Get)"));
        foreach (var (key, def, help) in rows)
        {
            if (_settingsFilter.Length > 0 && !key.Contains(_settingsFilter, StringComparison.OrdinalIgnoreCase) && !help.Contains(_settingsFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var n = defines.Get(key);
            string cur = n == null ? "" : n.Value ?? ("{ " + string.Join(" ", n.Items()) + " }");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Ui.T(key, n == null ? Ui.Dim : Ui.Gold);
            ImGui.TableNextColumn();
            string v = Buffer("def." + key, cur);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##" + key, def, ref v, 256);
            SetBuffer("def." + key, v);
            if (ImGui.IsItemDeactivatedAfterEdit() && v.Trim() != cur)
            {
                string val = v.Trim();
                if (val.Length == 0)
                {
                    var file = Store!.FilesOf("common/defines").FirstOrDefault(f => Store.Parse(f).Get(key) != null);
                    if (file != null) Store.Edit(file, t => ScriptPatch.Remove(t, file, new[] { key }), this);
                }
                else SetDefine(key, val.Any(char.IsWhiteSpace) && !val.StartsWith('{') && !val.StartsWith('"') ? ScriptWriter.Quote(val, true) : val);
            }
            ImGui.TableNextColumn();
            Ui.T(help, Ui.Dim);
        }
        ImGui.EndTable();
    }

    private const string FormulaFile = "common/script_values/engine_formulas.txt";

    private static readonly Dictionary<string, string> FormulaTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["country_monthly_income"] = "value = 0\nevery_owned_state = { add = tax }          # tax of every state\nmultiply = { value = 1 add = modifier:income }   # income modifiers",
        ["country_monthly_expenses"] = "value = num_of_divisions\nmultiply = 0.16                              # gold per division per month",
        ["country_monthly_manpower"] = "value = 0\nevery_owned_state = { add = manpower }",
        ["max_diplomatic_relations"] = "value = 3\nadd = modifier:relations",
        ["character_yearly_death_chance"] = "value = 0.004\nif = { limit = { age > 50 } add = { value = age subtract = 50 multiply = 0.006 } }",
        ["character_yearly_birth_chance"] = "value = 0.15\nif = { limit = { age > 40 } multiply = 0.3 }",
        ["division_combat_power"] = "value = 1\nadd = { value = num_of_allies multiply = 0.02 }   # example: +2% per ally",
        ["division_org_recovery"] = "value = 0.08",
        ["location_war_value"] = "value = 1\nadd = fort",
        ["base_opinion"] = "value = 0\nif = { limit = { is_at_war_with = scope:target } add = -50 }",
    };

    private void SettingsFormulas()
    {
        Ui.Wrapped("A script value with one of these names replaces the engine's own formula - no C# needed. " +
                   "'Replace' writes a starting point into " + FormulaFile + ". For bigger changes create a C# script (Content Browser > Create > C# script).", Ui.Dim);
        if (!ImGui.BeginTable("formulas", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Formula", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("Decides", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableHeadersRow();
        foreach (var (name, desc) in ScriptValueSystem.Formulas)
        {
            var found = Catalog!.Find(Specs.ScriptValues, name);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Ui.T(name, found != null ? Ui.Green : Ui.Dim);
            ImGui.TableNextColumn();
            ImGui.PushTextWrapPos(0);
            Ui.T(desc);
            ImGui.PopTextWrapPos();
            ImGui.TableNextColumn();
            ImGui.PushID(name);
            if (found != null)
            {
                if (ImGui.SmallButton("Edit")) SelectBlock(found);
                ImGui.SameLine();
                if (ImGui.SmallButton("Default")) Confirm("Engine default", $"Remove {name} from {found.File}? The engine formula is used again.", () =>
                {
                    Store!.Edit(found.File, t => ScriptPatch.Remove(t, found.File, found.Path), this);
                    Catalog.Invalidate();
                });
            }
            else if (ImGui.SmallButton("Replace"))
            {
                string body = FormulaTemplates.TryGetValue(name, out var b) ? b : "value = 1";
                string text = $"# {desc}\n{name} = {{\n\t{body.Replace("\n", "\n\t")}\n}}";
                if (!Store!.Exists(FormulaFile)) Store.Set(FormulaFile, "# Engine formulas replaced by this game. Delete a block to get the engine default back.\n", this);
                Store.Edit(FormulaFile, t => ScriptPatch.AppendStatement(t, text), this);
                Catalog.Invalidate();
                if (Catalog.Find(Specs.ScriptValues, name) is { } nb) SelectBlock(nb);
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private void SettingsTexts()
    {
        Ui.Wrapped("Display texts (localisation): key = \"text\". Any name or text in the game files is looked up here first.", Ui.Dim);
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##nk", "new key", ref _newTextKey, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##nv", "text", ref _newTextValue, 512);
        ImGui.SameLine();
        if (ImGui.Button("Add") && _newTextKey.Trim().Length > 0 && !_newTextKey.Any(char.IsWhiteSpace))
        {
            Index!.SetText(_newTextKey.Trim(), _newTextValue, this);
            _settingsFilter = _newTextKey.Trim();
            _newTextKey = _newTextValue = "";
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##tfilter", "Search...", ref _settingsFilter, 64);
        if (!ImGui.BeginTable("texts", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Text", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("File", ImGuiTableColumnFlags.WidthStretch, 0.8f);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();
        int shown = 0;
        foreach (var file in Store!.FilesOf("localisation"))
        {
            foreach (var n in Store.Parse(file).All)
            {
                if (n.Value == null) continue;
                if (_settingsFilter.Length > 0 && !n.Key.Contains(_settingsFilter, StringComparison.OrdinalIgnoreCase) && !n.Value.Contains(_settingsFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (++shown > 1500) break;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Ui.T(n.Key);
                ImGui.TableNextColumn();
                string v = Buffer("txt." + file + n.Key, n.Value);
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##" + file + n.Key, ref v, 2048);
                SetBuffer("txt." + file + n.Key, v);
                if (ImGui.IsItemDeactivatedAfterEdit() && v != n.Value)
                {
                    string key = n.Key, val = v;
                    Store.Edit(file, t => ScriptPatch.Set(t, file, new[] { key }, new Node(key, val) { ValueQuoted = true }), this);
                }
                ImGui.TableNextColumn();
                Ui.T(file, Ui.Dim);
            }
        }
        ImGui.EndTable();
    }
}
