using System.Text;
using Burger.Core.Script;

namespace Burger.Studio;

/// <summary>
/// All file access of the editor goes through here: texts are cached, edits are kept in memory
/// (marked dirty) until <see cref="SaveAll"/>. Every page edits the same cached text, so a change made
/// in a form is immediately visible in the text editor and the other way round.
/// Paths are relative to the game folder and use '/'.
/// </summary>
public sealed class ProjectStore
{
    private sealed class Entry
    {
        public string Text = "";
        public bool Dirty;
        public bool Crlf;
        public bool Bom;
        public DateTime DiskTime;
    }

    private readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);

    public string ProjectDir { get; }
    public string GameDir { get; }
    public string Name => Path.GetFileName(ProjectDir);

    /// <summary>Raised after a file's text changed. Argument: (relative path, the object that made the change).</summary>
    public event Action<string, object?>? Changed;
    /// <summary>Raised when the set of dirty files changes (for the window title).</summary>
    public event Action? DirtyChanged;

    public ProjectStore(string projectDir)
    {
        ProjectDir = Path.GetFullPath(projectDir);
        GameDir = Path.Combine(ProjectDir, "game");
    }

    public static string Norm(string rel) => rel.Replace('\\', '/').TrimStart('/');
    public string Abs(string rel) => Path.GetFullPath(Path.Combine(GameDir, Norm(rel)));
    public string Rel(string abs) => Norm(Path.GetRelativePath(GameDir, abs));

    public bool Exists(string rel) => _files.ContainsKey(Norm(rel)) || File.Exists(Abs(rel));
    public bool IsDirty(string rel) => _files.TryGetValue(Norm(rel), out var e) && e.Dirty;
    public IEnumerable<string> DirtyFiles => _files.Where(kv => kv.Value.Dirty).Select(kv => kv.Key).OrderBy(k => k).ToList();
    public bool AnyDirty => _files.Values.Any(e => e.Dirty);

    /// <summary>Text of a file ("" if it does not exist).</summary>
    public string Get(string rel)
    {
        rel = Norm(rel);
        if (_files.TryGetValue(rel, out var e))
        {
            // pick up changes made outside the editor (e.g. in a text editor) if we have no unsaved edits
            if (!e.Dirty && File.Exists(Abs(rel)) && File.GetLastWriteTimeUtc(Abs(rel)) != e.DiskTime) _files.Remove(rel);
            else return e.Text;
        }
        var entry = new Entry();
        string path = Abs(rel);
        if (File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            entry.Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bytes, entry.Bom ? 3 : 0, bytes.Length - (entry.Bom ? 3 : 0));
            entry.Crlf = text.Contains("\r\n");
            entry.Text = text.Replace("\r\n", "\n");
            entry.DiskTime = File.GetLastWriteTimeUtc(path);
        }
        _files[rel] = entry;
        return entry.Text;
    }

    /// <summary>Replaces the text of a file (kept in memory until saved).</summary>
    public void Set(string rel, string text, object? source = null)
    {
        rel = Norm(rel);
        Get(rel);
        var e = _files[rel];
        text = text.Replace("\r\n", "\n");
        if (e.Text == text) return;
        bool wasDirty = e.Dirty;
        e.Text = text;
        e.Dirty = true;
        Changed?.Invoke(rel, source);
        if (!wasDirty) DirtyChanged?.Invoke();
    }

    /// <summary>Applies a change to a file's text.</summary>
    public void Edit(string rel, Func<string, string> change, object? source = null) => Set(rel, change(Get(rel)), source);

    /// <summary>Parses a cached file (syntax errors are ignored here; they are reported by Check for errors).</summary>
    public Node Parse(string rel) => ScriptParser.ParseQuiet(Get(rel), Norm(rel));

    /// <summary>Discards unsaved changes of one file.</summary>
    public void Revert(string rel)
    {
        rel = Norm(rel);
        if (_files.Remove(rel))
        {
            Changed?.Invoke(rel, this);
            DirtyChanged?.Invoke();
        }
    }

    /// <summary>Forgets all cached texts (after files were changed by another program). Unsaved edits are lost.</summary>
    public void ReloadAll()
    {
        _files.Clear();
        DirtyChanged?.Invoke();
        Changed?.Invoke("*", this);
    }

    /// <summary>Writes every changed file. Returns the number of files written.</summary>
    public int SaveAll()
    {
        int n = 0;
        foreach (var (rel, e) in _files.Where(kv => kv.Value.Dirty).ToList())
        {
            Write(rel, e);
            n++;
        }
        if (n > 0) DirtyChanged?.Invoke();
        return n;
    }

    public void Save(string rel)
    {
        rel = Norm(rel);
        if (_files.TryGetValue(rel, out var e) && e.Dirty)
        {
            Write(rel, e);
            DirtyChanged?.Invoke();
        }
    }

    private void Write(string rel, Entry e)
    {
        string path = Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string text = e.Crlf ? e.Text.Replace("\n", "\r\n") : e.Text;
        File.WriteAllText(path, text, new UTF8Encoding(e.Bom));
        e.Dirty = false;
        e.DiskTime = File.GetLastWriteTimeUtc(path);
    }

    /// <summary>Script files of a "file or folder" source like DataLoader uses: common/traits → common/traits.txt + common/traits/*.txt.</summary>
    public List<string> FilesOf(string fileOrFolder, string pattern = "*.txt")
    {
        var result = new List<string>();
        fileOrFolder = Norm(fileOrFolder);
        string ext = pattern.StartsWith("*") ? pattern[1..] : ".txt";
        if (!fileOrFolder.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && Exists(fileOrFolder + ext)) result.Add(fileOrFolder + ext);
        else if (fileOrFolder.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && Exists(fileOrFolder)) result.Add(fileOrFolder);
        string dir = Abs(fileOrFolder);
        if (Directory.Exists(dir))
            result.AddRange(Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
                .Select(Rel).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        // new files that only exist in memory
        foreach (var k in _files.Keys)
            if (k.StartsWith(fileOrFolder + "/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && !result.Contains(k, StringComparer.OrdinalIgnoreCase))
                result.Add(k);
        return result;
    }
}
