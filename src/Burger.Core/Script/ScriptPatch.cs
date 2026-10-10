using System.Text;

namespace Burger.Core.Script;

/// <summary>
/// Edits script text in place: replaces, inserts or removes one statement while keeping every other
/// character of the file (comments, blank lines, formatting) exactly as it was.
/// Used by the editor so that changing a country's color does not rewrite the whole file.
/// <code>
/// text = ScriptPatch.SetValue(text, "countries.txt", new[] { "GER", "color" }, "{ 30 30 30 }");
/// text = ScriptPatch.Set(text, "states.txt", new[] { "berlin_state" }, newStateNode);
/// text = ScriptPatch.Remove(text, "states.txt", new[] { "old_state" });
/// </code>
/// A path is a list of keys from the top of the file down. Keys are case-insensitive; the first match is used.
/// A path element <c>#3</c> means "the 4th statement" (for files with repeated keys, like events).
/// </summary>
public static class ScriptPatch
{
    /// <summary>Sets <c>path = value</c>. <paramref name="valueText"/> is raw script text (a word, "quoted" or { block }).</summary>
    public static string SetValue(string text, string fileName, string[] path, string valueText)
    {
        var parsed = ScriptParser.ParseQuiet(path[^1] + " = " + valueText, fileName);
        var node = parsed.Children!.FirstOrDefault() ?? new Node(path[^1], valueText);
        node.Key = path[^1];
        return Set(text, fileName, path, node);
    }

    /// <summary>Replaces the statement at <paramref name="path"/> with <paramref name="node"/> (inserted if missing,
    /// creating parent blocks as needed). The node's key is used as written.</summary>
    public static string Set(string text, string fileName, string[] path, Node node)
    {
        if (path.Length == 0) throw new ArgumentException("empty path");
        var root = ParseQuiet(text, fileName);
        Node parent = root;
        int depth = 0;
        for (int i = 0; i < path.Length - 1; i++)
        {
            var next = Child(parent, path[i]);
            if (next == null && path[i].StartsWith('#')) return text; // positional element that no longer exists
            if (next == null || next.Children == null)
            {
                // build the missing chain as one new block and insert it into the deepest existing parent
                var chain = new Node(path[i], new List<Node>());
                var cur = chain;
                for (int j = i + 1; j < path.Length - 1; j++)
                {
                    var b = new Node(path[j], new List<Node>());
                    cur.Children!.Add(b);
                    cur = b;
                }
                cur.Children!.Add(node);
                if (next != null) return Replace(text, next, Serialize(chain, depth));
                return Insert(text, parent, chain, depth);
            }
            parent = next;
            depth++;
        }
        var target = Child(parent, path[^1]);
        if (target == null && path[^1].StartsWith('#')) return text;
        if (target != null && target.SpanStart >= 0) return Replace(text, target, Serialize(node, depth));
        return Insert(text, parent, node, depth);
    }

    /// <summary>Removes the statement at <paramref name="path"/> (and the rest of its line if that is only whitespace).</summary>
    public static string Remove(string text, string fileName, string[] path)
    {
        var target = Find(ParseQuiet(text, fileName), path);
        if (target == null || target.SpanStart < 0) return text;
        int start = target.SpanStart, end = target.SpanEnd;
        // swallow leading indentation and the trailing newline when the statement is alone on its line
        int ls = start;
        while (ls > 0 && (text[ls - 1] == ' ' || text[ls - 1] == '\t')) ls--;
        int le = end;
        while (le < text.Length && (text[le] == ' ' || text[le] == '\t')) le++;
        bool lineStart = ls == 0 || text[ls - 1] == '\n';
        bool lineEnd = le >= text.Length || text[le] == '\n' || text[le] == '\r';
        if (lineStart && lineEnd)
        {
            start = ls;
            end = le;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
        }
        return text[..start] + text[end..];
    }

    /// <summary>Returns the statement at <paramref name="path"/>, or null.</summary>
    public static Node? Find(Node root, string[] path)
    {
        Node? cur = root;
        foreach (var k in path)
        {
            cur = Child(cur, k);
            if (cur == null) return null;
        }
        return cur;
    }

    /// <summary>Exact original text of a statement (for "edit this block as text").</summary>
    public static string SourceOf(string text, Node n)
        => n.SpanStart >= 0 && n.SpanEnd <= text.Length ? text[n.SpanStart..n.SpanEnd] : n.ToString().TrimEnd();

    /// <summary>Replaces the original text of a top-level statement with new raw text.</summary>
    public static string ReplaceSource(string text, Node n, string newSource)
        => n.SpanStart < 0 ? AppendStatement(text, newSource) : text[..n.SpanStart] + newSource.Trim() + text[n.SpanEnd..];

    /// <summary>Adds raw statement text at the end of the file.</summary>
    public static string AppendStatement(string text, string statement)
    {
        var sb = new StringBuilder(text);
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
        if (sb.Length > 0) sb.Append('\n');
        sb.Append(statement.Trim()).Append('\n');
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Child by key, or by position for "#index".</summary>
    public static Node? Child(Node parent, string key)
    {
        if (key.Length > 1 && key[0] == '#' && int.TryParse(key[1..], out int idx))
            return parent.Children != null && idx >= 0 && idx < parent.Children.Count ? parent.Children[idx] : null;
        return parent.Get(key);
    }

    private static Node ParseQuiet(string text, string fileName)
    {
        // parse errors are reported elsewhere (validation); patching must still work on the parts that parse
        return ScriptParser.ParseQuiet(text, fileName);
    }

    private static string Serialize(Node n, int indent)
    {
        var sb = new StringBuilder();
        ScriptWriter.WriteNode(sb, n, indent);
        return sb.ToString().TrimStart('\t').TrimEnd('\n', '\r');
    }

    private static string Replace(string text, Node target, string replacement)
        => text[..target.SpanStart] + replacement + text[target.SpanEnd..];

    private static string Insert(string text, Node parent, Node node, int depth)
    {
        if (parent.SpanStart < 0) // the file root
            return AppendStatement(text, Serialize(node, 0));

        int close = parent.SpanEnd - 1; // position of the closing '}'
        if (close < 0 || close >= text.Length || text[close] != '}')
            return AppendStatement(text, Serialize(node, 0)); // broken file: never corrupt it further
        int lineStart = close;
        while (lineStart > 0 && (text[lineStart - 1] == ' ' || text[lineStart - 1] == '\t')) lineStart--;
        bool multiLine = text.IndexOf('\n', parent.SpanStart, parent.SpanEnd - parent.SpanStart) >= 0;
        if (multiLine && (lineStart == 0 || text[lineStart - 1] == '\n'))
        {
            string line = new string('\t', depth) + Serialize(node, depth) + "\n";
            return text[..lineStart] + line + text[lineStart..];
        }
        // single-line block: { a = 1 }  →  { a = 1 b = 2 }
        string s = Serialize(node, depth);
        bool needSpaceBefore = close > 0 && !char.IsWhiteSpace(text[close - 1]);
        return text[..close] + (needSpaceBefore ? " " : "") + s + " " + text[close..];
    }
}
