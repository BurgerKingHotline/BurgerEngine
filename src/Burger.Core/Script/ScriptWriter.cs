using System.Globalization;
using System.Text;

namespace Burger.Core.Script;

/// <summary>Writes <see cref="Node"/> trees back to the text format (used by the map editor and tools).</summary>
public static class ScriptWriter
{
    public static string Write(Node root)
    {
        var sb = new StringBuilder();
        if (root.Children != null)
            foreach (var c in root.Children)
                WriteNode(sb, c, 0);
        return sb.ToString();
    }

    public static void WriteFile(string path, Node root, string? header = null)
    {
        var sb = new StringBuilder();
        if (header != null)
        {
            foreach (var line in header.Split('\n'))
                sb.Append("# ").Append(line.TrimEnd('\r')).Append('\n');
            sb.Append('\n');
        }
        sb.Append(Write(root));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static void WriteNode(StringBuilder sb, Node n, int indent)
    {
        sb.Append('\t', indent);
        if (n.Op == Op.None && n.Children == null)
        {
            sb.Append(Quote(n.Key, n.KeyQuoted)).Append('\n');
            return;
        }
        if (n.Op == Op.None && n.Children != null)
        {
            WriteBlockBody(sb, n, indent);
            return;
        }
        sb.Append(Quote(n.Key, n.KeyQuoted)).Append(' ').Append(Node.OpToString(n.Op)).Append(' ');
        if (n.Children != null)
        {
            if (n.BlockType != null) sb.Append(n.BlockType).Append(' ');
            WriteBlockBody(sb, n, indent);
        }
        else
        {
            sb.Append(Quote(n.Value ?? "", n.ValueQuoted)).Append('\n');
        }
    }

    private static void WriteBlockBody(StringBuilder sb, Node n, int indent)
    {
        var kids = n.Children!;
        // short lists of bare items go on one line: { 1 2 3 }
        if (kids.Count > 0 && kids.Count <= 12 && kids.All(k => k.IsListItem))
        {
            sb.Append("{ ");
            foreach (var k in kids) sb.Append(Quote(k.Key, k.KeyQuoted)).Append(' ');
            sb.Append("}\n");
            return;
        }
        if (kids.Count == 0)
        {
            sb.Append("{ }\n");
            return;
        }
        sb.Append("{\n");
        foreach (var k in kids) WriteNode(sb, k, indent + 1);
        sb.Append('\t', indent).Append("}\n");
    }

    public static string Quote(string s, bool forceQuote = false)
    {
        if (!forceQuote && s.Length > 0 && !NeedsQuotes(s)) return s;
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }

    private static bool NeedsQuotes(string s)
    {
        foreach (char c in s)
            if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '=' || c == '<' || c == '>' || c == '#' || c == '"' || c == '!' || c == '?')
                return true;
        return false;
    }

    public static string Num(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
}
