using System.Numerics;
using System.Text;
using Burger.Core;
using Burger.Core.Modding;
using Burger.Core.Script;
using ImGuiNET;

namespace Burger.Studio;

/// <summary>Code panel: text editor tabs for script (.txt) and C# (.cs) files.</summary>
public sealed partial class Studio
{
    private sealed class OpenFileState
    {
        public string Rel = "";
        public int GotoLine;          // 1-based line to jump to, 0 = none
        public int GotoTries;
        public int MarkLine;          // line highlighted in the margin (after a jump)
        public int Line = 1, Col = 1;
        public string Message = "";
        public bool MessageIsError;
        public bool SelectTab;
        public List<string> CompileErrors = new();
    }

    private readonly List<OpenFileState> _openFiles = new();
    private OpenFileState? _cbFile;
    private ImGuiInputTextCallback? _codeCallback;
    private bool _showWords;
    private string _wordFilter = "";

    /// <summary>Opens a file in the Code panel (and jumps to a line if &gt; 0).</summary>
    public void OpenFile(string rel, int line)
    {
        rel = ProjectStore.Norm(rel);
        var f = _openFiles.FirstOrDefault(x => x.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (f == null) { f = new OpenFileState { Rel = rel }; _openFiles.Add(f); }
        f.SelectTab = true;
        if (line > 0) f.GotoLine = line;
        _showCode = true;
        Later(() => ImGui.SetWindowFocus(WCode));
    }

    private void DrawCode()
    {
        if (!ImGui.Begin(WCode, ref _showCode)) { ImGui.End(); return; }
        TrackHover(WCode);
        if (_openFiles.Count == 0)
        {
            Ui.Wrapped("No file open. Double-click a script in the Content Browser or an entry in the Outliner to edit it here.", Ui.Dim);
            Ui.Wrapped("Ctrl+S saves everything. For long C# work you can also use Visual Studio / VS Code: scripts/Scripts.csproj gives them code completion.", Ui.Dim);
            ImGui.End();
            return;
        }
        if (ImGui.BeginTabBar("codetabs", ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll))
        {
            foreach (var f in _openFiles.ToList())
            {
                bool open = true;
                var flags = (Store!.IsDirty(f.Rel) ? ImGuiTabItemFlags.UnsavedDocument : 0) | (f.SelectTab ? ImGuiTabItemFlags.SetSelected : 0);
                f.SelectTab = false;
                bool visible = ImGui.BeginTabItem(Path.GetFileName(f.Rel) + "##" + f.Rel, ref open, flags);
                Ui.Tip(f.Rel);
                if (visible)
                {
                    DrawCodeFile(f);
                    ImGui.EndTabItem();
                }
                if (!open) _openFiles.Remove(f);
            }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private unsafe void DrawCodeFile(OpenFileState f)
    {
        bool cs = f.Rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        bool script = f.Rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || f.Rel.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
        // toolbar
        if (ImGui.SmallButton("Save")) { Store!.Save(f.Rel); SetStatus("Saved " + f.Rel); if (script) CheckSyntax(f); }
        ImGui.SameLine();
        if (script && ImGui.SmallButton("Check syntax")) CheckSyntax(f);
        if (cs && ImGui.SmallButton("Compile C#")) CompileScripts(f);
        ImGui.SameLine();
        if (ImGui.SmallButton("Undo all changes") && Store!.IsDirty(f.Rel)) Confirm("Undo", "Throw away the unsaved changes of " + f.Rel + "?", () => Store.Revert(f.Rel));
        ImGui.SameLine();
        if (ImGui.BeginCombo("##insert", "Insert template", ImGuiComboFlags.WidthFitPreview))
        {
            foreach (var t in cs ? Templates.CSharpSnippets : Templates.Script)
            {
                if (ImGui.Selectable(t.Name)) InsertIntoFile(f, t.Code);
                Ui.Tip(t.Code.Length > 500 ? t.Code[..500] + "..." : t.Code);
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Script words", ref _showWords);
        Ui.Tip("Every trigger, effect, value, iterator and scope you can use");
        ImGui.SameLine();
        if (ImGui.SmallButton("Open with Windows")) Engine.OpenInExplorer(Store!.Abs(f.Rel));
        ImGui.SameLine();
        Ui.T($"Ln {f.Line}, Col {f.Col}", Ui.Dim);
        if (f.Message.Length > 0) { ImGui.SameLine(); Ui.T(f.Message, f.MessageIsError ? Ui.Red : Ui.Green); }

        float wordsW = _showWords ? 260 : 0;
        float errH = f.CompileErrors.Count > 0 ? 110 : 0;
        var size = ImGui.GetContentRegionAvail() - new Vector2(wordsW + (wordsW > 0 ? 8 : 0), errH);

        string text = Store!.Get(f.Rel);
        _cbFile = f;
        _codeCallback ??= CodeCallback;
        ImGui.PushFont(HasCodeFont ? CodeFont : ImGui.GetFont());
        float lh = ImGui.GetTextLineHeight();
        float padY = ImGui.GetStyle().FramePadding.Y;
        int lineCount = 1, longest = 0, cur = 0;
        foreach (char ch in text)
        {
            if (ch == '\n') { lineCount++; longest = Math.Max(longest, cur); cur = 0; }
            else cur += ch == '\t' ? 4 : 1;
        }
        longest = Math.Max(longest, cur);
        float charW = ImGui.CalcTextSize("M").X;
        float gutter = charW * (lineCount.ToString().Length + 2);
        // the editor is as big as the text and scrolls inside this child, so we can scroll to lines and show line numbers
        ImGui.BeginChild("codescroll" + f.Rel, size, ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar);
        float viewH = ImGui.GetWindowHeight();
        if (f.GotoLine > 0)
        {
            // repeated for a few frames: the scroll range is only known after the text was laid out once
            ImGui.SetScrollY(Math.Max(0, (f.GotoLine - 4) * lh));
            f.MarkLine = f.GotoLine;
            if (++f.GotoTries >= 3) { f.GotoLine = 0; f.GotoTries = 0; }
        }
        var origin = ImGui.GetCursorScreenPos();
        float scroll = ImGui.GetScrollY();
        var dl = ImGui.GetWindowDrawList();
        int first = Math.Max(0, (int)(scroll / lh) - 1), last = Math.Min(lineCount, first + (int)(viewH / lh) + 3);
        for (int i = first; i < last; i++)
        {
            string num = (i + 1).ToString();
            bool mark = i + 1 == f.MarkLine;
            float x = origin.X + gutter - charW * (num.Length + 1);
            if (mark) dl.AddRectFilled(new Vector2(origin.X, origin.Y + padY + i * lh), new Vector2(origin.X + gutter - 4, origin.Y + padY + (i + 1) * lh), Ui.Col(120, 98, 52));
            dl.AddText(new Vector2(x, origin.Y + padY + i * lh), mark ? Ui.Col(255, 230, 160) : Ui.Col(110, 110, 120), num);
        }
        ImGui.SetCursorScreenPos(origin + new Vector2(gutter, 0));
        var inputSize = new Vector2(Math.Max(size.X - gutter - 20, (longest + 4) * charW), Math.Max(viewH - 8, lineCount * lh + padY * 2 + lh));
        int lineBefore = f.Line;
        bool changed = ImGui.InputTextMultiline("##code" + f.Rel, ref text, (uint)(text.Length * 2 + 65536), inputSize,
            ImGuiInputTextFlags.AllowTabInput | ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.NoHorizontalScroll, _codeCallback);
        if (ImGui.IsItemActive())
        {
            f.MarkLine = 0;
            if (f.Line != lineBefore)
            {
                // keep the caret visible
                float y = (f.Line - 1) * lh + padY;
                if (y < scroll) ImGui.SetScrollY(y);
                else if (y + lh * 2 > scroll + viewH) ImGui.SetScrollY(y + lh * 2 - viewH);
            }
        }
        ImGui.EndChild();
        ImGui.PopFont();
        if (changed) Store.Set(f.Rel, text, this);

        if (_showWords)
        {
            ImGui.SameLine();
            ImGui.BeginChild("words", new Vector2(wordsW, size.Y), ImGuiChildFlags.Borders);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##wf", "search...", ref _wordFilter, 64);
            var g = LastCheckedGame ?? _world;
            if (g == null) Ui.T("Press Check (F7) to list the script words of this project.", Ui.Dim);
            else
            {
                void Section(string title, IEnumerable<string> names, string help)
                {
                    var list = names.Where(n => _wordFilter.Length == 0 || n.Contains(_wordFilter, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n).ToList();
                    if (list.Count == 0) return;
                    if (!ImGui.CollapsingHeader($"{title} ({list.Count})")) { Ui.Tip(help); return; }
                    Ui.Tip(help);
                    foreach (var n in list)
                        if (ImGui.Selectable(n + "##w" + title)) InsertIntoFile(f, n);
                }
                Section("Triggers", g.Script.TriggerNames, "Conditions: trigger, potential, allow, limit");
                Section("Effects", g.Script.EffectNames, "Actions: immediate, option, effect");
                Section("Values", g.Script.ValueNames, "Numbers: gold > 10, add_gold = income");
                Section("Iterators", g.Script.IteratorNames, "Use as any_/all_ (triggers) or every_/random_ (effects) + name");
                Section("Scopes", g.Script.LinkNames, "Switch scope: ruler = { ... }, owner = ROOT");
            }
            ImGui.EndChild();
        }
        if (f.CompileErrors.Count > 0)
        {
            ImGui.BeginChild("cserrors", new Vector2(0, errH - 4), ImGuiChildFlags.Borders);
            foreach (var e in f.CompileErrors)
            {
                if (ImGui.Selectable(e))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(e, @"^(?<f>[^:]+\.cs):(?<l>\d+):");
                    if (m.Success) OpenFile(m.Groups["f"].Value, int.Parse(m.Groups["l"].Value));
                }
            }
            ImGui.EndChild();
        }
    }

    /// <summary>Runs while the editor is active: applies a pending "go to line" and reports the caret position.</summary>
    private unsafe int CodeCallback(ImGuiInputTextCallbackData* data)
    {
        var d = new ImGuiInputTextCallbackDataPtr(data);
        var f = _cbFile;
        if (f == null) return 0;
        byte* buf = (byte*)d.Buf;
        int len = d.BufTextLen;
        int ln = 1, col = 1;
        int cur = Math.Min(d.CursorPos, len);
        for (int i = 0; i < cur; i++)
        {
            if (buf[i] == '\n') { ln++; col = 1; }
            else if ((buf[i] & 0xC0) != 0x80) col++;
        }
        f.Line = ln;
        f.Col = col;
        _cursorByte = cur;
        return 0;
    }

    private int _cursorByte;

    private void InsertIntoFile(OpenFileState f, string code)
    {
        // insert at the caret (byte offset of the last active editor) or at the end
        string text = Store!.Get(f.Rel);
        var bytes = Encoding.UTF8.GetBytes(text);
        int at = _cbFile == f ? Math.Clamp(_cursorByte, 0, bytes.Length) : bytes.Length;
        string before = Encoding.UTF8.GetString(bytes, 0, at), after = Encoding.UTF8.GetString(bytes, at, bytes.Length - at);
        Store.Set(f.Rel, before + code + after, this);
        SetStatus("Inserted into " + f.Rel);
    }

    private void CheckSyntax(OpenFileState f)
    {
        var errors = new List<(string Message, string Where)>();
        ScriptParser.ParseQuiet(Store!.Get(f.Rel), f.Rel, errors);
        if (errors.Count == 0)
        {
            f.Message = "No syntax errors";
            f.MessageIsError = false;
            return;
        }
        string ln = errors[0].Where.Split(':').Last();
        f.Message = $"{errors.Count} syntax error(s) - line {ln}: {errors[0].Message}";
        f.MessageIsError = true;
        if (int.TryParse(ln, out int l)) f.GotoLine = l;
    }

    private void CompileScripts(OpenFileState f)
    {
        SaveAll();
        var errors = new List<string>();
        var (fileCount, classes) = ModManager.CompileCheck(Store!.GameDir, errors);
        f.CompileErrors = errors;
        if (errors.Count == 0)
        {
            f.Message = $"Compiled {fileCount} file(s): {classes} script class(es) will run when the game starts";
            f.MessageIsError = false;
        }
        else
        {
            f.Message = $"{errors.Count} C# error(s) - click one below";
            f.MessageIsError = true;
        }
    }
}
