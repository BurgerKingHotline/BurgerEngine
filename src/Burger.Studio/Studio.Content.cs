using System.Numerics;
using Burger.Core.Script;
using ImGuiNET;
using Raylib_cs;

namespace Burger.Studio;

/// <summary>Content Browser: the game folder like Unity's Project window / Unreal's Content Browser,
/// with right-click "Create" for every kind of asset.</summary>
public sealed partial class Studio
{
    private string _browseDir = "";
    private string? _contentSelected;
    private string _contentFilter = "";
    private float _tileSize = 84;
    private string? _renaming;
    private string _renameText = "";

    private void DrawContentBrowser()
    {
        if (!ImGui.Begin(WContent, ref _showContent)) { ImGui.End(); return; }
        TrackHover(WContent);
        if (!Directory.Exists(Store!.Abs(_browseDir))) _browseDir = IsBackend(_browseDir) && Directory.Exists(Store.Abs(BackendRoot)) ? BackendRoot : "";
        string rootRel = IsBackend(_browseDir) ? BackendRoot : "";

        // ------------------------------------------------ breadcrumb + search
        if (ImGui.Button("+ Create")) ImGui.OpenPopup("createpopup");
        Ui.Tip("Create a new resource, event, country, C# script... (also: right-click in the folder)");
        if (ImGui.BeginPopup("createpopup")) { CreateMenuItems(); ImGui.EndPopup(); }
        ImGui.SameLine();
        if (ImGui.ArrowButton("##up", ImGuiDir.Up) && _browseDir != rootRel) _browseDir = Parent(_browseDir);
        Ui.Tip("Up one folder");
        ImGui.SameLine();
        if (ImGui.SmallButton(rootRel.Length == 0 ? "Game" : "Backend")) _browseDir = rootRel;
        string acc = rootRel;
        foreach (var part in (rootRel.Length == 0 ? _browseDir : _browseDir.Length > rootRel.Length ? _browseDir[(rootRel.Length + 1)..] : "").Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            acc = acc.Length == 0 ? part : acc + "/" + part;
            ImGui.SameLine();
            Ui.T(">", Ui.Dim);
            ImGui.SameLine();
            if (ImGui.SmallButton(part + "##crumb" + acc)) { _browseDir = acc; break; }
        }
        ImGui.SameLine();
        Ui.T(rootRel.Length == 0 ? "   players can change these files" : "   compiled into the EXE - players cannot change it", Ui.Dim);
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 10, ImGui.GetWindowWidth() - 360));
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##cfilter", "Search all files...", ref _contentFilter, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.SliderFloat("##tile", ref _tileSize, 56, 160, "size %.0f");

        // ------------------------------------------------ folder tree | files
        if (ImGui.BeginTable("cbsplit", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("tree", ImGuiTableColumnFlags.WidthFixed, 210);
            ImGui.TableSetupColumn("files", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.BeginChild("cbtree");
            RootNode("Game", "", "The game folder: data, maps, events, texts and scripts/ (C#). Shipped next to the EXE - players can mod all of it.");
            RootNode("Backend", BackendRoot, "Your game's locked C# (rules, combat, AI...). Compiled into the EXE when you export, so players cannot change it. While you work it is compiled when the game starts, like scripts.");
            ImGui.EndChild();
            ImGui.TableNextColumn();
            ImGui.BeginChild("cbfiles");
            DrawFileGrid();
            ImGui.EndChild();
            ImGui.EndTable();
        }
        ImGui.End();
    }

    /// <summary>The project's backend folder relative to the game folder.</summary>
    public const string BackendRoot = "../backend";
    public static bool IsBackend(string rel) => rel == BackendRoot || rel.StartsWith(BackendRoot + "/");
    private static string Parent(string rel) => Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";

    private void RootNode(string label, string rel, string help)
    {
        bool selected = _browseDir == rel;
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen | (selected ? ImGuiTreeNodeFlags.Selected : 0);
        ImGui.PushStyleColor(ImGuiCol.Text, rel.Length == 0 ? Ui.Gold : Ui.Blue);
        bool open = ImGui.TreeNodeEx(label + "##root" + label, flags);
        ImGui.PopStyleColor();
        Ui.Tip(help);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            if (rel == BackendRoot) EnsureBackend();
            _browseDir = rel;
        }
        if (!open) return;
        string abs = Store!.Abs(rel);
        if (Directory.Exists(abs)) FolderTree(abs, rel);
        else if (rel == BackendRoot) ImGui.TextDisabled("   (click to create)");
        ImGui.TreePop();
    }

    /// <summary>Creates the backend folder with a short explanation the first time it is used.</summary>
    private void EnsureBackend()
    {
        string dir = Store!.Abs(BackendRoot);
        if (Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        if (Burger.Core.Modding.EngineAssets.BackendReadme() is { } readme) File.Copy(readme, Path.Combine(dir, "README.txt"));
    }

    private void FolderTree(string abs, string rel)
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(abs).Where(d => Path.GetFileName(d) is not ("bin" or "obj" or ".vs")).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch { return; }
        foreach (var d in dirs)
        {
            string name = Path.GetFileName(d);
            string r = rel.Length == 0 ? name : rel + "/" + name;
            bool hasChildren;
            try { hasChildren = Directory.EnumerateDirectories(d).Any(); } catch { hasChildren = false; }
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | (hasChildren ? 0 : ImGuiTreeNodeFlags.Leaf);
            if (r.Equals(_browseDir, StringComparison.OrdinalIgnoreCase)) flags |= ImGuiTreeNodeFlags.Selected;
            if (_browseDir.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            bool open = ImGui.TreeNodeEx(name + "##tree" + r, flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) _browseDir = r;
            if (open)
            {
                FolderTree(d, r);
                ImGui.TreePop();
            }
        }
    }

    private void DrawFileGrid()
    {
        string dirAbs = Store!.Abs(_browseDir);
        var entries = new List<(string Name, string Rel, bool Dir)>();
        string Join(string dir, string name) => dir.Length == 0 ? name : dir + "/" + name;
        try
        {
            if (_contentFilter.Trim().Length > 0)
            {
                // search every file of the current root (like Unity's project search)
                string rootRel = IsBackend(_browseDir) ? BackendRoot : "";
                string rootAbs = Store.Abs(rootRel);
                if (Directory.Exists(rootAbs))
                    foreach (var f in Directory.EnumerateFileSystemEntries(rootAbs, "*", SearchOption.AllDirectories))
                    {
                        string rel = Store.Rel(f);
                        if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                        if (!Path.GetFileName(f).Contains(_contentFilter.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                        entries.Add((Path.GetFileName(f), rel, Directory.Exists(f)));
                        if (entries.Count >= 300) break;
                    }
            }
            else
            {
                entries = Directory.GetDirectories(dirAbs).Select(d => (Path.GetFileName(d), Join(_browseDir, Path.GetFileName(d)), true))
                    .Where(e => e.Item1 is not ("bin" or "obj" or ".vs"))
                    .OrderBy(e => e.Item1, StringComparer.OrdinalIgnoreCase)
                    .Concat(Directory.GetFiles(dirAbs).Select(f => (Path.GetFileName(f), Join(_browseDir, Path.GetFileName(f)), false)).OrderBy(e => e.Item1, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                // files created in the editor that are not saved yet
                foreach (var dirty in Store.DirtyFiles)
                    if (Parent(dirty).Equals(_browseDir, StringComparison.OrdinalIgnoreCase) && !entries.Any(e => e.Name.Equals(Path.GetFileName(dirty), StringComparison.OrdinalIgnoreCase)))
                        entries.Add((Path.GetFileName(dirty), dirty, false));
            }
        }
        catch (Exception ex) { Ui.T("Cannot read folder: " + ex.Message, Ui.Red); return; }

        float tile = _tileSize, pad = 10;
        float avail = ImGui.GetContentRegionAvail().X;
        int cols = Math.Max(1, (int)((avail + pad) / (tile + pad)));
        int i = 0;
        var dl = ImGui.GetWindowDrawList();
        float lineH = ImGui.GetTextLineHeight();
        foreach (var (name, rel, isDir) in entries)
        {
            if (i % cols != 0) ImGui.SameLine(0, pad);
            i++;
            ImGui.PushID(rel);
            ImGui.BeginGroup();
            var p = ImGui.GetCursorScreenPos();
            bool sel = _contentSelected == rel;
            ImGui.InvisibleButton("tile", new Vector2(tile, tile));
            bool hovered = ImGui.IsItemHovered();
            if (sel || hovered) dl.AddRectFilled(p, p + new Vector2(tile, tile), sel ? Ui.Col(120, 98, 52, 200) : Ui.Col(255, 255, 255, 18), 4);
            var icon = Ui.IconFor(name, isDir);
            Texture2D? thumb = icon == Ui.Icon.Image ? Textures.Get(Store.Abs(rel), 128) : null;
            if (thumb is { } t)
            {
                float s = Math.Min((tile - 12) / t.Width, (tile - 12) / t.Height);
                var size = new Vector2(t.Width * s, t.Height * s);
                var tl = p + new Vector2((tile - size.X) / 2, (tile - size.Y) / 2);
                dl.AddImage(new IntPtr(t.Id), tl, tl + size);
            }
            else Ui.DrawIcon(dl, icon, p + new Vector2(tile * 0.12f, tile * 0.08f), tile * 0.76f);
            if (Store.IsDirty(rel)) dl.AddCircleFilled(p + new Vector2(tile - 9, 9), 4, Ui.Col(237, 196, 92));

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _contentSelected = rel;
                if (!isDir) Selected = new SelFile { Rel = rel };
            }
            if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) OpenEntry(rel, isDir);
            if (hovered) Ui.Tip(rel + (Store.IsDirty(rel) ? "\n(not saved)" : ""));
            ItemContextMenu(rel, isDir);

            // name (2 lines max) or rename box
            if (_renaming == rel)
            {
                ImGui.SetNextItemWidth(tile);
                if (_renameFocus) { ImGui.SetKeyboardFocusHere(); _renameFocus = false; }
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) _renaming = null;
                else if (ImGui.InputText("##rename", ref _renameText, 128, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll) || ImGui.IsItemDeactivated()) DoRename(rel, isDir);
            }
            else
            {
                // name centered under the icon (two lines at most), like Unity
                var lines = WrapName(name, tile);
                var start = ImGui.GetCursorScreenPos();
                for (int li = 0; li < lines.Count; li++)
                {
                    float w = ImGui.CalcTextSize(lines[li]).X;
                    dl.AddText(new Vector2(start.X + (tile - w) / 2, start.Y + li * lineH), sel ? Ui.Col(255, 236, 180) : Ui.Col(226, 224, 216), lines[li]);
                }
                ImGui.Dummy(new Vector2(tile, lineH * 2));
            }
            ImGui.EndGroup();
            ImGui.PopID();
        }
        if (entries.Count == 0) Ui.T(_contentFilter.Trim().Length > 0 ? "Nothing matches." : "Empty folder - right-click to create something, or drag files here from Windows.", Ui.Dim);

        // background context menu (Create ...)
        if (ImGui.BeginPopupContextWindow("cbbg", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            CreateMenu();
            ImGui.Separator();
            if (ImGui.MenuItem("Show in Explorer")) Engine.OpenInExplorer(dirAbs);
            if (ImGui.MenuItem("Import files...")) ShowMessage("Import files", "Drag files from Windows Explorer onto this window. They are copied into the folder shown in the Content Browser.");
            ImGui.EndPopup();
        }
        // keyboard
        if (ImGui.IsWindowFocused() && _contentSelected != null && _renaming == null)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F2)) StartRename(_contentSelected);
            if (ImGui.IsKeyPressed(ImGuiKey.Delete)) DeleteEntry(_contentSelected, Directory.Exists(Store.Abs(_contentSelected)));
        }
    }

    /// <summary>Splits a file name into at most two lines that fit the tile (breaking at _ - . or anywhere), with ... if needed.</summary>
    private static List<string> WrapName(string name, float width)
    {
        var result = new List<string>();
        string rest = name;
        for (int line = 0; line < 2 && rest.Length > 0; line++)
        {
            if (ImGui.CalcTextSize(rest).X <= width || line == 1)
            {
                string l = rest;
                if (ImGui.CalcTextSize(l).X > width)
                {
                    while (l.Length > 1 && ImGui.CalcTextSize(l + "...").X > width) l = l[..^1];
                    l += "...";
                }
                result.Add(l);
                break;
            }
            // longest prefix that fits, preferring a break after _ - . or space
            int fit = 1;
            while (fit < rest.Length && ImGui.CalcTextSize(rest[..(fit + 1)]).X <= width) fit++;
            int brk = rest.LastIndexOfAny(new[] { '_', '-', '.', ' ' }, Math.Max(0, fit - 1));
            int cut = brk > fit / 3 ? brk + 1 : fit;
            result.Add(rest[..cut]);
            rest = rest[cut..];
        }
        return result;
    }

    private void OpenEntry(string rel, bool isDir)
    {
        if (isDir) { _browseDir = rel; _contentSelected = null; return; }
        var icon = Ui.IconFor(rel, false);
        if (icon == Ui.Icon.Image)
        {
            if (_imageTarget is { } it) { SetImageField(it.B, it.F, rel); Selected = new SelBlock { Ref = it.B }; _imageTarget = null; return; }
            Selected = new SelFile { Rel = rel };
            return;
        }
        if (icon is Ui.Icon.Script or Ui.Icon.CSharp or Ui.Icon.Text or Ui.Icon.Map) OpenFile(rel, 0);
        else Engine.OpenInExplorer(Store!.Abs(rel));
    }

    private void ItemContextMenu(string rel, bool isDir)
    {
        if (!ImGui.BeginPopupContextItem("ctx")) return;
        _contentSelected = rel;
        if (ImGui.MenuItem(isDir ? "Open folder" : "Open")) OpenEntry(rel, isDir);
        if (!isDir && ImGui.MenuItem("Open with Windows")) Engine.OpenInExplorer(Store!.Abs(rel));
        if (ImGui.MenuItem("Rename", "F2")) StartRename(rel);
        if (!isDir && ImGui.MenuItem("Duplicate")) DuplicateFile(rel);
        if (ImGui.MenuItem("Delete", "Del")) DeleteEntry(rel, isDir);
        ImGui.Separator();
        if (ImGui.MenuItem("Copy path")) ImGui.SetClipboardText(rel);
        if (ImGui.MenuItem("Show in Explorer")) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + Store!.Abs(rel) + "\"");
        if (!isDir && Catalog.KindOfFile(rel) is { } kind && rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            ImGui.Separator();
            AddToFileMenuItems(kind, rel);
        }
        if (isDir && ImGui.BeginMenu("Create inside"))
        {
            _browseDir = rel;
            CreateMenu();
            ImGui.EndMenu();
        }
        ImGui.EndPopup();
    }

    private bool _renameFocus;

    private void StartRename(string rel)
    {
        _renameFocus = true;
        _renaming = rel;
        _renameText = Path.GetFileName(rel);
    }

    private void DoRename(string rel, bool isDir)
    {
        if (_renaming != rel) return;
        string newName = _renameText.Trim();
        _renaming = null;
        if (newName.Length == 0 || newName == Path.GetFileName(rel) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
        string from = Store!.Abs(rel);
        string to = Path.Combine(Path.GetDirectoryName(from)!, newName);
        if (File.Exists(to) || Directory.Exists(to)) { ShowMessage("Rename", "Something with that name already exists."); return; }
        if (Store.AnyDirty) SaveAll();
        try
        {
            if (isDir) Directory.Move(from, to); else File.Move(from, to);
            Store.ReloadAll();
            _contentSelected = Store.Rel(to);
            SetStatus($"Renamed to {newName}");
            if (rel.EndsWith(".cs")) SetStatus("Renamed. The class name inside the file stays the same.");
        }
        catch (Exception ex) { ShowMessage("Could not rename", ex.Message); }
    }

    private void DuplicateFile(string rel)
    {
        string from = Store!.Abs(rel);
        string dir = Path.GetDirectoryName(from)!, name = Path.GetFileNameWithoutExtension(from), ext = Path.GetExtension(from);
        string to;
        int n = 1;
        do to = Path.Combine(dir, $"{name}_copy{(n > 1 ? n.ToString() : "")}{ext}"); while (File.Exists(to) && n++ < 100);
        Store.Save(rel);
        File.Copy(from, to);
        _contentSelected = Store.Rel(to);
        SetStatus($"Created {Path.GetFileName(to)}" + (ext == ".txt" ? " - rename the ids inside it, they must be unique" : ""));
    }

    private void DeleteEntry(string rel, bool isDir)
    {
        Confirm("Delete", $"Delete {rel}{(isDir ? " and everything in it" : "")}?\n\nIt is moved to the Windows Recycle Bin.", () =>
        {
            try
            {
                string abs = Store!.Abs(rel);
                if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(abs, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else if (File.Exists(abs)) Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(abs, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                foreach (var dirty in Store.DirtyFiles.Where(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase) || f.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)).ToList())
                    Store.Revert(dirty);
                Store.Revert(rel);
                _openFiles.RemoveAll(f => f.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase) || f.Rel.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase));
                if (Selected is SelFile sf && sf.Rel == rel) Selected = null;
                _contentSelected = null;
                Catalog!.Invalidate();
                SetStatus($"Deleted {rel} (in the Recycle Bin)");
            }
            catch (Exception ex) { ShowMessage("Could not delete", ex.Message); }
        });
    }

    // =============================================================================================
    // Create
    // =============================================================================================

    private void CreateMenu()
    {
        if (!ImGui.BeginMenu("Create")) return;
        CreateMenuItems();
        ImGui.EndMenu();
    }

    private void CreateMenuItems()
    {
        if (IsBackend(_browseDir))
        {
            ImGui.TextDisabled("Backend C# (locked into the EXE)");
            foreach (var t in Templates.CSharp)
            {
                if (ImGui.MenuItem(t.Name)) CreateCSharp(t);
                Ui.Tip(t.Description);
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Add a feature...")) ShowFeatures = true;
            Ui.Tip("Features (Project > Features) add ready-made backend files: warfare, combat models, fog of war, diplomacy, characters, economy, AI.");
        }
        else
        {
            ImGui.TextDisabled("Mechanics");
            foreach (var k in new[] { Specs.Resources, Specs.Modifiers, Specs.Decisions, Specs.ScriptValues })
                if (ImGui.MenuItem(Cap(Singular(k)))) CreateBlockAsset(k);
            if (ImGui.MenuItem("Window (panel)")) CreateBlockAsset(Specs.Interface, "panel");
            if (ImGui.MenuItem("Map mode")) CreateBlockAsset(Specs.Interface, "map_mode");
            if (ImGui.MenuItem("On action")) CreateBlockAsset(Specs.OnActions);
            ImGui.Separator();
            ImGui.TextDisabled("World");
            foreach (var k in new[] { Specs.Countries, Specs.Characters, Specs.Terrain, Specs.UnitTypes, Specs.DivisionTemplates, Specs.Equipment, Specs.ShipTypes, Specs.PlaneTypes, Specs.Buildings }.Where(KindAvailable))
                if (ImGui.MenuItem(Cap(Singular(k)))) CreateBlockAsset(k);
            ImGui.Separator();
            ImGui.TextDisabled("Content");
            if (ImGui.MenuItem("Event file")) CreateEventFile();
            Ui.Tip("A new file of events. Add events to it (or any event file) by right-clicking the file > New event.");
            foreach (var k in new[] { Specs.DiplomaticActions, Specs.CharacterInteractions, Specs.CasusBelli, Specs.SubjectTypes, Specs.Traits,
                                      Specs.Ideologies, Specs.Governments, Specs.LawGroups, Specs.CabinetPosts }.Where(KindAvailable))
                if (ImGui.MenuItem(Cap(Singular(k)))) CreateBlockAsset(k);
            if (ImGui.MenuItem("More: add a feature...")) ShowFeatures = true;
            Ui.Tip("Units, characters, diplomatic actions and more appear here when their feature is installed (Project > Features).");
            ImGui.Separator();
            if (ImGui.BeginMenu("C# script"))
            {
                foreach (var t in Templates.CSharp)
                {
                    if (ImGui.MenuItem(t.Name)) CreateCSharp(t);
                    Ui.Tip(t.Description);
                }
                ImGui.EndMenu();
            }
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Folder")) Prompt("New folder", "Folder name:", "new_folder", v => v.Length == 0 || v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "Invalid name" : null, v =>
        {
            Directory.CreateDirectory(Store!.Abs(_browseDir + "/" + v));
            _browseDir = ProjectStore.Norm(_browseDir + "/" + v);
        });
        if (ImGui.MenuItem("Text file")) Prompt("New text file", "File name:", "new_file.txt", v => v.Length == 0 || v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "Invalid name" : Store!.Exists(_browseDir + "/" + v) ? "That file exists" : null, v =>
        {
            string rel = ProjectStore.Norm(_browseDir + "/" + v);
            Store!.Set(rel, "# " + v + "\n", this);
            Store.Save(rel);
            OpenFile(rel, 0);
        });
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Folder that holds the files of a kind (for showing it in the Content Browser).</summary>
    public string FolderOf(BlockSpec k) => Directory.Exists(Store!.Abs(k.Source)) ? k.Source : Parent(k.DefaultFile);

    private static string What(BlockSpec kind, string? type)
        => type == "map_mode" ? "map mode" : type == "panel" ? "window" : type?.Replace('_', ' ') ?? Singular(kind);

    /// <summary>
    /// Files a new entry of this kind can be added to, the main file first. Files that only hold other kinds of
    /// statements (e.g. interface/theme.txt for windows) are left out. Empty if the kind has no file yet.
    /// </summary>
    public List<string> TargetFilesOf(BlockSpec kind)
    {
        var files = Store!.FilesOf(kind.Source).Where(f => Catalog.KindOfFile(f) is { } k && (k == kind || k.Source == kind.Source)).ToList();
        if (kind.StatementKeys != null)
            files = files.Where(f =>
            {
                var root = Store.Parse(f);
                return !root.All.Any() || root.All.Any(n => kind.StatementKeys.Contains(n.Key, StringComparer.OrdinalIgnoreCase));
            }).ToList();
        string main = files.FirstOrDefault(f => f.Equals(kind.DefaultFile, StringComparison.OrdinalIgnoreCase))
                      ?? files.OrderByDescending(f => Catalog!.Of(kind).Count(b => b.File.Equals(f, StringComparison.OrdinalIgnoreCase))).FirstOrDefault()
                      ?? "";
        if (main.Length > 0) { files.Remove(main); files.Insert(0, main); }
        return files;
    }

    /// <summary>Right-click menu items on a file: add a new entry of the file's kind into it.</summary>
    private void AddToFileMenuItems(BlockSpec kind, string rel)
    {
        if (kind == Specs.Events)
        {
            if (ImGui.MenuItem("New country event in this file")) CreateBlockAsset(kind, "country_event", rel);
            if (ImGui.MenuItem("New character event in this file")) CreateBlockAsset(kind, "character_event", rel);
        }
        else if (kind == Specs.Interface)
        {
            if (ImGui.MenuItem("New window in this file")) CreateBlockAsset(kind, "panel", rel);
            if (ImGui.MenuItem("New map mode in this file")) CreateBlockAsset(kind, "map_mode", rel);
        }
        else
        {
            var kinds = Catalog.Kinds.Where(k => k.Source == kind.Source).ToList(); // unit types and division templates share a file
            foreach (var k in kinds)
                if (ImGui.MenuItem($"New {Singular(k)} in this file")) CreateBlockAsset(k, null, rel);
        }
    }

    /// <summary>Asks for the id (and, when the folder has several files, which file) and adds a new entry.</summary>
    public void CreateBlockAsset(BlockSpec kind, string? type = null, string? intoFile = null)
    {
        if (kind == Specs.Events && intoFile == null)
        {
            // events are added from an event file (right-click > New event); from a folder, make a new file
            var eventFiles = TargetFilesOf(kind);
            if (eventFiles.Count == 0) { CreateEventFile(); return; }
        }
        string what = What(kind, type);
        string label = kind == Specs.Countries ? "Tag (3 capital letters or digits, e.g. GER):" : $"Id of the new {what} (letters, digits, _):";
        string initial = kind == Specs.Countries ? "" : kind == Specs.Events ? NextEventId(intoFile ?? TargetFilesOf(kind)[0]) : "my_" + what.Replace(' ', '_');
        Func<string, string?> validate = v => kind.ValidateId?.Invoke(v) ?? (Catalog!.Find(kind, v) != null ? "That id already exists" : null);
        var files = intoFile != null ? new List<string> { intoFile } : TargetFilesOf(kind);
        if (files.Count == 0) files.Add(kind.DefaultFile);
        if (files.Count == 1)
            Prompt("New " + what, label + $"\n\nIt is added to {files[0]}.", initial, validate, id => DoCreateBlockAsset(kind, id, type, files[0]));
        else
            PromptWithChoice("New " + what, label, initial, validate, "Add it to the file:", files.ToArray(),
                (id, choice) => DoCreateBlockAsset(kind, id, type, files[choice]));
    }

    /// <summary>Adds a new entry from the kind's template to a file (the main file when none is given). Used by the dialogs and the self-test.</summary>
    public BlockRef? DoCreateBlockAsset(BlockSpec kind, string id, string? type = null, string? file = null)
    {
        string what = What(kind, type);
        file ??= TargetFilesOf(kind).FirstOrDefault() ?? kind.DefaultFile;
        string text = kind.Template.Replace("{id}", id);
        if (type != null && kind.NewTypes != null && !text.StartsWith(type))
        {
            if (type == "map_mode")
                text = $"map_mode = {{\n\tid = {id}\n\tname = \"New Map Mode\"\n\tvalue = {{ value = state.tax }}      # location scope\n\tmin = 0\n\tmax = 10\n\tlow_color = {{ 60 60 140 }}\n\thigh_color = {{ 240 220 80 }}\n}}\n";
            else text = type + " " + text[text.IndexOf('=')..];
        }
        bool isNew = !Store!.Exists(file);
        bool wasDirty = Store.DirtyFiles.Contains(file, StringComparer.OrdinalIgnoreCase);
        if (isNew) Store.Set(file, $"# {Cap(kind.Title)}\n# {kind.Help}\n", this);
        if (kind.Container != null)
        {
            var node = ScriptParser.ParseQuiet(text, file).Children!.FirstOrDefault();
            if (node == null) return null;
            Store.Edit(file, t => ScriptPatch.Set(t, file, new[] { kind.Container, id }, node), this);
        }
        else Store.Edit(file, t => ScriptPatch.AppendStatement(t, text.TrimEnd()), this);
        if (isNew || !wasDirty) Store.Save(file); // never saves other unsaved edits of that file
        _browseDir = Parent(file);
        _contentSelected = file;
        Catalog!.Invalidate();
        var b = Catalog.Find(kind, id);
        if (b != null) Selected = new SelBlock { Ref = b };
        ImGui.SetWindowFocus(WInspector);
        SetStatus($"Added {what} '{id}' to {file}");
        if (kind == Specs.Countries) SetStatus($"Added {id} to {file}. Give {id} land: select a state on the map and set its owner.");
        return b;
    }

    /// <summary>The next free id in an event file: namespace.N.</summary>
    private string NextEventId(string file)
    {
        string ns = Store!.Exists(file) ? Store.Parse(file).GetString("namespace") ?? Path.GetFileNameWithoutExtension(file) : Path.GetFileNameWithoutExtension(file);
        int max = 0;
        foreach (var b in Catalog!.Of(Specs.Events))
        {
            if (!b.Id.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(b.Id[(ns.Length + 1)..], out int n)) max = Math.Max(max, n);
        }
        return $"{ns}.{max + 1}";
    }

    /// <summary>Asks for a name and creates events/name.txt with a namespace and one starter event.</summary>
    public void CreateEventFile()
    {
        string folder = _browseDir.StartsWith("events", StringComparison.OrdinalIgnoreCase) ? _browseDir : "events";
        Prompt("New event file", "File name (also the event namespace, e.g. france_events):", "my_events",
            v => v.Length == 0 || v.Any(c => !(char.IsLetterOrDigit(c) || c == '_')) ? "Letters, digits and _ only"
                 : Store!.Exists($"{folder}/{v}.txt") ? "That file already exists" : null,
            v => DoCreateEventFile(folder, v));
    }

    public BlockRef? DoCreateEventFile(string folder, string name)
    {
        string rel = ProjectStore.Norm($"{folder}/{name}.txt");
        Store!.Set(rel, $"# Events. Add more: right-click this file in the Content Browser > New country/character event.\nnamespace = {name}\n", this);
        Store.Save(rel);
        return DoCreateBlockAsset(Specs.Events, NextEventId(rel), "country_event", rel);
    }

    public void CreateCSharp(Template t)
    {
        string def = t.Name == "Empty script" ? "MyScript" : MakeClassName(string.Concat(t.Name.Split(' ', '/', ',').Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : "")));
        bool backendFirst = IsBackend(_browseDir);
        string backendFolder = backendFirst ? _browseDir : BackendRoot;
        var choices = backendFirst
            ? new[] { "Backend - compiled into the EXE, players cannot change it", "scripts/ - players can mod it" }
            : new[] { "scripts/ - players can mod it", "Backend - compiled into the EXE, players cannot change it" };
        PromptWithChoice("New C# script", "File name (without .cs):", def,
            s => s.Length == 0 || s.Any(c => !(char.IsLetterOrDigit(c) || c == '_')) ? "Letters, digits and _ only"
                 : Store!.Exists($"scripts/{s}.cs") || Store.Exists($"{backendFolder}/{s}.cs") ? "That file already exists" : null,
            "Put it in:", choices,
            (name, choice) =>
            {
                bool toBackend = choices[choice].StartsWith("Backend");
                if (toBackend) EnsureBackend();
                string rel = toBackend ? $"{backendFolder}/{name}.cs" : $"scripts/{name}.cs";
                string code = t.Code;
                foreach (var cls in new[] { "MyScriptWords", "MyScript", "MyCombatScript", "MyCombat", "MyDiplomacyScript", "MyDiplomacyRules", "MyEconomyScript", "MyEconomyRules",
                             "MyCharacterScript", "MyCharacterRules", "MyWarfareScript", "MyWarfareRules", "MyAiScript", "MyAi", "MyInterfaceScript", "MyCommands", "MyReactions" })
                    code = System.Text.RegularExpressions.Regex.Replace(code, $@"\b{cls}\b", cls == "MyScript" ? MakeClassName(name) : cls + "_" + name);
                Store!.Set(rel, code, this);
                Store.Save(rel);
                _browseDir = Parent(rel);
                _contentSelected = rel;
                OpenFile(rel, 0);
                SetStatus(toBackend ? $"Created {rel}. It runs the next time the game starts and is locked into the EXE on export."
                                    : $"Created {rel}. It runs the next time the game starts; players can mod it.");
            });
    }

    public static string MakeClassName(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
        string name = new(chars);
        if (name.Length == 0 || char.IsDigit(name[0])) name = "Script" + name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    public void DuplicateBlock(BlockRef b)
    {
        Prompt("Duplicate " + b.Id, "Id of the copy:", b.Id + "_copy",
            v => b.Spec.ValidateId?.Invoke(v) ?? (Catalog!.Find(b.Spec, v) != null ? "That id already exists" : null),
            id => DoDuplicateBlock(b, id));
    }

    public BlockRef? DoDuplicateBlock(BlockRef b, string id)
    {
        var node = Catalog!.NodeOf(b);
        if (node == null) return null;
        string text = ScriptPatch.SourceOf(Store!.Get(b.File), node);
        var top = ScriptParser.ParseQuiet(text, b.File).Children!.FirstOrDefault();
        if (top == null) return null;
        if (b.Spec.IdKey != null) text = ScriptPatch.SetValue(text, b.File, new[] { "#0", b.Spec.IdKey }, id);
        else text = id + text[(top.KeyQuoted ? top.Key.Length + 2 : top.Key.Length)..];
        if (b.Spec.Container != null)
        {
            var parsed = ScriptParser.ParseQuiet(text, b.File).Children!.FirstOrDefault();
            if (parsed == null) return null;
            Store.Edit(b.File, t => ScriptPatch.Set(t, b.File, new[] { b.Path[0], id }, parsed), this);
        }
        else Store.Edit(b.File, t => ScriptPatch.AppendStatement(t, text), this);
        Catalog.Invalidate();
        var nb = Catalog.Find(b.Spec, id);
        if (nb != null) Selected = new SelBlock { Ref = nb };
        SetStatus($"Created {id} in {b.File} (not saved yet)");
        return nb;
    }

    public void DeleteBlock(BlockRef b)
    {
        Confirm("Delete", $"Delete {b.Id} from {b.File}?\n\nOther files that refer to it are not changed - use Check afterwards.", () =>
        {
            Store!.Edit(b.File, t => ScriptPatch.Remove(t, b.File, b.Path), this);
            // a file that held only this entry is now empty: offer nothing, just leave the header
            Catalog!.Invalidate();
            if (Selected is SelBlock sb && sb.Ref.Same(b)) Selected = null;
            SetStatus($"Deleted {b.Id} (not saved yet)");
        });
    }
}
