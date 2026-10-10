using Burger.Core.Data;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Mechanics;

/// <summary>
/// Script values (CK3 style): named formulas in <c>common/script_values/*.txt</c>.
/// <code>
/// war_tax = {
///     value = income                 # start value
///     multiply = 0.2
///     add = { value = num_of_wars multiply = 5 }
///     if = { limit = { is_at_war = yes } multiply = 2 }
///     every_owned_state = { add = tax }      # sums over every owned state
///     min = 0  max = 500                     # clamp
///     round = yes
/// }
/// my_constant = 12
/// </code>
/// Every script value becomes a number usable everywhere: <c>war_tax &gt; 10</c>, <c>add_gold = war_tax</c>,
/// <c>[ROOT.war_tax]</c> in text. Some names replace engine formulas (see <see cref="Formulas"/>).
/// </summary>
public sealed class ScriptValueSystem
{
    private readonly Game _game;
    private readonly Dictionary<string, Node> _defs = new(StringComparer.OrdinalIgnoreCase);
    private int _depth;

    /// <summary>Names of script values that replace a built-in engine formula when defined.</summary>
    public static readonly Dictionary<string, string> Formulas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["country_monthly_income"] = "Country scope. Gold earned per month (replaces the tax-based income).",
        ["country_monthly_expenses"] = "Country scope. Gold paid per month for armies.",
        ["country_monthly_manpower"] = "Country scope. Manpower gained per month.",
        ["max_diplomatic_relations"] = "Country scope. Number of alliances/guarantees/subjects allowed.",
        ["character_yearly_death_chance"] = "Character scope. 0-1 chance to die each year.",
        ["character_yearly_birth_chance"] = "Character scope (the mother). 0-1 chance of a child each year.",
        ["division_combat_power"] = "Country scope = owner. Multiplier on the combat damage of the owner's divisions (1 = normal).",
        ["division_org_recovery"] = "Country scope = owner. Daily organisation recovery as a fraction of max organisation.",
        ["location_war_value"] = "Location scope. How much war score holding this location is worth.",
        ["base_opinion"] = "Country scope = the country holding the opinion, scope:target = the other country. Added to opinion.",
    };

    public ScriptValueSystem(Game game) { _game = game; }

    public IEnumerable<string> Names => _defs.Keys;
    public bool Has(string name) => _defs.ContainsKey(name);

    public void Load(GameData data)
    {
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/script_values"))
            foreach (var n in root.All)
            {
                if (n.Op == Op.None) continue;
                Define(n.Key, n);
            }
    }

    /// <summary>Adds or replaces a script value (also usable from C#).</summary>
    public void Define(string name, Node definition)
    {
        _defs[name] = definition;
        _game.Script.RegisterValue(name, (ctx, scope) => Evaluate(definition, ctx.Enter(scope)));
    }

    /// <summary>Evaluates a named script value in a scope. Returns false if no such value is defined.</summary>
    public bool TryEval(string name, IScope? scope, out double value, Dictionary<string, IScope?>? scopes = null)
    {
        value = 0;
        if (!_defs.TryGetValue(name, out var def)) return false;
        var ctx = _game.Context(scope, scopes);
        value = Evaluate(def, ctx);
        return true;
    }

    /// <summary>Evaluates a value node: a number, a value name, or a formula block.</summary>
    public double Evaluate(Node node, ScriptContext ctx)
    {
        if (node.Value != null) return _game.Script.ResolveNumber(node.Value, ctx);
        if (node.Children == null) return 0;
        if (_depth > 40)
        {
            Log.ErrorOnce("sv-depth:" + node.Location, "Script value recursion is too deep (a value uses itself?)", node.Location);
            return 0;
        }
        _depth++;
        try
        {
            double acc = 0;
            Apply(node, ctx, ref acc);
            return acc;
        }
        finally { _depth--; }
    }

    private double Operand(Node n, ScriptContext ctx) => n.Value != null ? _game.Script.ResolveNumber(n.Value, ctx) : Evaluate(n, ctx);

    private static readonly HashSet<string> LimitKey = new(StringComparer.OrdinalIgnoreCase) { "limit" };

    private void Apply(Node block, ScriptContext ctx, ref double acc, ISet<string>? ignore = null)
    {
        bool? lastIf = null;
        foreach (var n in block.All)
        {
            if (ignore != null && ignore.Contains(n.Key)) continue;
            string k = n.Key.ToLowerInvariant();
            switch (k)
            {
                case "value": acc = Operand(n, ctx); lastIf = null; continue;
                case "add": acc += Operand(n, ctx); lastIf = null; continue;
                case "subtract": acc -= Operand(n, ctx); lastIf = null; continue;
                case "multiply": acc *= Operand(n, ctx); lastIf = null; continue;
                case "divide": { double d = Operand(n, ctx); if (Math.Abs(d) > 1e-12) acc /= d; lastIf = null; continue; }
                case "modulo": { double d = Operand(n, ctx); if (Math.Abs(d) > 1e-12) acc %= d; lastIf = null; continue; }
                case "min": acc = Math.Max(acc, Operand(n, ctx)); lastIf = null; continue;
                case "max": acc = Math.Min(acc, Operand(n, ctx)); lastIf = null; continue;
                case "abs": if (n.AsBool(true)) acc = Math.Abs(acc); continue;
                case "round": if (n.AsBool(true)) acc = Math.Round(acc); continue;
                case "floor": if (n.AsBool(true)) acc = Math.Floor(acc); continue;
                case "ceiling": if (n.AsBool(true)) acc = Math.Ceiling(acc); continue;
                case "desc": case "format": case "name": continue;
                case "if":
                    lastIf = _game.Script.EvalTrigger(n.Get("limit"), ctx);
                    if (lastIf.Value) Apply(n, ctx, ref acc, LimitKey);
                    continue;
                case "else_if":
                    if (lastIf == null || lastIf.Value) continue;
                    lastIf = _game.Script.EvalTrigger(n.Get("limit"), ctx);
                    if (lastIf.Value) Apply(n, ctx, ref acc, LimitKey);
                    continue;
                case "else":
                    if (lastIf == false) Apply(n, ctx, ref acc);
                    lastIf = null;
                    continue;
            }
            lastIf = null;
            // iterators: every_owned_state = { limit = { } add = tax }
            int us = k.IndexOf('_');
            if (us > 0 && n.Children != null && (k.StartsWith("every_") || k.StartsWith("any_")))
            {
                string iter = k[(us + 1)..];
                if (_game.Script.IteratorNames.Contains(iter, StringComparer.OrdinalIgnoreCase))
                {
                    var limit = n.Get("limit");
                    foreach (var item in IteratorItems(iter, ctx))
                    {
                        var sub = ctx.Enter(item);
                        if (_game.Script.EvalTrigger(limit, sub)) Apply(n, sub, ref acc, LimitKey);
                    }
                    continue;
                }
            }
            // scope switch: ruler = { add = martial }
            if (n.Children != null && _game.Script.TryResolveScope(n.Key, ctx, out var scope))
            {
                if (scope != null) Apply(n, ctx.Enter(scope), ref acc);
                continue;
            }
            Log.ErrorOnce("sv-key:" + n.Location, $"Unknown script value operation '{n.Key}' (use value, add, subtract, multiply, divide, min, max, round, if, every_...)", n.Location);
        }
    }

    private IEnumerable<IScope> IteratorItems(string iter, ScriptContext ctx)
    {
        // reuse the registered iterator through a tiny trigger evaluation trick: ask the engine for the list
        return _game.Script.Iterate(iter, ctx);
    }
}
