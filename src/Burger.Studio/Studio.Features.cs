using System.Numerics;
using Burger.Core.Modding;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>
/// Features (mechanics packages): the new-project wizard on the start screen and the Project > Features window,
/// where features are added to or removed from the open project.
/// </summary>
public sealed partial class Studio
{
    private List<FeaturePackage>? _packages;
    private List<FeaturePreset>? _presets;
    private List<(string Id, string Name, string Description, string Dir)>? _bases;
    private List<FeaturePackage> Packages => _packages ??= FeaturePackages.All();
    private List<FeaturePreset> Presets => _presets ??= FeaturePackages.Presets();
    private List<(string Id, string Name, string Description, string Dir)> Bases => _bases ??= FeaturePackages.Bases();

    private string? _featText;
    private HashSet<string>? _featCache;

    /// <summary>Does the open project use a feature? (true for projects made before features existed)</summary>
    public bool ProjectHas(string feature)
    {
        if (Store == null) return false;
        if (!Store.Exists("features.txt")) return true;
        string text = Store.Get("features.txt");
        if (text != _featText)
        {
            _featText = text;
            _featCache = FeatureSet.ReadIds(Store.Parse("features.txt")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return _featCache!.Contains(feature);
    }

    /// <summary>The feature a kind of content belongs to (null = core, always available).</summary>
    public static string? FeatureOf(BlockSpec k)
        => k == Specs.UnitTypes || k == Specs.DivisionTemplates ? FeatureSet.Military
         : k == Specs.Characters || k == Specs.Traits ? FeatureSet.Characters
         : k == Specs.CharacterInteractions ? FeatureSet.CharacterInteractions
         : k == Specs.DiplomaticActions || k == Specs.CasusBelli || k == Specs.SubjectTypes ? FeatureSet.Diplomacy
         : k == Specs.Equipment ? FeatureSet.Production
         : k == Specs.ShipTypes ? FeatureSet.Navy
         : k == Specs.PlaneTypes ? FeatureSet.Air
         : k == Specs.Ideologies || k == Specs.Governments ? FeatureSet.Politics
         : k == Specs.LawGroups ? FeatureSet.Laws
         : k == Specs.CabinetPosts ? FeatureSet.Cabinet
         : k == Specs.CultureGroups ? FeatureSet.Population
         : k == Specs.ReligionGroups ? FeatureSet.Religion
         : k == Specs.PopTypes ? FeatureSet.SocialClasses
         : null;

    public bool KindAvailable(BlockSpec k)
    {
        if (k == Specs.Buildings) return ProjectHas(FeatureSet.Production) || ProjectHas(FeatureSet.Navy) || ProjectHas(FeatureSet.Air) || ProjectHas(FeatureSet.SupplyRange) || ProjectHas(FeatureSet.SupplyHubs);
        return FeatureOf(k) is not { } f || ProjectHas(f);
    }

    // ---------------------------------------------------------------------------------------- wizard state
    private int _newStart;          // 0 = base map + features, 1 = full sample game
    private int _newBase;
    private int _newPreset = -1;
    private readonly HashSet<string> _newFeatures = new(StringComparer.OrdinalIgnoreCase);

    private void ApplyPreset(int index)
    {
        _newPreset = index;
        _newFeatures.Clear();
        if (index >= 0 && index < Presets.Count) foreach (var f in Presets[index].Features) _newFeatures.Add(f);
    }

    /// <summary>Feature checkboxes grouped by category; alternatives (same group) are radio buttons. Returns true if changed.</summary>
    private bool FeatureChecklist(HashSet<string> selected, string idPrefix)
    {
        bool changed = false;
        foreach (var cat in Packages.GroupBy(p => p.Category))
        {
            Ui.Header(cat.Key);
            ImGui.Indent(8);
            foreach (var p in cat.Where(p => p.Group == null))
            {
                bool on = selected.Contains(p.Id);
                var missing = p.Requires.Where(r => !selected.Contains(r)).ToList();
                if (ImGui.Checkbox($"{p.Name}##{idPrefix}{p.Id}", ref on))
                {
                    if (on) foreach (var id in FeaturePackages.WithRequirements(new[] { p.Id })) selected.Add(id);
                    else RemoveWithDependents(selected, p.Id);
                    changed = true;
                }
                Ui.Tip(p.Description + (p.Requires.Count > 0 ? $"\n\nNeeds: {string.Join(", ", p.Requires)}" : ""));
            }
            foreach (var group in cat.Where(p => p.Group != null).GroupBy(p => p.Group!))
            {
                // a choice: none or one of the alternatives (several can be installed; the define picks the active one)
                var options = group.ToList();
                bool anyOn = options.Any(o => selected.Contains(o.Id));
                bool reqOk = options[0].Requires.All(selected.Contains);
                ImGui.BeginDisabled(!reqOk);
                Ui.T($"{Cap(group.Key)}:", Ui.Dim);
                ImGui.SameLine();
                if (ImGui.RadioButton($"none##{idPrefix}{group.Key}", !anyOn)) { foreach (var o in options) selected.Remove(o.Id); changed = true; }
                foreach (var o in options)
                {
                    ImGui.Indent(16);
                    if (ImGui.RadioButton($"{o.Name}##{idPrefix}{o.Id}", selected.Contains(o.Id)))
                    {
                        foreach (var x in options) selected.Remove(x.Id);
                        selected.Add(o.Id);
                        changed = true;
                    }
                    Ui.Tip(o.Description);
                    ImGui.Unindent(16);
                }
                ImGui.EndDisabled();
                if (!reqOk) { ImGui.SameLine(); Ui.T($"(needs {string.Join(", ", options[0].Requires)})", Ui.Dim); }
            }
            ImGui.Unindent(8);
        }
        return changed;
    }

    private void RemoveWithDependents(HashSet<string> selected, string id)
    {
        selected.Remove(id);
        foreach (var p in Packages.Where(p => selected.Contains(p.Id) && p.Requires.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList())
            RemoveWithDependents(selected, p.Id);
    }

    /// <summary>The "New project" column of the start screen.</summary>
    private void DrawNewProjectWizard()
    {
        Ui.Header("New project");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##newname", ref _newName, 64);
        ImGui.RadioButton("Pick a map and features", ref _newStart, 0);
        Ui.Tip("Start from a map and choose the mechanics you want - from a blank slate to a preset inspired by HOI4, EU, Imperator or CK.");
        ImGui.SameLine();
        ImGui.RadioButton("Full sample game", ref _newStart, 1);
        Ui.Tip("A copy of the sample game: every feature plus example content (characters, armies, events, a court window, a C# script).");
        if (_newStart == 0)
        {
            if (_newPreset < 0 && Presets.Count > 0) ApplyPreset(0);
            ImGui.SetNextItemWidth(-1);
            string baseName = Bases.Count == 0 ? "(engine assets missing)" : Bases[Math.Clamp(_newBase, 0, Bases.Count - 1)].Name;
            if (ImGui.BeginCombo("##base", "Map: " + baseName))
            {
                for (int i = 0; i < Bases.Count; i++)
                {
                    if (ImGui.Selectable(Bases[i].Name, i == _newBase)) _newBase = i;
                    Ui.Tip(Bases[i].Description);
                }
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(-1);
            string presetName = _newPreset >= 0 && _newPreset < Presets.Count ? Presets[_newPreset].Name : "Custom";
            if (ImGui.BeginCombo("##preset", "Preset: " + presetName))
            {
                for (int i = 0; i < Presets.Count; i++)
                {
                    if (ImGui.Selectable(Presets[i].Name, i == _newPreset)) ApplyPreset(i);
                    Ui.Tip(Presets[i].Description + "\n\nFeatures: " + (Presets[i].Features.Count == 0 ? "none" : string.Join(", ", Presets[i].Features)));
                }
                ImGui.EndCombo();
            }
            if (_newPreset >= 0 && _newPreset < Presets.Count) Ui.Wrapped(Presets[_newPreset].Description, Ui.Dim);
            ImGui.BeginChild("newfeatures", new Vector2(0, 250), ImGuiChildFlags.Borders);
            FeatureChecklist(_newFeatures, "new");
            ImGui.EndChild();
        }
        else Ui.Wrapped("The sample game is a complete small game (map, countries, characters, armies, events, mechanics, a C# script) - the best way to learn.", Ui.Dim);

        bool busy = _creating != null && !_creating.IsCompleted;
        ImGui.BeginDisabled(busy);
        if (ImGui.Button(busy ? "Creating..." : "Create project", new Vector2(-1, 34)))
        {
            string n = _newName.Trim();
            if (n.Length == 0 || n.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-'))) ShowMessage("Name", "Use only letters, digits, _ and -.");
            else if (Directory.Exists(Path.Combine(Engine.ProjectsDir, n))) ShowMessage("Name", "A project with that name already exists.");
            else
            {
                var args = new List<string> { "new", n };
                if (_newStart == 0)
                {
                    if (Bases.Count > 0) { args.Add("--base"); args.Add(Bases[Math.Clamp(_newBase, 0, Bases.Count - 1)].Id); }
                    args.Add("--preset");
                    args.Add(_newPreset >= 0 && _newPreset < Presets.Count ? Presets[_newPreset].Id : "blank");
                    args.Add("--features");
                    // keep the engine's order (requirements first)
                    args.Add(_newFeatures.Count == 0 ? "," : string.Join(",", Packages.Where(p => _newFeatures.Contains(p.Id)).Select(p => p.Id)));
                }
                Output("> burger " + string.Join(" ", args));
                _creating = Engine.RunToolAsync(args, Output);
                string created = Path.Combine(Engine.ProjectsDir, n);
                _creating.ContinueWith(t => { if (t.Result == 0) Later(() => OpenProject(created)); else Later(() => ShowMessage("Could not create the project", string.Join("\n", _output.TakeLast(12)))); });
            }
        }
        ImGui.EndDisabled();
    }

    // ---------------------------------------------------------------------------------------- Features window
    public bool ShowFeatures;
    private string _customFeature = "";

    private void DrawFeaturesWindow()
    {
        if (!ShowFeatures || Store == null) return;
        ImGui.SetNextWindowSize(new Vector2(760, 640), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Features###features", ref ShowFeatures, ImGuiWindowFlags.NoDocking)) { ImGui.End(); return; }
        var installed = FeaturePackages.Installed(Store.ProjectDir);
        Ui.Wrapped("Features are the mechanics of your game. Adding one copies its files (C# in the backend, data in game/) into the project: " +
                   "they are yours to change. Removing one deletes its unchanged files; files you changed are moved to removed_features/.", Ui.Dim);
        if (installed == null)
        {
            ImGui.Spacing();
            Ui.Wrapped("This project was made before features existed: every built-in system runs. Convert it to manage features one by one " +
                       "(nothing is removed - all built-in features are recorded as installed).", Ui.Yellow);
            if (ImGui.Button("Convert to features")) RunFeatureChange(() => { FeatureSet.Save(Store.GameDir, FeatureSet.BuiltIn); return new List<string> { "Recorded all built-in features in game/features.txt" }; });
            ImGui.End();
            return;
        }
        ImGui.Separator();
        string combat = DefinesRoot().GetString("combat_model") ?? "hoi4";
        if (ImGui.BeginTable("feat", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp, new Vector2(0, -90)))
        {
            ImGui.TableSetupColumn("Feature", ImGuiTableColumnFlags.WidthStretch, 1.1f);
            ImGui.TableSetupColumn("What it adds", ImGuiTableColumnFlags.WidthStretch, 2.2f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            foreach (var cat in Packages.GroupBy(p => p.Category))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Ui.Header(cat.Key);
                foreach (var p in cat)
                {
                    bool on = installed.Contains(p.Id, StringComparer.OrdinalIgnoreCase);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.AlignTextToFramePadding();
                    Ui.T((on ? "[x] " : "[ ] ") + p.Name, on ? Ui.Green : Ui.Dim);
                    ImGui.TableNextColumn();
                    ImGui.PushTextWrapPos(0);
                    Ui.T(p.Description, on ? Ui.Dim : new Vector4(0.5f, 0.5f, 0.52f, 1));
                    if (p.Requires.Count > 0) Ui.T("Needs: " + string.Join(", ", p.Requires), Ui.Dim);
                    ImGui.PopTextWrapPos();
                    ImGui.TableNextColumn();
                    ImGui.PushID(p.Id);
                    if (!on)
                    {
                        if (ImGui.Button("Add", new Vector2(-1, 0)))
                        {
                            var needs = FeaturePackages.WithRequirements(new[] { p.Id }).Where(x => !installed.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
                            // an alternative that conflicts with an installed one (e.g. the other supply mode) replaces it
                            var replaces = p.Conflicts.Where(c => installed.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
                            string msg = $"Add {p.Name}?" + (needs.Count > 1 ? $"\n\nIt also adds what it needs: {string.Join(", ", needs.Where(x => x != p.Id))}." : "") +
                                         (replaces.Count > 0 ? $"\n\nIt replaces: {string.Join(", ", replaces.Select(r => Packages.FirstOrDefault(x => x.Id == r)?.Name ?? r))} (changed files are kept in removed_features/)." : "") +
                                         $"\n\nFiles: {string.Join(", ", p.Files.Take(8))}{(p.Files.Count > 8 ? "..." : "")}";
                            Confirm("Add feature", msg, () => RunFeatureChange(() =>
                            {
                                var log = new List<string>();
                                foreach (var r in replaces) log.AddRange(FeaturePackages.Uninstall(Store!.ProjectDir, r));
                                log.AddRange(FeaturePackages.Install(Store!.ProjectDir, new[] { p.Id }));
                                return log;
                            }));
                        }
                    }
                    else
                    {
                        if (p.Group == "combat")
                        {
                            string model = p.Defines.FirstOrDefault(d => d.Key == "combat_model").Value ?? "";
                            bool active = model.Equals(combat, StringComparison.OrdinalIgnoreCase);
                            ImGui.BeginDisabled(active);
                            if (ImGui.Button(active ? "Active" : "Use", new Vector2(-1, 0))) SetDefine("combat_model", model);
                            ImGui.EndDisabled();
                            Ui.Tip("The combat model used in battles (combat_model in common/defines.txt).");
                        }
                        if (ImGui.Button("Remove", new Vector2(-1, 0)))
                        {
                            var deps = FeaturePackages.Dependents(Store.ProjectDir, p.Id);
                            string msg = $"Remove {p.Name}?" + (deps.Count > 0 ? $"\n\nThese features need it and are removed too: {string.Join(", ", deps)}." : "") +
                                         "\n\nIts files are deleted if you did not change them. Changed files are moved to removed_features/ so no work is lost.";
                            Confirm("Remove feature", msg, () => RunFeatureChange(() =>
                            {
                                var log = new List<string>();
                                foreach (var d in deps) log.AddRange(FeaturePackages.Uninstall(Store!.ProjectDir, d));
                                log.AddRange(FeaturePackages.Uninstall(Store!.ProjectDir, p.Id));
                                return log;
                            }));
                        }
                    }
                    ImGui.PopID();
                }
            }
            ImGui.EndTable();
        }
        ImGui.Separator();
        var custom = installed.Where(i => !Packages.Any(p => p.Id.Equals(i, StringComparison.OrdinalIgnoreCase))).ToList();
        Ui.T("Custom features: " + (custom.Count == 0 ? "none" : string.Join(", ", custom)), Ui.Dim);
        Ui.Help("Your own feature ids. They add no files - your scripts check them with has_feature = my_feature (or Game.Features.Has in C#), so mechanics can be switched on and off.");
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##customfeat", "my_feature", ref _customFeature, 64);
        ImGui.SameLine();
        string cf = _customFeature.Trim().ToLowerInvariant();
        bool valid = cf.Length > 0 && cf.All(c => char.IsLetterOrDigit(c) || c == '_');
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Add custom")) { RunFeatureChange(() => FeaturePackages.Install(Store!.ProjectDir, new[] { cf })); _customFeature = ""; }
        ImGui.EndDisabled();
        foreach (var c in custom)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Remove {c}")) RunFeatureChange(() => FeaturePackages.Uninstall(Store!.ProjectDir, c));
        }
        ImGui.End();
    }

    /// <summary>Saves, runs a feature install/uninstall, then reloads the project files and the map.</summary>
    private void RunFeatureChange(Func<List<string>> change)
    {
        if (Store!.AnyDirty) SaveAll();
        try
        {
            foreach (var line in change()) Output(line);
            Store.ReloadAll();
            Catalog!.Invalidate();
            Later(() => LoadWorld());
            SetStatus("Features changed - see the Output tab. Press Check (F7) to test the game.");
        }
        catch (Exception ex) { ShowMessage("Feature change failed", ex.Message); }
    }
}
