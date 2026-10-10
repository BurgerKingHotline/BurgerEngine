using System.Globalization;
using System.Text;

namespace Burger.Core.Script;

/// <summary>Comparison / assignment operator used in a script statement.</summary>
public enum Op
{
    /// <summary>No operator: the node is a bare list item such as <c>GER</c> inside <c>{ GER FRA }</c>.</summary>
    None,
    Eq,
    Lt,
    Gt,
    Le,
    Ge,
    Ne,
    /// <summary><c>?=</c> - "equals if the scope exists" (treated like <c>=</c>).</summary>
    SafeEq,
}

/// <summary>
/// One statement of a Paradox-style script file.
/// <para><c>key = value</c> → <see cref="Value"/> is set.</para>
/// <para><c>key = { ... }</c> → <see cref="Children"/> is set.</para>
/// <para><c>value</c> (bare item inside a list) → <see cref="Op"/> is <see cref="Op.None"/> and only <see cref="Key"/> is set.</para>
/// <para><c>{ ... }</c> (anonymous block inside a list) → <see cref="Key"/> is empty and <see cref="Children"/> is set.</para>
/// </summary>
public sealed class Node
{
    public string Key = "";
    public Op Op = Op.None;
    public string? Value;
    public List<Node>? Children;
    /// <summary>For typed blocks like <c>color = rgb { 1 2 3 }</c> this holds "rgb".</summary>
    public string? BlockType;
    public bool KeyQuoted;
    public bool ValueQuoted;
    public string File = "";
    public int Line;
    /// <summary>Character offsets of this statement in the parsed text (end exclusive); -1 if the node was not parsed.
    /// Used by <see cref="ScriptPatch"/> to edit files without losing comments or formatting.</summary>
    public int SpanStart = -1, SpanEnd = -1;

    public Node() { }

    public Node(string key, string value)
    {
        Key = key;
        Op = Op.Eq;
        Value = value;
    }

    public Node(string key, List<Node> children)
    {
        Key = key;
        Op = Op.Eq;
        Children = children;
    }

    public bool IsBlock => Children != null;
    public bool IsListItem => Op == Op.None && Children == null;
    public string Location => $"{File}:{Line}";

    /// <summary>Creates a root node that holds statements.</summary>
    public static Node Root(string file) => new() { Key = "", Children = new List<Node>(), File = file, Line = 0 };

    // ---------------------------------------------------------------------------------------------
    // Query helpers. All key comparisons are case-insensitive.
    // ---------------------------------------------------------------------------------------------

    public IEnumerable<Node> All => Children ?? (IEnumerable<Node>)Array.Empty<Node>();

    public Node? Get(string key)
    {
        if (Children == null) return null;
        foreach (var c in Children)
            if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase) && c.Op != Op.None) return c;
        return null;
    }

    public IEnumerable<Node> GetAll(string key)
    {
        if (Children == null) yield break;
        foreach (var c in Children)
            if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase) && c.Op != Op.None) yield return c;
    }

    public bool Has(string key) => Get(key) != null;

    public string? GetString(string key, string? def = null) => Get(key)?.Value ?? def;

    public double GetDouble(string key, double def = 0)
    {
        var n = Get(key);
        if (n?.Value == null) return def;
        return TryParseDouble(n.Value, out var d) ? d : def;
    }

    public int GetInt(string key, int def = 0) => (int)Math.Round(GetDouble(key, def));

    public bool GetBool(string key, bool def = false)
    {
        var v = Get(key)?.Value;
        if (v == null) return def;
        return ParseBool(v, def);
    }

    /// <summary>Returns the bare items of a block, e.g. <c>cores = { GER FRA }</c> → [GER, FRA].</summary>
    public List<string> GetList(string key)
    {
        var result = new List<string>();
        foreach (var n in GetAll(key))
        {
            if (n.Children != null)
            {
                foreach (var c in n.Children)
                    if (c.IsListItem) result.Add(c.Key);
            }
            else if (n.Value != null)
            {
                result.Add(n.Value);
            }
        }
        return result;
    }

    /// <summary>Items of this block that are bare list values.</summary>
    public List<string> Items()
    {
        var result = new List<string>();
        if (Children == null) return result;
        foreach (var c in Children)
            if (c.IsListItem) result.Add(c.Key);
        return result;
    }

    /// <summary>Numeric value of this node (for <c>key = 5</c>). Returns <paramref name="def"/> if not numeric.</summary>
    public double AsDouble(double def = 0) => Value != null && TryParseDouble(Value, out var d) ? d : def;

    public bool AsBool(bool def = false) => Value == null ? def : ParseBool(Value, def);

    public static bool ParseBool(string v, bool def = false)
    {
        switch (v.Trim().ToLowerInvariant())
        {
            case "yes": case "true": case "1": case "on": return true;
            case "no": case "false": case "0": case "off": return false;
            default: return def;
        }
    }

    public static bool TryParseDouble(string s, out double d)
    {
        s = s.Trim();
        if (s.EndsWith("%")) // allow 50% → 0.5
        {
            if (double.TryParse(s[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out d)) { d /= 100.0; return true; }
            return false;
        }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    /// <summary>Deep copy.</summary>
    public Node Clone()
    {
        var n = (Node)MemberwiseClone();
        if (Children != null) n.Children = Children.Select(c => c.Clone()).ToList();
        return n;
    }

    public void Add(Node child)
    {
        Children ??= new List<Node>();
        child.File = string.IsNullOrEmpty(child.File) ? File : child.File;
        Children.Add(child);
    }

    public Node Add(string key, string value, bool quoted = false)
    {
        var n = new Node(key, value) { ValueQuoted = quoted, File = File };
        Add(n);
        return n;
    }

    public Node AddBlock(string key)
    {
        var n = new Node(key, new List<Node>()) { File = File };
        Add(n);
        return n;
    }

    public void AddItem(string item)
    {
        Add(new Node { Key = item, Op = Op.None, File = File });
    }

    public static string OpToString(Op op) => op switch
    {
        Op.Eq => "=",
        Op.Lt => "<",
        Op.Gt => ">",
        Op.Le => "<=",
        Op.Ge => ">=",
        Op.Ne => "!=",
        Op.SafeEq => "?=",
        _ => "",
    };

    /// <summary>Compares two numbers using the given operator (= means equal).</summary>
    public static bool Compare(double a, Op op, double b) => op switch
    {
        Op.Lt => a < b,
        Op.Gt => a > b,
        Op.Le => a <= b,
        Op.Ge => a >= b,
        Op.Ne => Math.Abs(a - b) > 1e-9,
        _ => Math.Abs(a - b) < 1e-9,
    };

    public override string ToString()
    {
        var sb = new StringBuilder();
        ScriptWriter.WriteNode(sb, this, 0);
        return sb.ToString();
    }
}
