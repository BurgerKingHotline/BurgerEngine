using System.Text;

namespace Burger.Core.Script;

/// <summary>
/// Parser for the Paradox/Clausewitz-style text format used by every BurgerEngine data file:
/// <code>
/// # comment
/// key = value
/// key = "quoted value"
/// key = { child = 1 other = { 1 2 3 } }
/// gold &gt;= 100
/// color = rgb { 255 0 0 }
/// @my_constant = 10        # file-local constant, use as  value = @my_constant
/// </code>
/// Parse errors never throw; they are written to <see cref="Log"/> with file and line, and parsing continues.
/// </summary>
public static class ScriptParser
{
    private enum Tok { Word, String, LBrace, RBrace, Op, Eof }

    private struct Token
    {
        public Tok Type;
        public string Text;
        public Op Op;
        public int Line;
        public int Start, End;
    }

    private static readonly HashSet<string> TypedBlockPrefixes = new(StringComparer.OrdinalIgnoreCase) { "rgb", "hsv", "hsv360", "list" };

    public static Node ParseFile(string path, string? displayName = null)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Err($"Could not read file: {ex.Message}", displayName ?? path);
            return Node.Root(displayName ?? path);
        }
        return Parse(text, displayName ?? path);
    }

    [ThreadStatic] private static List<(string Message, string Where)>? _sink;

    private static void Err(string msg, string where)
    {
        if (_sink != null) _sink.Add((msg, where));
        else Log.Error(msg, where);
    }

    /// <summary>Parses without writing to the log; syntax errors are returned in <paramref name="errors"/> ("file:line").</summary>
    public static Node ParseQuiet(string text, string fileName, List<(string Message, string Where)>? errors = null)
    {
        var old = _sink;
        _sink = errors ?? new List<(string, string)>();
        try { return Parse(text, fileName); }
        finally { _sink = old; }
    }

    public static Node Parse(string text, string fileName)
    {
        var tokens = Tokenize(text, fileName);
        var root = Node.Root(fileName);
        var constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int pos = 0;
        ParseStatements(tokens, ref pos, root, fileName, constants, isRoot: true);
        return root;
    }

    private static List<Token> Tokenize(string s, string file)
    {
        var list = new List<Token>(s.Length / 4);
        int i = 0, line = 1;
        if (s.Length > 0 && s[0] == '﻿') i = 1; // BOM
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '{') { list.Add(new Token { Type = Tok.LBrace, Text = "{", Line = line, Start = i, End = i + 1 }); i++; continue; }
            if (c == '}') { list.Add(new Token { Type = Tok.RBrace, Text = "}", Line = line, Start = i, End = i + 1 }); i++; continue; }
            if (c == '=' || c == '<' || c == '>' || ((c == '!' || c == '?') && i + 1 < s.Length && s[i + 1] == '='))
            {
                int opStart = i;
                Op op;
                if (c == '=')
                {
                    op = Op.Eq;
                    i++;
                    if (i < s.Length && s[i] == '=') i++; // allow ==
                }
                else if (c == '<')
                {
                    if (i + 1 < s.Length && s[i + 1] == '=') { op = Op.Le; i += 2; } else { op = Op.Lt; i++; }
                }
                else if (c == '>')
                {
                    if (i + 1 < s.Length && s[i + 1] == '=') { op = Op.Ge; i += 2; } else { op = Op.Gt; i++; }
                }
                else if (c == '!') { op = Op.Ne; i += 2; }
                else { op = Op.SafeEq; i += 2; }
                list.Add(new Token { Type = Tok.Op, Op = op, Text = Node.OpToString(op), Line = line, Start = opStart, End = i });
                continue;
            }
            if (c == '"')
            {
                int startLine = line, strStart = i;
                var sb = new StringBuilder();
                i++;
                bool closed = false;
                while (i < s.Length)
                {
                    char d = s[i];
                    if (d == '\\' && i + 1 < s.Length)
                    {
                        char e = s[i + 1];
                        sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
                        i += 2;
                        continue;
                    }
                    if (d == '"') { closed = true; i++; break; }
                    if (d == '\n') line++;
                    sb.Append(d);
                    i++;
                }
                if (!closed) Err("Unterminated quoted string", $"{file}:{startLine}");
                list.Add(new Token { Type = Tok.String, Text = sb.ToString(), Line = startLine, Start = strStart, End = i });
                continue;
            }
            // bare word
            int start = i;
            while (i < s.Length)
            {
                char d = s[i];
                if (char.IsWhiteSpace(d) || d == '{' || d == '}' || d == '=' || d == '<' || d == '>' || d == '#' || d == '"') break;
                if ((d == '!' || d == '?') && i + 1 < s.Length && s[i + 1] == '=') break;
                i++;
            }
            if (i == start) { i++; continue; } // should not happen; skip the char
            list.Add(new Token { Type = Tok.Word, Text = s.Substring(start, i - start), Line = line, Start = start, End = i });
        }
        list.Add(new Token { Type = Tok.Eof, Text = "", Line = line, Start = s.Length, End = s.Length });
        return list;
    }

    private static void ParseStatements(List<Token> t, ref int pos, Node parent, string file, Dictionary<string, string> constants, bool isRoot)
    {
        while (true)
        {
            var tok = t[pos];
            if (tok.Type == Tok.Eof)
            {
                if (!isRoot) Err($"Missing closing '}}' for block '{parent.Key}' opened at line {parent.Line}", $"{file}:{tok.Line}");
                return;
            }
            if (tok.Type == Tok.RBrace)
            {
                pos++;
                if (isRoot)
                {
                    Err("Unexpected '}' (too many closing braces)", $"{file}:{tok.Line}");
                    continue;
                }
                return;
            }
            if (tok.Type == Tok.LBrace)
            {
                // anonymous block, e.g. inside a list of blocks
                pos++;
                var anon = new Node { Key = "", Op = Op.None, Children = new List<Node>(), File = file, Line = tok.Line, SpanStart = tok.Start };
                ParseStatements(t, ref pos, anon, file, constants, isRoot: false);
                anon.SpanEnd = t[pos - 1].End;
                parent.Children!.Add(anon);
                continue;
            }
            if (tok.Type == Tok.Op)
            {
                Err($"Unexpected operator '{tok.Text}' (missing key?)", $"{file}:{tok.Line}");
                pos++;
                // skip the value too
                if (t[pos].Type == Tok.Word || t[pos].Type == Tok.String) pos++;
                else if (t[pos].Type == Tok.LBrace) { pos++; SkipBlock(t, ref pos); }
                continue;
            }

            // word or string: key
            pos++;
            var node = new Node { Key = tok.Text, KeyQuoted = tok.Type == Tok.String, File = file, Line = tok.Line, SpanStart = tok.Start };
            if (t[pos].Type == Tok.Op)
            {
                node.Op = t[pos].Op;
                pos++;
                var v = t[pos];
                if (v.Type == Tok.LBrace)
                {
                    pos++;
                    node.Children = new List<Node>();
                    ParseStatements(t, ref pos, node, file, constants, isRoot: false);
                }
                else if (v.Type == Tok.Word || v.Type == Tok.String)
                {
                    pos++;
                    if (v.Type == Tok.Word && t[pos].Type == Tok.LBrace && TypedBlockPrefixes.Contains(v.Text))
                    {
                        pos++;
                        node.BlockType = v.Text.ToLowerInvariant();
                        node.Children = new List<Node>();
                        ParseStatements(t, ref pos, node, file, constants, isRoot: false);
                    }
                    else
                    {
                        string val = v.Text;
                        if (v.Type == Tok.Word && val.StartsWith('@') && val.Length > 1)
                        {
                            if (constants.TryGetValue(val, out var cv)) val = cv;
                            else Err($"Unknown constant '{val}'", $"{file}:{v.Line}");
                        }
                        node.Value = val;
                        node.ValueQuoted = v.Type == Tok.String;
                    }
                }
                else
                {
                    Err($"Missing value after '{node.Key} {Node.OpToString(node.Op)}'", $"{file}:{tok.Line}");
                    node.Value = "";
                }

                if (node.Key.StartsWith('@') && node.Value != null && isRoot)
                {
                    constants[node.Key] = node.Value;
                    continue; // constants are not part of the data
                }
            }
            else
            {
                node.Op = Op.None; // list item
                if (!node.KeyQuoted && node.Key.StartsWith('@') && constants.TryGetValue(node.Key, out var cv)) node.Key = cv;
            }
            node.SpanEnd = t[pos - 1].End;
            parent.Children!.Add(node);
        }
    }

    private static void SkipBlock(List<Token> t, ref int pos)
    {
        int depth = 1;
        while (depth > 0 && t[pos].Type != Tok.Eof)
        {
            if (t[pos].Type == Tok.LBrace) depth++;
            else if (t[pos].Type == Tok.RBrace) depth--;
            pos++;
        }
    }
}
