using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Scripting;

public delegate bool TriggerFn(ScriptContext ctx, Node node);
public delegate void EffectFn(ScriptContext ctx, Node node);
/// <summary>Returns a number for a scope (e.g. <c>gold</c> of a country) or null if it does not apply.</summary>
public delegate double? ValueFn(ScriptContext ctx, IScope? scope);
public delegate IEnumerable<IScope> IteratorFn(ScriptContext ctx, IScope? scope);
public delegate IScope? LinkFn(ScriptContext ctx, IScope? scope);
public delegate string? DescribeFn(ScriptContext ctx, Node node);

/// <summary>
/// Runs Paradox-style triggers and effects. Everything is registered by name, so C# mods can add new
/// triggers, effects, numeric values, scope links and iterators:
/// <code>
/// Game.Script.RegisterEffect("add_prestige", (ctx, node) => ...);
/// Game.Script.RegisterValue("prestige", (ctx, scope) => (scope as Country)?.Vars.Get("prestige"));
/// </code>
/// </summary>
public sealed class ScriptEngine
{
    private readonly Game _game;
    private readonly Dictionary<string, TriggerFn> _triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EffectFn> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DescribeFn> _effectDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DescribeFn> _triggerDescriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ValueFn> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IteratorFn> _iterators = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkFn> _links = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> WeightKeys = new(StringComparer.OrdinalIgnoreCase) { "factor", "add", "base", "days", "months", "years" };
    private static readonly HashSet<string> IteratorReservedKeys = new(StringComparer.OrdinalIgnoreCase) { "limit", "count", "percent", "max", "order_by", "weight" };

    public ScriptEngine(Game game) { _game = game; }

    /// <summary>Set by the modifier system: total of a modifier key on a scope (used by <c>modifier:key</c>).</summary>
    public Func<IScope?, string, double>? ModifierLookup { get; set; }

    public IEnumerable<string> TriggerNames => _triggers.Keys;
    public IEnumerable<string> EffectNames => _effects.Keys;
    public IEnumerable<string> ValueNames => _values.Keys;
    public IEnumerable<string> IteratorNames => _iterators.Keys;
    public IEnumerable<string> LinkNames => _links.Keys;

    public void RegisterTrigger(string name, TriggerFn fn, DescribeFn? describe = null)
    {
        _triggers[name] = fn;
        if (describe != null) _triggerDescriptions[name] = describe;
    }

    public void RegisterEffect(string name, EffectFn fn, DescribeFn? describe = null)
    {
        _effects[name] = fn;
        if (describe != null) _effectDescriptions[name] = describe;
    }

    /// <summary>A numeric value usable in triggers (<c>gold &gt;= 100</c>) and as effect arguments.</summary>
    public void RegisterValue(string name, ValueFn fn) => _values[name] = fn;

    /// <summary>Registers <c>any_NAME</c>, <c>all_NAME</c>, <c>every_NAME</c> and <c>random_NAME</c>.</summary>
    public void RegisterIterator(string name, IteratorFn fn) => _iterators[name] = fn;

    /// <summary>A scope link such as <c>ruler</c>, <c>capital</c>, <c>owner</c>.</summary>
    public void RegisterLink(string name, LinkFn fn) => _links[name] = fn;

    /// <summary>Items of a registered iterator (e.g. "owned_state") for the current scope.</summary>
    public IEnumerable<IScope> Iterate(string iterator, ScriptContext ctx)
        => _iterators.TryGetValue(iterator, out var fn) ? fn(ctx, ctx.This) : Enumerable.Empty<IScope>();

    public bool HasValue(string name) => _values.ContainsKey(name);

    public bool HasTrigger(string name) => _triggers.ContainsKey(name);
    public bool HasEffect(string name) => _effects.ContainsKey(name);

    // =================================================================================================
    // Scope resolution
    // =================================================================================================

    /// <summary>
    /// Resolves a scope expression: <c>ROOT</c>, <c>THIS</c>, <c>PREV</c>, <c>scope:name</c>, a country tag
    /// (<c>GER</c>), <c>c:GER</c>, <c>state:key</c>, <c>location:key</c>, <c>character:id</c>, any registered link
    /// (<c>ruler</c>, <c>capital</c>, <c>owner</c>...) and dot paths (<c>ROOT.ruler.spouse</c>).
    /// </summary>
    /// <returns>True if the expression is a valid scope expression (the scope itself may still be null).</returns>
    public bool TryResolveScope(string expr, ScriptContext ctx, out IScope? scope)
    {
        scope = null;
        if (string.IsNullOrEmpty(expr)) return false;
        if (expr.Contains('.') && !char.IsDigit(expr[0]))
        {
            var parts = expr.Split('.');
            IScope? cur = ctx.This;
            bool first = true;
            foreach (var p in parts)
            {
                var sub = first ? ctx : ctx.Enter(cur);
                if (!TryResolveSingle(p, sub, out cur)) return false;
                first = false;
                if (cur == null) { scope = null; return true; }
            }
            scope = cur;
            return true;
        }
        return TryResolveSingle(expr, ctx, out scope);
    }

    private bool TryResolveSingle(string key, ScriptContext ctx, out IScope? scope)
    {
        scope = null;
        var w = _game.World;
        switch (key.ToLowerInvariant())
        {
            case "root": scope = ctx.Root; return true;
            case "this": scope = ctx.This; return true;
            case "prev": scope = ctx.Prev; return true;
            case "from": scope = ctx.Named("from") ?? ctx.Named("actor"); return true;
            case "player": scope = w.Player; return true;
        }
        int colon = key.IndexOf(':');
        if (colon > 0)
        {
            string prefix = key[..colon].ToLowerInvariant(), id = key[(colon + 1)..];
            switch (prefix)
            {
                case "scope": scope = ctx.Named(id); return true;
                case "c": case "country": case "tag": scope = w.GetCountry(id); return true;
                case "s": case "state": scope = w.GetState(id); return true;
                case "l": case "location": case "loc": case "p": case "province": scope = w.GetLocation(id); return true;
                case "char": case "character": scope = w.GetCharacter(id); return true;
                case "var": case "global_var": case "modifier": return false;
            }
        }
        if (key.Length == 3 && Country.IsValidTag(key) && w.CountryByTag.TryGetValue(key, out var c)) { scope = c; return true; }
        if (_links.TryGetValue(key, out var link)) { scope = link(ctx, ctx.This); return true; }
        if (ctx.Scopes.ContainsKey(key)) { scope = ctx.Scopes[key]; return true; }
        return false;
    }

    /// <summary>Resolves a number argument: a literal, <c>var:name</c>, a value name (<c>gold</c>) or a scoped value (<c>ROOT.gold</c>).</summary>
    public double ResolveNumber(string? s, ScriptContext ctx, double def = 0)
    {
        if (s == null) return def;
        if (Node.TryParseDouble(s, out var d)) return d;
        bool neg = s.StartsWith('-');
        string expr = neg ? s[1..] : s;
        double r = ResolveNumberExpr(expr, ctx, def, out bool ok);
        if (!ok) Log.ErrorOnce("num:" + s + ctx.Where, $"'{s}' is not a number, variable (var:name) or known value", ctx.Where);
        return neg ? -r : r;
    }

    private double ResolveNumberExpr(string expr, ScriptContext ctx, double def, out bool ok)
    {
        ok = true;
        IScope? scope = ctx.This;
        string name = expr;
        int dot = expr.LastIndexOf('.');
        if (dot > 0)
        {
            if (!TryResolveScope(expr[..dot], ctx, out scope)) { ok = false; return def; }
            name = expr[(dot + 1)..];
        }
        if (name.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
            return scope?.Vars.Get(name[4..], def) ?? def;
        if (name.StartsWith("modifier:", StringComparison.OrdinalIgnoreCase))
            return ModifierLookup?.Invoke(scope, name[9..]) ?? 0;
        if (name.StartsWith("global_var:", StringComparison.OrdinalIgnoreCase))
            return _game.World.Global.Get(name[11..], def);
        if (_values.TryGetValue(name, out var fn)) return fn(ctx.Enter(scope), scope) ?? def;
        ok = false;
        return def;
    }

    /// <summary>Resolves a scope given as an effect/trigger argument (value or <c>target = X</c>).</summary>
    public IScope? ArgScope(Node node, ScriptContext ctx, string key = "target")
    {
        string? expr = node.Value ?? node.GetString(key);
        if (expr == null)
        {
            Log.ErrorOnce("argscope:" + node.Location, $"'{node.Key}' needs a target (e.g. {node.Key} = ROOT)", node.Location);
            return null;
        }
        if (TryResolveScope(expr, ctx, out var s)) return s;
        Log.ErrorOnce("argscope2:" + node.Location + expr, $"Unknown scope '{expr}' in '{node.Key}'", node.Location);
        return null;
    }

    public Country? ArgCountry(Node node, ScriptContext ctx, string key = "target") => ScopeHelpers.CountryOf(ArgScope(node, ctx, key));

    // =================================================================================================
    // Triggers
    // =================================================================================================

    /// <summary>Evaluates a trigger block (all statements must be true). A null block is true.</summary>
    public bool EvalTrigger(Node? block, ScriptContext ctx, ISet<string>? ignoreKeys = null)
    {
        if (block?.Children == null) return true;
        foreach (var n in block.Children)
        {
            if (ignoreKeys != null && ignoreKeys.Contains(n.Key)) continue;
            if (!EvalStatement(n, ctx)) return false;
        }
        return true;
    }

    private bool EvalAny(Node block, ScriptContext ctx)
    {
        if (block.Children == null) return false;
        foreach (var n in block.Children)
            if (EvalStatement(n, ctx)) return true;
        return false;
    }

    public bool EvalStatement(Node n, ScriptContext ctx)
    {
        string key = n.Key;
        if (n.Op == Op.None)
        {
            if (n.Children != null) return EvalTrigger(n, ctx); // anonymous block = AND
            Log.ErrorOnce("trig-item:" + n.Location, $"Unexpected '{key}' in trigger (missing '= value'?)", n.Location);
            return false;
        }
        ctx.Where = n.Location;
        switch (key.ToLowerInvariant())
        {
            case "and": return EvalTrigger(n, ctx);
            case "or": return EvalAny(n, ctx);
            case "not": case "nor": return !EvalAny(n, ctx);
            case "nand": return !EvalTrigger(n, ctx);
            case "always": return n.AsBool();
            case "custom_tooltip":
            case "custom_trigger_tooltip":
                return n.Children == null || EvalTrigger(n, ctx, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tooltip", "text" });
            case "limit": return EvalTrigger(n, ctx);
        }

        if (_triggers.TryGetValue(key, out var trig)) return trig(ctx, n);

        // iterators: any_x / all_x
        if (TrySplitIterator(key, out var prefix, out var iterName) && (prefix == "any" || prefix == "all"))
        {
            var items = _iterators[iterName](ctx, ctx.This);
            if (prefix == "all")
            {
                foreach (var it in items)
                    if (!EvalTrigger(n, ctx.Enter(it), IteratorReservedKeys)) return false;
                return true;
            }
            var countNode = n.Get("count");
            var percentNode = n.Get("percent");
            if (countNode == null && percentNode == null)
            {
                foreach (var it in items)
                    if (EvalTrigger(n, ctx.Enter(it), IteratorReservedKeys)) return true;
                return false;
            }
            int matched = 0, total = 0;
            foreach (var it in items)
            {
                total++;
                if (EvalTrigger(n, ctx.Enter(it), IteratorReservedKeys)) matched++;
            }
            if (countNode != null)
            {
                if (string.Equals(countNode.Value, "all", StringComparison.OrdinalIgnoreCase)) return matched == total;
                return Node.Compare(matched, countNode.Op == Op.Eq ? Op.Ge : countNode.Op, ResolveNumber(countNode.Value, ctx, 1));
            }
            double pct = total == 0 ? 0 : matched / (double)total;
            double want = ResolveNumber(percentNode!.Value, ctx);
            if (want > 1) want /= 100.0;
            return Node.Compare(pct, percentNode.Op == Op.Eq ? Op.Ge : percentNode.Op, want);
        }

        // numeric values: gold >= 100, age > 30
        if (n.Value != null && _values.TryGetValue(key, out var valueFn))
        {
            var v = valueFn(ctx, ctx.This);
            if (v == null) return false;
            return Node.Compare(v.Value, n.Op, ResolveNumber(n.Value, ctx));
        }

        // scope switch / comparison
        if (TryResolveScope(key, ctx, out var scope))
        {
            if (n.Children != null)
            {
                if (scope == null) return false;
                return EvalTrigger(n, ctx.Enter(scope));
            }
            if (n.Value != null)
            {
                // owner = ROOT, ruler = scope:x
                if (!TryResolveScope(n.Value, ctx, out var other))
                {
                    Log.ErrorOnce("cmp:" + n.Location, $"'{n.Value}' is not a scope", n.Location);
                    return false;
                }
                bool eq = scope != null && ReferenceEquals(scope, other);
                return n.Op == Op.Ne ? !eq : eq;
            }
        }

        Log.ErrorOnce("unknown-trigger:" + n.Location, $"Unknown trigger '{key}'", n.Location);
        return false;
    }

    private bool TrySplitIterator(string key, out string prefix, out string name)
    {
        prefix = name = "";
        int us = key.IndexOf('_');
        if (us <= 0) return false;
        prefix = key[..us].ToLowerInvariant();
        name = key[(us + 1)..];
        if (prefix != "any" && prefix != "all" && prefix != "every" && prefix != "random" && prefix != "ordered") return false;
        return _iterators.ContainsKey(name);
    }

    /// <summary>For the UI: each top-level condition of a trigger block with its result.</summary>
    public List<(string Text, bool Ok)> ExplainTrigger(Node? block, ScriptContext ctx)
    {
        var result = new List<(string, bool)>();
        if (block?.Children == null) return result;
        foreach (var n in block.Children)
        {
            bool ok = EvalStatement(n, ctx);
            result.Add((DescribeTrigger(n, ctx), ok));
        }
        return result;
    }

    public string DescribeTrigger(Node n, ScriptContext ctx)
    {
        if (string.Equals(n.Key, "custom_tooltip", StringComparison.OrdinalIgnoreCase) || string.Equals(n.Key, "custom_trigger_tooltip", StringComparison.OrdinalIgnoreCase))
            return Localisation.Get(n.Value ?? n.GetString("tooltip") ?? n.GetString("text") ?? "");
        if (_triggerDescriptions.TryGetValue(n.Key, out var d))
        {
            var s = d(ctx, n);
            if (s != null) return s;
        }
        string k = n.Key.ToUpperInvariant() switch
        {
            "NOT" => "NOT",
            "OR" => "One of",
            "AND" => "All of",
            _ => Localisation.GetOrPretty(n.Key),
        };
        if (n.Children != null)
        {
            var inner = n.Children.Select(c => DescribeTrigger(c, ctx));
            return $"{k}: {string.Join(", ", inner)}";
        }
        string val = n.Value ?? "";
        if (TryResolveScope(val, ctx, out var s2) && s2 != null) val = s2.DisplayName;
        else if (string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase)) return k;
        else if (string.Equals(val, "no", StringComparison.OrdinalIgnoreCase)) return "Not " + k.ToLowerInvariant();
        return $"{k} {(n.Op == Op.Eq ? "is" : Node.OpToString(n.Op))} {Localisation.GetOrPretty(val)}";
    }

    // =================================================================================================
    // Effects
    // =================================================================================================

    public void Execute(Node? block, ScriptContext ctx, ISet<string>? ignoreKeys = null)
    {
        if (block?.Children == null) return;
        bool? lastIf = null; // result of the previous if/else_if chain
        foreach (var n in block.Children)
        {
            if (ignoreKeys != null && ignoreKeys.Contains(n.Key)) continue;
            string k = n.Key.ToLowerInvariant();
            if (k == "if")
            {
                lastIf = EvalTrigger(n.Get("limit"), ctx);
                if (lastIf.Value) Execute(n, ctx, LimitOnly);
                continue;
            }
            if (k == "else_if")
            {
                if (lastIf == null) { Log.ErrorOnce("elseif:" + n.Location, "'else_if' without 'if'", n.Location); continue; }
                if (lastIf.Value) continue;
                lastIf = EvalTrigger(n.Get("limit"), ctx);
                if (lastIf.Value) Execute(n, ctx, LimitOnly);
                continue;
            }
            if (k == "else")
            {
                if (lastIf == null) { Log.ErrorOnce("else:" + n.Location, "'else' without 'if'", n.Location); continue; }
                if (!lastIf.Value) Execute(n, ctx);
                lastIf = null;
                continue;
            }
            lastIf = null;
            ExecuteStatement(n, ctx);
        }
    }

    private static readonly HashSet<string> LimitOnly = new(StringComparer.OrdinalIgnoreCase) { "limit" };
    private static readonly HashSet<string> ChanceKeys = new(StringComparer.OrdinalIgnoreCase) { "chance", "limit" };

    public void ExecuteStatement(Node n, ScriptContext ctx)
    {
        if (n.Op == Op.None)
        {
            if (n.Children != null) { Execute(n, ctx); return; }
            Log.ErrorOnce("eff-item:" + n.Location, $"Unexpected '{n.Key}' in effect (missing '= value'?)", n.Location);
            return;
        }
        ctx.Where = n.Location;
        string key = n.Key;
        switch (key.ToLowerInvariant())
        {
            case "hidden_effect":
            case "effect":
                Execute(n, ctx);
                return;
            case "random":
                {
                    double chance = ResolveNumber(n.GetString("chance"), ctx, 50);
                    if (chance <= 1 && n.GetString("chance")?.Contains('.') == true) chance *= 100;
                    if (_game.Rng.NextDouble() * 100 < chance) Execute(n, ctx, ChanceKeys);
                    return;
                }
            case "random_list":
                ExecuteRandomList(n, ctx);
                return;
            case "custom_tooltip":
            case "custom_effect_tooltip":
                if (n.Children != null) Execute(n, ctx, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tooltip", "text" });
                return;
            case "limit":
                return;
        }

        if (_effects.TryGetValue(key, out var eff))
        {
            try { eff(ctx, n); }
            catch (Exception ex) { Log.Error($"Effect '{key}' failed: {ex.Message}", n.Location); }
            return;
        }

        if (TrySplitIterator(key, out var prefix, out var iterName) && prefix != "any" && prefix != "all")
        {
            var limit = n.Get("limit");
            var items = _iterators[iterName](ctx, ctx.This).Where(it => EvalTrigger(limit, ctx.Enter(it))).ToList();
            if (prefix == "random")
            {
                if (items.Count == 0) return;
                var pick = items[_game.Rng.Next(items.Count)];
                Execute(n, ctx.Enter(pick), IteratorReservedKeys);
            }
            else
            {
                int max = n.Has("max") ? (int)ResolveNumber(n.GetString("max"), ctx, int.MaxValue) : int.MaxValue;
                foreach (var it in items.Take(max)) Execute(n, ctx.Enter(it), IteratorReservedKeys);
            }
            return;
        }

        if (n.Children != null && TryResolveScope(key, ctx, out var scope))
        {
            if (scope != null) Execute(n, ctx.Enter(scope));
            return;
        }

        Log.ErrorOnce("unknown-effect:" + n.Location, $"Unknown effect '{key}'", n.Location);
    }

    private void ExecuteRandomList(Node n, ScriptContext ctx)
    {
        var options = new List<(double W, Node Block)>();
        foreach (var c in n.All)
        {
            if (c.Children == null) continue;
            if (!Node.TryParseDouble(c.Key, out double w))
            {
                Log.ErrorOnce("rl:" + c.Location, $"random_list entries must look like  50 = {{ ... }}  (got '{c.Key}')", c.Location);
                continue;
            }
            if (c.Has("trigger") && !EvalTrigger(c.Get("trigger"), ctx)) continue;
            foreach (var m in c.GetAll("modifier"))
                if (EvalTrigger(m, ctx, WeightKeys)) w = w * m.GetDouble("factor", 1) + m.GetDouble("add", 0);
            if (w > 0) options.Add((w, c));
        }
        double total = options.Sum(o => o.W);
        if (total <= 0) return;
        double r = _game.Rng.NextDouble() * total;
        foreach (var (w, block) in options)
        {
            r -= w;
            if (r <= 0)
            {
                Execute(block, ctx, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trigger", "modifier" });
                return;
            }
        }
        Execute(options[^1].Block, ctx, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trigger", "modifier" });
    }

    // =================================================================================================
    // Weights (mean_time_to_happen, ai_chance, ai_will_do, ai_acceptance)
    // =================================================================================================

    /// <summary>
    /// Evaluates a weight block: <c>{ base = 10 modifier = { factor = 2 add = 5 &lt;triggers&gt; } }</c>.
    /// <c>factor</c> at the top level is treated like <c>base</c> (Paradox style).
    /// </summary>
    public double EvalWeight(Node? block, ScriptContext ctx, double def = 1)
    {
        if (block == null) return def;
        if (block.Value != null) return ResolveNumber(block.Value, ctx, def);
        double v = block.Has("base") ? ResolveNumber(block.GetString("base"), ctx, def)
                 : block.Has("factor") ? ResolveNumber(block.GetString("factor"), ctx, def)
                 : def;
        foreach (var m in block.GetAll("modifier"))
        {
            if (!EvalTrigger(m, ctx, WeightKeys)) continue;
            if (m.Has("factor")) v *= ResolveNumber(m.GetString("factor"), ctx, 1);
            if (m.Has("add")) v += ResolveNumber(m.GetString("add"), ctx, 0);
        }
        return v;
    }

    /// <summary>Mean time to happen in days from a block with days/months/years and modifiers.</summary>
    public double EvalMtthDays(Node block, ScriptContext ctx)
    {
        double days = block.GetDouble("days", 0) + block.GetDouble("months", 0) * 30 + block.GetDouble("years", 0) * 365;
        if (days <= 0) days = 365;
        foreach (var m in block.GetAll("modifier"))
            if (EvalTrigger(m, ctx, WeightKeys))
                days *= ResolveNumber(m.GetString("factor"), ctx, 1);
        return Math.Max(1, days);
    }

    // =================================================================================================
    // Tooltips
    // =================================================================================================

    /// <summary>Human-readable description of an effect block (used for event option tooltips).</summary>
    public List<string> Describe(Node? block, ScriptContext ctx, int indent = 0, ISet<string>? ignoreKeys = null)
    {
        var lines = new List<string>();
        if (block?.Children == null) return lines;
        string pad = new(' ', indent * 2);
        foreach (var n in block.Children)
        {
            if (ignoreKeys != null && ignoreKeys.Contains(n.Key)) continue;
            string k = n.Key.ToLowerInvariant();
            if (k is "hidden_effect" or "limit" or "name" or "trigger" or "ai_chance" or "highlight" or "tooltip" or "text") continue;
            if (k is "custom_tooltip" or "custom_effect_tooltip")
            {
                lines.Add(pad + Localisation.Get(n.Value ?? n.GetString("tooltip") ?? n.GetString("text") ?? ""));
                continue;
            }
            if (k == "if" || k == "else_if")
            {
                var cond = n.Get("limit")?.Children?.Select(c => DescribeTrigger(c, ctx)) ?? Enumerable.Empty<string>();
                lines.Add($"{pad}{(k == "if" ? "If" : "Else if")} {string.Join(" and ", cond)}:");
                lines.AddRange(Describe(n, ctx, indent + 1, LimitOnly));
                continue;
            }
            if (k == "else")
            {
                lines.Add(pad + "Otherwise:");
                lines.AddRange(Describe(n, ctx, indent + 1));
                continue;
            }
            if (k == "random")
            {
                lines.Add($"{pad}{n.GetString("chance", "50")}% chance:");
                lines.AddRange(Describe(n, ctx, indent + 1, ChanceKeys));
                continue;
            }
            if (k == "random_list")
            {
                lines.Add(pad + "One of the following at random:");
                foreach (var c in n.All.Where(c => c.Children != null))
                {
                    lines.Add($"{pad}  ({c.Key}):");
                    lines.AddRange(Describe(c, ctx, indent + 2));
                }
                continue;
            }
            if (_effectDescriptions.TryGetValue(n.Key, out var d))
            {
                string? s = null;
                try { s = d(ctx, n); } catch { /* description must not crash UI */ }
                if (s != null) { lines.Add(pad + s); continue; }
            }
            if (TrySplitIterator(n.Key, out var prefix, out var iter))
            {
                lines.Add($"{pad}{(prefix == "random" ? "One random" : "Every")} {Localisation.Prettify(iter)}:");
                lines.AddRange(Describe(n, ctx, indent + 1, IteratorReservedKeys));
                continue;
            }
            if (n.Children != null && TryResolveScope(n.Key, ctx, out var scope))
            {
                lines.Add($"{pad}{scope?.DisplayName ?? n.Key}:");
                lines.AddRange(Describe(n, ctx.Enter(scope), indent + 1));
                continue;
            }
            if (n.Value != null)
            {
                string val = n.Value;
                if (TryResolveScope(val, ctx, out var vs) && vs != null) val = vs.DisplayName;
                lines.Add($"{pad}{Localisation.GetOrPretty(n.Key)}: {Localisation.Get(val)}");
            }
            else
            {
                lines.Add($"{pad}{Localisation.GetOrPretty(n.Key)}");
            }
        }
        return lines;
    }

    public static string FormatSigned(double v) => (v >= 0 ? "+" : "") + v.ToString("0.##", CultureInfo.InvariantCulture);
}
