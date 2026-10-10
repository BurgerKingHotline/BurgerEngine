using System.Globalization;
using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Script;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>Inspector: properties of the selected object (like Unity's Inspector).</summary>
public sealed partial class Studio
{
    private BlockRef? _scriptFor;
    private string _scriptText = "";
    private string _scriptError = "";
    private bool _scriptDirty;
    /// <summary>Text being edited per field: (value in the file when editing started, text in the box).</summary>
    private readonly Dictionary<string, (string Base, string Edit)> _fieldBuffers = new();
    private object? _bufferOwner;

    private void DrawInspector()
    {
        if (!ImGui.Begin(WInspector, ref _showInspector)) { ImGui.End(); return; }
        TrackHover(WInspector);
        // edits in text boxes belong to one object; reset when the selection changes
        object? owner = Selected switch { SelBlock b => b.Ref.File + b.Ref.Id, SelLocation l => l.Loc, SelState s => s.State, SelFile f => f.Rel, _ => null };
        if (!Equals(owner, _bufferOwner)) { _fieldBuffers.Clear(); _bufferOwner = owner; }

        switch (Selected)
        {
            case null:
                Ui.Wrapped("Select something: a location on the map, an entry in the Outliner, or a file in the Content Browser.", Ui.Dim);
                ImGui.Spacing();
                Ui.Wrapped("Tip: right-click in the Content Browser or on a category in the Outliner to create new resources, events, countries, C# scripts...", Ui.Dim);
                break;
            case SelBlock sb: InspectBlock(sb); break;
            case SelLocation sl: InspectLocation(sl.Loc); break;
            case SelState ss: InspectState(ss.State); break;
            case SelFile sf: InspectFile(sf.Rel); break;
        }
        ImGui.End();
    }

    // =============================================================================================
    // Blocks
    // =============================================================================================

    private void InspectBlock(SelBlock sel)
    {
        var b = Catalog!.Refresh(sel.Ref);
        if (b == null) { Ui.T("This entry no longer exists.", Ui.Dim); return; }
        sel.Ref = b;
        var node = Catalog.NodeOf(b);
        if (node == null) { Ui.T("This entry no longer exists.", Ui.Dim); return; }
        var spec = b.Spec;

        Ui.T(Singular(spec).ToUpperInvariant(), Ui.Dim);
        Ui.Header(b.Id);
        Ui.T($"{b.File}  line {b.Line}", Ui.Dim);
        if (ImGui.SmallButton("Open script")) OpenFile(b.File, b.Line);
        ImGui.SameLine();
        if (ImGui.SmallButton("Duplicate")) DuplicateBlock(b);
        ImGui.SameLine();
        if (ImGui.SmallButton("Delete")) DeleteBlock(b);
        if (spec == Specs.Countries) { ImGui.SameLine(); if (ImGui.SmallButton("Play as")) Play(b.Id); }
        ImGui.Separator();

        // images (flag / portrait / picture) shown at the top
        foreach (var f in spec.Fields.Where(f => f.Kind == FieldKind.Image))
        {
            string? path = node.GetString(f.Key) ?? f.DefaultImage?.Replace("{id}", b.Id);
            var tex = path != null ? Textures.Get(Store!.Abs(path), 256) : null;
            if (tex != null) { Textures.Show(tex.Value, 160, 110); ImGui.SameLine(); }
        }
        if (spec.Fields.Any(f => f.Kind == FieldKind.Image)) { ImGui.NewLine(); Ui.T("Drop a PNG on this panel to set the " + spec.Fields.First(f => f.Kind == FieldKind.Image).Label.ToLowerInvariant() + ".", Ui.Dim); }

        if (spec.Help.Length > 0 && ImGui.CollapsingHeader("About " + spec.Title.ToLowerInvariant())) Ui.Wrapped(spec.Help, Ui.Dim);
        WorldContext(b);

        var fields = spec.Fields.Concat(spec.ExtraFields?.Invoke(Index!) ?? Enumerable.Empty<FieldSpec>()).ToList();
        if (fields.Count > 0 && ImGui.BeginTable("fields", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);
            foreach (var f in fields)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Ui.T(f.Label);
                Ui.Tip(f.Key + (f.Help != null ? "\n" + f.Help : ""));
                ImGui.TableNextColumn();
                ImGui.PushID(f.Key);
                ImGui.SetNextItemWidth(-1);
                FieldEditor(b, node, f);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        // full script of the entry
        ImGui.Spacing();
        if (_scriptFor == null || !_scriptFor.Same(b) || !_scriptDirty)
        {
            _scriptFor = b;
            _scriptText = ScriptPatch.SourceOf(Store!.Get(b.File), node).Replace("\r\n", "\n");
            if (!_scriptDirty) _scriptError = "";
        }
        ImGui.SetNextItemOpen(spec.ScriptFirst || fields.Count == 0, ImGuiCond.Appearing);
        if (ImGui.CollapsingHeader("Script" + (_scriptDirty ? " *" : "") + "##blockscript"))
        {
            Ui.T("Triggers, effects and nested blocks. Apply writes it back into " + Path.GetFileName(b.File) + ".", Ui.Dim);
            float hgt = Math.Max(160, ImGui.GetContentRegionAvail().Y - 40);
            if (ImGui.InputTextMultiline("##blocksrc", ref _scriptText, (uint)(_scriptText.Length * 2 + 65536), new Vector2(-1, hgt), ImGuiInputTextFlags.AllowTabInput))
                _scriptDirty = true;
            ImGui.BeginDisabled(!_scriptDirty);
            if (ImGui.Button("Apply")) ApplyBlockScript(b);
            ImGui.SameLine();
            if (ImGui.Button("Undo changes")) { _scriptDirty = false; _scriptError = ""; }
            ImGui.EndDisabled();
            if (_scriptError.Length > 0) { ImGui.SameLine(); Ui.T(_scriptError, Ui.Red); }
        }
    }

    private void ApplyBlockScript(BlockRef b)
    {
        var errors = new List<(string Message, string Where)>();
        string src = _scriptText.Trim();
        var parsed = ScriptParser.ParseQuiet(src, b.File, errors);
        if (errors.Count > 0) { _scriptError = $"Line {errors[0].Where.Split(':').Last()}: {errors[0].Message}"; return; }
        if (parsed.Children!.Count != 1 || parsed.Children[0].Children == null) { _scriptError = "The script must be exactly one block: key = { ... }"; return; }
        Store!.Edit(b.File, t =>
        {
            var node = ScriptPatch.Find(ScriptParser.ParseQuiet(t, b.File), b.Path);
            return node == null ? t : ScriptPatch.ReplaceSource(t, node, src);
        }, this);
        var newId = b.Spec.IdKey != null ? parsed.Children[0].GetString(b.Spec.IdKey) ?? b.Id : parsed.Children[0].Key;
        b.Id = newId;
        _scriptDirty = false;
        _scriptError = "";
        SetStatus($"{newId}: script applied (not saved yet)");
    }

    /// <summary>Writes one key of a block (null removes it).</summary>
    public void SetField(BlockRef b, string key, string? valueText)
    {
        var path = b.Path.Append(key).ToArray();
        Store!.Edit(b.File, t => valueText == null ? ScriptPatch.Remove(t, b.File, path) : ScriptPatch.SetValue(t, b.File, path, valueText), this);
        SetStatus($"{b.Id}: {key} = {valueText ?? "(removed)"}   (not saved yet)");
    }

    /// <summary>The text of a field's box; follows the file unless the user has typed something different.</summary>
    private string Buffer(string key, string current)
    {
        if (!_fieldBuffers.TryGetValue(key, out var v) || v.Base != current) _fieldBuffers[key] = v = (current, current);
        return v.Edit;
    }

    private void SetBuffer(string key, string edit)
    {
        var b = _fieldBuffers.TryGetValue(key, out var v) ? v.Base : edit;
        _fieldBuffers[key] = (b, edit);
    }

    private void FieldEditor(BlockRef b, Node block, FieldSpec f)
    {
        var n = block.Get(f.Key);
        string cur = n?.Value ?? "";
        switch (f.Kind)
        {
            case FieldKind.Bool:
            {
                int v = n == null ? 0 : Node.ParseBool(cur) ? 1 : 2;
                string[] opts = { "(not set)", "yes", "no" };
                if (ImGui.Combo("##v", ref v, opts, 3)) SetField(b, f.Key, v == 0 ? null : v == 1 ? "yes" : "no");
                break;
            }
            case FieldKind.Color:
            {
                bool has = Rgb.TryParse(n, out var c);
                bool pending = _pendingColor is { } pc && pc.B.Same(b) && pc.Key == f.Key;
                var col = pending ? _pendingColor!.Value.C : has ? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : new Vector3(0.5f, 0.5f, 0.5f);
                ImGui.SetNextItemWidth(-30);
                if (ImGui.ColorEdit3("##v", ref col, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.DisplayRGB)) _pendingColor = (b, f.Key, col);
                // write the color when the user lets go (dragging in the picker keeps an item active)
                if (_pendingColor is { } p2 && p2.B.Same(b) && p2.Key == f.Key && !ImGui.IsAnyItemActive())
                {
                    SetField(b, f.Key, $"{{ {(int)Math.Round(p2.C.X * 255)} {(int)Math.Round(p2.C.Y * 255)} {(int)Math.Round(p2.C.Z * 255)} }}");
                    _pendingColor = null;
                }
                ImGui.SameLine();
                if (has && ImGui.SmallButton("x")) SetField(b, f.Key, null);
                Ui.Tip("Remove");
                break;
            }
            case FieldKind.Country:
            case FieldKind.Location:
            case FieldKind.Character:
            case FieldKind.Choice:
            {
                IReadOnlyList<string> options = f.Kind switch
                {
                    FieldKind.Country => Index!.Tags,
                    FieldKind.Location => Index!.Locations,
                    FieldKind.Character => Index!.Characters,
                    _ => f.Options?.Invoke(Index!).ToList() ?? new List<string>(),
                };
                string v = cur;
                if (Ui.ComboFilter("##v", ref v, options)) SetField(b, f.Key, v.Length == 0 ? null : ScriptWriter.Quote(v, n?.ValueQuoted == true));
                break;
            }
            case FieldKind.List:
            {
                string v = Buffer(f.Key, string.Join(" ", block.GetList(f.Key)));
                ImGui.SetNextItemWidth(-30);
                ImGui.InputText("##v", ref v, 1024);
                SetBuffer(f.Key, v);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    var items = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    SetField(b, f.Key, items.Length == 0 ? (n == null ? null : "{ }") : "{ " + string.Join(" ", items.Select(i => ScriptWriter.Quote(i))) + " }");
                }
                Ui.Tip("Items separated by spaces");
                ImGui.SameLine();
                if (ImGui.SmallButton("+")) ImGui.OpenPopup("pick");
                if (ImGui.BeginPopup("pick"))
                {
                    var have = new HashSet<string>(v.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                    foreach (var o in f.Options?.Invoke(Index!) ?? Array.Empty<string>())
                    {
                        bool on = have.Contains(o);
                        if (ImGui.Checkbox(o, ref on))
                        {
                            if (on) have.Add(o); else have.Remove(o);
                            var list = have.ToList();
                            SetBuffer(f.Key, string.Join(" ", list));
                            SetField(b, f.Key, "{ " + string.Join(" ", list.Select(i => ScriptWriter.Quote(i))) + " }");
                        }
                    }
                    ImGui.EndPopup();
                }
                break;
            }
            case FieldKind.Image:
            {
                string v = Buffer(f.Key, cur);
                ImGui.SetNextItemWidth(-80);
                ImGui.InputTextWithHint("##v", f.DefaultImage?.Replace("{id}", b.Id) ?? "path inside the game folder", ref v, 512);
                SetBuffer(f.Key, v);
                if (ImGui.IsItemDeactivatedAfterEdit()) SetImageField(b, f, v.Trim().Trim('"'));
                ImGui.SameLine();
                if (ImGui.SmallButton("Browse")) { _browseDir = f.ImageFolder; _showContent = true; _imageTarget = (b, f); ImGui.SetWindowFocus(WContent); }
                Ui.Tip("Pick an image in the Content Browser (double-click it), or drop a PNG from Windows onto this panel");
                break;
            }
            default:
            {
                if (n?.Children != null) { Ui.T("(block - edit it in Script below)", Ui.Dim); break; }
                string v = Buffer(f.Key, cur);
                ImGui.InputText("##v", ref v, 1024);
                SetBuffer(f.Key, v);
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    v = v.Trim();
                    if (v.Length == 0) SetField(b, f.Key, null);
                    else if (f.Kind == FieldKind.Number && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && v.Any(char.IsWhiteSpace))
                        SetStatus($"{f.Label}: enter a number (use . for decimals)");
                    else
                    {
                        bool quote = f.Kind == FieldKind.Text && (n?.ValueQuoted == true || v.Any(char.IsWhiteSpace) || n == null);
                        SetField(b, f.Key, ScriptWriter.Quote(v, quote));
                    }
                }
                break;
            }
        }
    }

    private (BlockRef B, string Key, Vector3 C)? _pendingColor;
    private (BlockRef B, FieldSpec F)? _imageTarget;

    /// <summary>Sets an image field; a path outside the game folder is copied into the field's folder first.</summary>
    public void SetImageField(BlockRef b, FieldSpec f, string path)
    {
        if (path.Length == 0) { SetField(b, f.Key, null); return; }
        string rel = path;
        if (Path.IsPathRooted(path))
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Store!.GameDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) rel = Store.Rel(full);
            else
            {
                if (!File.Exists(full)) { SetStatus("File not found: " + full); return; }
                string name = (f.DefaultImage != null ? b.Id : Path.GetFileNameWithoutExtension(full)) + Path.GetExtension(full).ToLowerInvariant();
                rel = f.ImageFolder.TrimEnd('/') + "/" + name;
                string dest = Store.Abs(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(full, dest, true);
                SetStatus($"Copied the image to {rel}");
            }
        }
        SetBuffer(f.Key, rel);
        SetField(b, f.Key, ScriptWriter.Quote(rel.Replace('\\', '/'), true));
        if (b.Spec == Specs.Countries) _countriesDirty = true;
    }

    private bool TryDropOnInspector(string[] files)
    {
        if (Selected is not SelBlock sb) return false;
        var field = sb.Ref.Spec.Fields.FirstOrDefault(f => f.Kind == FieldKind.Image);
        var img = files.FirstOrDefault(f => Ui.IconFor(f, false) == Ui.Icon.Image);
        if (field == null || img == null) return false;
        SetImageField(sb.Ref, field, img);
        return true;
    }

    public void SelectCountry(string tag)
    {
        var b = Catalog!.Find(Specs.Countries, tag);
        if (b != null) SelectBlock(b);
    }

    /// <summary>What a country or character is doing in the loaded world: states, characters, family...</summary>
    private void WorldContext(BlockRef b)
    {
        if (_world == null) return;
        var w = _world.World;
        if (b.Spec == Specs.Countries && w.GetCountry(b.Id) is { } c)
        {
            if (ImGui.CollapsingHeader($"States ({c.OwnedStates.Count})##cstates", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (c.OwnedStates.Count == 0) Ui.Wrapped("Owns no land. Select a state on the map and set its owner to " + c.Tag + ".", Ui.Yellow);
                foreach (var st in c.OwnedStates.OrderBy(x => x.Key))
                    if (ImGui.Selectable($"{st.DisplayName}  ({st.Key}, {st.Locations.Count} locations)##cs{st.Key}"))
                    {
                        Selected = new SelState { State = st };
                        if (st.Locations.Count > 0) FocusOn(st.Locations[0]);
                    }
            }
            var chars = w.CharactersOf(c).ToList();
            if (ImGui.CollapsingHeader($"Characters ({chars.Count})##cchars"))
            {
                foreach (var ch in chars.OrderByDescending(x => x == c.Ruler).ThenByDescending(x => x == c.Heir).ThenBy(x => x.Key))
                {
                    string role = ch == c.Ruler ? "  [ruler]" : ch == c.Heir ? "  [heir]" : ch.IsGeneral ? "  [general]" : "";
                    var cb = Catalog!.Find(Specs.Characters, ch.Key);
                    if (ImGui.Selectable($"{ch.FirstName} {ch.Family}{role}##cc{ch.Key}") && cb != null) SelectBlock(cb);
                    if (cb == null) Ui.Tip("Generated by the engine (not in history/characters)");
                }
            }
        }
        if (b.Spec == Specs.Characters && w.GetCharacter(b.Id) is { } person)
        {
            if (person.Country != null)
            {
                if (ImGui.SmallButton($"Country {person.Country.Tag}")) SelectCountry(person.Country.Tag);
                ImGui.SameLine();
            }
            Ui.T($"Age {person.Age(w.Date)}" + (person.Spouse != null ? $"   married to {person.Spouse.FirstName}" : ""), Ui.Dim);
            if (person.Children.Count > 0 && ImGui.CollapsingHeader($"Children ({person.Children.Count})##kids"))
                foreach (var kid in person.Children)
                {
                    var kb = Catalog!.Find(Specs.Characters, kid.Key);
                    if (ImGui.Selectable($"{kid.FirstName} {kid.Family}##k{kid.Key}") && kb != null) SelectBlock(kb);
                }
        }
    }

    // =============================================================================================
    // Locations and states
    // =============================================================================================

    private void InspectLocation(Location l)
    {
        Ui.T("LOCATION", Ui.Dim);
        Ui.Header(l.Key);
        string name = Buffer("name", Index!.Pretty(l.Key));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##locname", ref name, 128);
        SetBuffer("name", name);
        if (ImGui.IsItemDeactivatedAfterEdit() && name.Trim().Length > 0) { Index.SetText(l.Key, name.Trim(), this); SetStatus($"{l.Key} is now called \"{name.Trim()}\" (not saved yet)"); }
        Ui.Tip("Display name (saved as a localisation text)");
        Ui.T($"Color {l.MapColor.ToHex()}   {l.PixelCount} pixels   {l.Neighbors.Count} neighbors", Ui.Dim);
        Ui.T($"Terrain: {l.Terrain.Name}{(l.IsWater ? " (water)" : "")}{(l.IsCoastal ? ", coastal" : "")}");
        if (ImGui.SmallButton("Focus camera")) FocusOn(l);
        ImGui.SameLine();
        if (l.IsLand && ImGui.SmallButton("New state from this location")) NewStateFromLocation(l);
        ImGui.Separator();
        if (l.Owner != null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Country {l.Owner.Tag}")) SelectCountry(l.Owner.Tag);
            Ui.Tip($"Edit {l.Owner.Name}");
        }
        if (l.State != null) InspectState(l.State);
        else if (l.IsLand)
        {
            Ui.T("This location has no state.", Ui.Yellow);
            Ui.Wrapped("Create a new state from it, or select a state and use the Paint tool in the Viewport.", Ui.Dim);
        }
    }

    private void InspectState(State s)
    {
        Ui.T("STATE", Ui.Dim);
        Ui.Header(s.Key);
        Ui.T($"{s.Locations.Count} locations   {s.SourceFile}", Ui.Dim);
        if (ImGui.BeginTable("statefields", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("k", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableSetupColumn("v", ImGuiTableColumnFlags.WidthStretch);

            Row("Display name");
            string nm = Buffer("statename", s.NameOverride != null ? Index!.Pretty(s.NameOverride) : Index!.Pretty(s.Key));
            ImGui.InputText("##sname", ref nm, 128);
            SetBuffer("statename", nm);
            if (ImGui.IsItemDeactivatedAfterEdit() && nm.Trim().Length > 0)
            {
                if (s.NameOverride != null) { s.NameOverride = nm.Trim(); WriteState(s); }
                else Index.SetText(s.Key, nm.Trim(), this);
            }

            Row("Owner");
            string owner = s.Owner?.Tag ?? "";
            if (Ui.ComboFilter("##owner", ref owner, Index.Tags))
            {
                var c = CountryOf(owner);
                if (owner.Length > 0 && c == null) SetStatus($"'{owner}' is not a country tag");
                else SetStateOwner(s, c);
            }

            Row("Tax / month");
            double tax = s.Tax;
            if (ImGui.InputDouble("##tax", ref tax, 0.5, 1, "%.2f") && tax >= 0) { s.Tax = tax; _stateNumbersDirty = s; }
            Row("Manpower");
            double mp = s.Manpower;
            if (ImGui.InputDouble("##mp", ref mp, 10, 100, "%.0f") && mp >= 0) { s.Manpower = mp; _stateNumbersDirty = s; }
            if (_stateNumbersDirty == s && !ImGui.IsAnyItemActive()) { WriteState(s); _stateNumbersDirty = null; }

            Row("Category");
            string cat = Buffer("cat", s.Category);
            ImGui.InputText("##cat", ref cat, 64);
            SetBuffer("cat", cat);
            if (ImGui.IsItemDeactivatedAfterEdit()) { s.Category = cat.Trim(); WriteState(s); }
            ImGui.EndTable();
        }
        if (s.Owner != null && ImGui.SmallButton($"Edit owner {s.Owner.Tag}")) SelectCountry(s.Owner.Tag);
        if (ImGui.CollapsingHeader($"Locations ({s.Locations.Count})##statelocs"))
        {
            foreach (var l in s.Locations.OrderBy(l => l.Key))
                if (ImGui.Selectable($"{Index!.Pretty(l.Key)}  ({l.Key})##sl{l.Key}", Selected is SelLocation sl && sl.Loc == l)) { Selected = new SelLocation { Loc = l }; FocusOn(l); }
        }
        CountryChecklist("Cores", "Countries that see this state as theirs", s.Cores, () => WriteState(s));
        CountryChecklist("Claims", "Countries that want this state", s.Claims, () => WriteState(s));
        ImGui.Spacing();
        if (ImGui.Button("Paint locations into this state"))
        {
            Selected = new SelState { State = s };
            _tool = 1;
            ImGui.SetWindowFocus(WViewport);
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete state")) DeleteState(s);
    }

    private State? _stateNumbersDirty;

    private static void Row(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        Ui.T(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
    }

    private void CountryChecklist(string title, string help, HashSet<Country> set, Action changed)
    {
        string label = $"{title}: " + (set.Count == 0 ? "none" : string.Join(" ", set.Select(c => c.Tag)));
        if (!ImGui.CollapsingHeader(label + "##" + title)) { Ui.Tip(help); return; }
        Ui.Tip(help);
        ImGui.BeginChild(title + "list", new Vector2(0, Math.Min(200, 24 * Index!.Tags.Count + 8)), ImGuiChildFlags.Borders);
        foreach (var tag in Index.Tags)
        {
            bool on = set.Any(c => c.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
            var c = _world?.World.GetCountry(tag);
            string text = c != null ? $"{tag}  {c.Name}" : tag;
            if (ImGui.Checkbox(text, ref on))
            {
                var country = CountryOf(tag);
                if (country == null) continue;
                if (on) set.Add(country); else set.Remove(country);
                changed();
            }
        }
        ImGui.EndChild();
    }

    // =============================================================================================
    // Files
    // =============================================================================================

    private void InspectFile(string rel)
    {
        Ui.T("FILE", Ui.Dim);
        Ui.Header(Path.GetFileName(rel));
        Ui.T(rel, Ui.Dim);
        string abs = Store!.Abs(rel);
        if (File.Exists(abs))
        {
            var info = new FileInfo(abs);
            Ui.T($"{info.Length / 1024.0:0.#} KB   modified {info.LastWriteTime:g}", Ui.Dim);
        }
        var icon = Ui.IconFor(rel, false);
        if (icon == Ui.Icon.Image)
        {
            var tex = Textures.Get(abs, 1024);
            if (tex != null)
            {
                Ui.T($"{tex.Value.Width} x {tex.Value.Height} (preview)", Ui.Dim);
                Textures.Show(tex.Value, ImGui.GetContentRegionAvail().X, 400);
            }
            if (_imageTarget is { } it)
            {
                if (ImGui.Button($"Use as {it.F.Label.ToLowerInvariant()} of {it.B.Id}")) { SetImageField(it.B, it.F, rel); Selected = new SelBlock { Ref = it.B }; _imageTarget = null; }
            }
            return;
        }
        if (ImGui.Button("Open in code editor")) OpenFile(rel, 0);
        ImGui.SameLine();
        if (ImGui.Button("Open with Windows")) Engine.OpenInExplorer(abs);

        var kind = Catalog.KindOfFile(rel);
        if (kind != null && rel.EndsWith(".txt"))
        {
            ImGui.Separator();
            Ui.T($"{kind.Title} in this file:", Ui.Dim);
            var kinds = rel.StartsWith("common/units") ? new[] { Specs.UnitTypes, Specs.DivisionTemplates } : new[] { kind };
            foreach (var k in kinds)
                foreach (var b in Catalog!.Of(k).Where(x => x.File.Equals(rel, StringComparison.OrdinalIgnoreCase)))
                    if (ImGui.Selectable(b.Label + "##f" + k.Title + b.Id)) SelectBlock(b);
        }
    }
}
