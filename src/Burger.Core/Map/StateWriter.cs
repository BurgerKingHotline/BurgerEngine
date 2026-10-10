using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Map;

/// <summary>
/// Writes states back to map/states/*.txt (used by the in-game map editor and the editor app).
/// Only the state blocks are replaced; comments and other text in the files are kept.
/// </summary>
public static class StateWriter
{
    public const string NewStatesFile = "map/states/editor_states.txt";

    /// <summary>The script block for one state.</summary>
    public static Node ToNode(State s)
    {
        var n = new Node(s.Key, new List<Node>());
        if (s.NameOverride != null) n.Add("name", s.NameOverride, true);
        if (s.Owner != null) n.Add("owner", s.Owner.Tag);
        var cores = n.AddBlock("cores");
        foreach (var c in s.Cores) cores.AddItem(c.Tag);
        if (s.Claims.Count > 0)
        {
            var claims = n.AddBlock("claims");
            foreach (var c in s.Claims) claims.AddItem(c.Tag);
        }
        n.Add("manpower", ScriptWriter.Num(s.Manpower));
        n.Add("tax", ScriptWriter.Num(s.Tax));
        if (s.Category.Length > 0) n.Add("category", s.Category);
        if (s.Buildings.Count > 0)
        {
            var bn = n.AddBlock("buildings");
            foreach (var kv in s.Buildings) bn.Add(kv.Key, kv.Value.ToString());
        }
        var locs = n.AddBlock("locations");
        foreach (var l in s.Locations) locs.AddItem(l.Key);
        return n;
    }

    /// <summary>Path of a state's statement in a states file (handles both <c>key = { }</c> and <c>state = { id = key }</c>).</summary>
    public static string[] PathOf(string text, string file, string key)
    {
        var root = ScriptParser.ParseQuiet(text, file);
        for (int i = 0; i < root.Children!.Count; i++)
        {
            var n = root.Children[i];
            if (n.Children == null) continue;
            if (n.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return new[] { "#" + i };
            if (n.Key.Equals("state", StringComparison.OrdinalIgnoreCase) && string.Equals(n.GetString("id"), key, StringComparison.OrdinalIgnoreCase)) return new[] { "#" + i };
        }
        return new[] { key };
    }

    private static readonly string[] Managed = { "name", "owner", "cores", "claims", "manpower", "tax", "category", "buildings", "locations" };

    /// <summary>Updates (or adds) one state in a states file's text. Keys the editor does not manage (flags, controller...) are kept.</summary>
    public static string SetState(string text, string file, State s)
    {
        var path = PathOf(text, file, s.Key);
        var node = ToNode(s);
        if (!path[0].StartsWith('#')) return ScriptPatch.Set(text, file, path, node);
        var existing = ScriptPatch.Find(ScriptParser.ParseQuiet(text, file), path);
        if (existing == null) return ScriptPatch.Set(text, file, new[] { s.Key }, node);
        foreach (var k in Managed)
        {
            var child = node.Get(k);
            string target = k == "locations" && existing.Get("locations") == null && existing.Get("provinces") != null ? "provinces" : k;
            var old = existing.Get(target);
            if (child == null)
            {
                if (old != null) text = ScriptPatch.Remove(text, file, path.Append(target).ToArray());
                continue;
            }
            child.Key = target;
            // skip unchanged values so the original formatting stays
            if (old != null && old.ToString().Trim() == child.ToString().Trim()) continue;
            if (old != null && k is "manpower" or "tax" && Node.TryParseDouble(old.Value ?? "", out var ov) && Node.TryParseDouble(child.Value ?? "", out var nv) && Math.Abs(ov - nv) < 1e-9) continue;
            text = ScriptPatch.Set(text, file, path.Append(target).ToArray(), child);
        }
        return text;
    }

    /// <summary>Removes one state from a states file's text.</summary>
    public static string RemoveState(string text, string file, string key)
    {
        var path = PathOf(text, file, key);
        return path[0].StartsWith('#') ? ScriptPatch.Remove(text, file, path) : text;
    }

    /// <summary>
    /// Writes every state to the file it came from (new states go to <see cref="NewStatesFile"/>).
    /// <paramref name="deleted"/> lists (state key, source file) of states to remove from their files.
    /// Returns the number of files written.
    /// </summary>
    public static int Save(Game game, IEnumerable<(string Key, string File)>? deleted = null)
    {
        var byFile = new Dictionary<string, List<State>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in game.World.States)
        {
            if (string.IsNullOrEmpty(s.SourceFile)) s.SourceFile = NewStatesFile;
            if (!byFile.TryGetValue(s.SourceFile, out var list)) byFile[s.SourceFile] = list = new List<State>();
            list.Add(s);
        }
        var removals = (deleted ?? Array.Empty<(string, string)>()).ToList();
        foreach (var (_, f) in removals)
            if (!byFile.ContainsKey(f)) byFile[f] = new List<State>();

        int count = 0;
        foreach (var (file, states) in byFile)
        {
            string path = game.Data.PathOf(file);
            try
            {
                string text = File.Exists(path) ? File.ReadAllText(path) : "# States. Format: state_id = { owner = TAG cores = { TAG } manpower = 100 tax = 1 locations = { a b c } }\n";
                string original = text;
                foreach (var (key, f) in removals)
                    if (string.Equals(f, file, StringComparison.OrdinalIgnoreCase) && !states.Any(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                        text = RemoveState(text, file, key);
                foreach (var s in states) text = SetState(text, file, s);
                if (text == original && File.Exists(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
                count++;
            }
            catch (Exception ex)
            {
                Log.Error($"Could not write {file}: {ex.Message}");
            }
        }
        return count;
    }
}
