using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Mechanics;

/// <summary>One focus of a national focus tree.</summary>
public sealed class FocusDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    public FocusTreeDef Tree = null!;
    /// <summary>Days of work (the rule <see cref="FocusRules.Days"/> may change it).</summary>
    public int Days = 70;
    public int X, Y;
    /// <summary>Any one of these must be done first (HOI4 prerequisite = { focus = a focus = b }).</summary>
    public List<string> Prerequisite = new();
    /// <summary>All of these must be done first.</summary>
    public List<string> Requires = new();
    public List<string> MutuallyExclusive = new();
    public Node? Available;
    public Node? Reward;
    public Node? AiWillDo;
    public string File = "";
    public int Line;
}

/// <summary>A tree: the countries it belongs to (or <c>default = yes</c> for those without one) and its focuses.</summary>
public sealed class FocusTreeDef
{
    public string Key = "";
    public HashSet<string> Countries = new(StringComparer.OrdinalIgnoreCase);
    public bool Default;
    public List<FocusDef> Focuses = new();
}

/// <summary>
/// Overridable focus rules (the focus feature puts an editable copy in backend/Focus.cs).
/// </summary>
public class FocusRules
{
    /// <summary>Days a focus takes for a country: its days, faster with the modifier focus_speed (a fraction, 0.1 = 10% faster).</summary>
    public virtual double Days(Game game, Country c, FocusDef f)
        => Math.Max(7, f.Days / Math.Max(0.25, 1 + game.Modifiers.Sum(c, "focus_speed")));

    /// <summary>
    /// What the Historical Bias does to the weight the AI gives a focus: a country's own tree is its history, so its weights count in full at bias 1
    /// and a quarter at bias 0; the generic default tree is not history and is not scaled.
    /// </summary>
    public virtual double AiWeight(Game game, Country c, FocusDef f, double weight)
        => f.Tree.Default ? weight : weight * (0.25 + 0.75 * Math.Clamp(game.HistoricalBias, 0, 1));

    /// <summary>Chance per month that an idle AI country starts a focus at all (the AI otherwise picks one by its ai_will_do weights).</summary>
    public virtual double AiStartChance(Game game, Country c) => game.Data.Defines.Get("ai_focus_chance", 0.9);
}

/// <summary>
/// National focuses (HOI4 style), from <c>common/focuses/*.txt</c>. A focus is a goal a country works on for some days; completing it gives
/// its reward. They guide a country's course - an economic drive, a political turn, a demand made on a neighbour - they are not a script
/// and need not be a way to wage war. The engine allows everything a trigger or effect can do; <see cref="Lint"/> lists focuses that use date
/// checks (<c>date</c>, <c>year</c>, <c>month</c>) or make war / move land by force (<c>declare_war</c>, <c>annex_country</c>,
/// <c>transfer_state</c>, <c>set_owner</c>, <c>set_controller</c>, <c>white_peace</c>), for a game that wants to keep its trees to the
/// situation of the country and the world (the Earth1930 selftest does). A focus can <c>make_demand</c>: it puts a diplomatic action
/// (demand_state by default) to another country, which accepts or refuses by its own acceptance rules.
/// <code>
/// tree_germany = {
///     countries = { GER }               # or  default = yes
///     focus = {
///         id = reclaim_sudeten
///         days = 70   x = 2   y = 1
///         prerequisite = { focus = rearm }          # any one of them
///         requires = { focus = a focus = b }        # all of them
///         mutually_exclusive = { focus = other }
///         available = { ...trigger... }
///         completion_reward = { ...effect... }      # ROOT is the country
///         ai_will_do = { base = 1 modifier = { ... } }
///     }
/// }
/// </code>
/// </summary>
public sealed class FocusSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, FocusDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<FocusTreeDef> Trees = new();
    private readonly Dictionary<string, FocusTreeDef> _byCountry = new(StringComparer.OrdinalIgnoreCase);
    private FocusTreeDef? _default;

    private static readonly HashSet<string> ForbiddenTriggers = new(StringComparer.OrdinalIgnoreCase) { "date", "year", "month", "current_year", "current_date" };
    private static readonly HashSet<string> ForbiddenEffects = new(StringComparer.OrdinalIgnoreCase)
        { "declare_war", "annex_country", "transfer_state", "set_owner", "set_controller", "white_peace", "add_truce" };

    public FocusSystem(Game game) { _game = game; }

    public bool Enabled => _game.Features.Has(FeatureSet.Focus) && Defs.Count > 0;
    private FocusRules Rules => _game.Rules.Focus;

    private static string DoneFlag(string key) => "focus_done:" + key;
    private static string CurrentFlag(string key) => "focus_current:" + key;
    private const string ProgressVar = "focus_progress";

    public void Load(GameData data)
    {
        Defs.Clear();
        Trees.Clear();
        _byCountry.Clear();
        _default = null;
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/focuses"))
            foreach (var t in root.All)
            {
                if (t.Children == null) continue;
                var tree = new FocusTreeDef { Key = t.Key, Default = t.GetBool("default") };
                foreach (var c in t.GetList("countries")) tree.Countries.Add(c);
                foreach (var fn in t.GetAll("focus"))
                {
                    string id = fn.GetString("id", "")!;
                    if (id.Length == 0) { Log.Error("Focus without an id", fn.Location); continue; }
                    var f = new FocusDef
                    {
                        Key = id, Tree = tree,
                        Name = fn.GetString("name") ?? Localisation.GetOrPretty(id),
                        Desc = fn.GetString("desc", "")!,
                        Days = fn.GetInt("days", 70), X = fn.GetInt("x", 0), Y = fn.GetInt("y", 0),
                        Available = fn.Get("available"), Reward = fn.Get("completion_reward"), AiWillDo = fn.Get("ai_will_do"),
                        File = fn.File, Line = fn.Line,
                    };
                    if (fn.Get("prerequisite") is { } pre) foreach (var p in pre.GetAll("focus")) if (p.Value != null) f.Prerequisite.Add(p.Value);
                    if (fn.Get("requires") is { } req) foreach (var p in req.GetAll("focus")) if (p.Value != null) f.Requires.Add(p.Value);
                    if (fn.Get("mutually_exclusive") is { } mx) foreach (var p in mx.GetAll("focus")) if (p.Value != null) f.MutuallyExclusive.Add(p.Value);
                    if (Defs.ContainsKey(id)) { Log.Error($"Focus '{id}' is defined twice", fn.Location); continue; }
                    Defs[id] = f;
                    tree.Focuses.Add(f);
                }
                Trees.Add(tree);
                foreach (var c in tree.Countries) _byCountry[c] = tree;
                if (tree.Default) _default = tree;
            }
        foreach (var f in Defs.Values)
            foreach (var k in f.Prerequisite.Concat(f.Requires).Concat(f.MutuallyExclusive))
                if (!Defs.ContainsKey(k)) Log.Error($"Focus '{f.Key}': unknown focus '{k}'", $"{f.File}:{f.Line}");
    }

    /// <summary>Focuses that check the date or make war / move land by force (a lint for games that keep their trees to the situation, not the clock or the sword).</summary>
    public List<string> Lint()
    {
        var problems = new List<string>();
        foreach (var f in Defs.Values)
        {
            void Scan(Node? n, bool effect)
            {
                if (n == null) return;
                if (!effect && ForbiddenTriggers.Contains(n.Key)) problems.Add($"{f.Key}: date check '{n.Key}' ({n.Location})");
                if (effect && ForbiddenEffects.Contains(n.Key)) problems.Add($"{f.Key}: war or conquest effect '{n.Key}' ({n.Location})");
                if (n.Children != null) foreach (var c in n.Children) Scan(c, c.Key == "limit" ? false : effect);
            }
            Scan(f.Available, false);
            Scan(f.AiWillDo, false);
            Scan(f.Reward, true);
        }
        return problems;
    }

    /// <summary>Script check run by <c>burger validate</c>.</summary>
    public void Validate()
    {
        foreach (var f in Defs.Values)
        {
            ScriptValidator.Trigger(_game, f.Available);
            ScriptValidator.Effect(_game, f.Reward);
            ScriptValidator.Weight(_game, f.AiWillDo);
        }
    }

    public FocusTreeDef? TreeOf(Country c) => _byCountry.GetValueOrDefault(c.Tag) ?? _default;
    public IEnumerable<FocusDef> FocusesOf(Country c) => TreeOf(c)?.Focuses ?? Enumerable.Empty<FocusDef>();

    private ScriptContext Ctx(FocusDef f, Country c)
    {
        var ctx = _game.Context(c);
        ctx.Where = $"{f.File}:{f.Line}";
        return ctx;
    }

    public bool IsDone(Country c, string key) => c.Vars.HasFlag(DoneFlag(key));
    public FocusDef? Current(Country c) => FocusesOf(c).FirstOrDefault(f => c.Vars.HasFlag(CurrentFlag(f.Key)));
    public double Progress(Country c) => c.Vars.Get(ProgressVar);
    public int DoneCount(Country c) => FocusesOf(c).Count(f => IsDone(c, f.Key));

    /// <summary>Why a focus cannot be started now (null = it can).</summary>
    public string? CannotStart(Country c, FocusDef f)
    {
        if (IsDone(c, f.Key)) return "Already completed";
        if (Current(c) is { } cur) return cur == f ? "Already in progress" : $"Busy with {Localisation.Get(cur.Name)}";
        if (f.Prerequisite.Count > 0 && !f.Prerequisite.Any(p => IsDone(c, p))) return "Needs one of: " + string.Join(", ", f.Prerequisite.Select(NameOf));
        var missing = f.Requires.Where(r => !IsDone(c, r)).ToList();
        if (missing.Count > 0) return "Needs: " + string.Join(", ", missing.Select(NameOf));
        foreach (var x in f.MutuallyExclusive) if (IsDone(c, x)) return $"Excluded by {NameOf(x)}";
        foreach (var other in FocusesOf(c)) if (other.MutuallyExclusive.Contains(f.Key) && IsDone(c, other.Key)) return $"Excluded by {Localisation.Get(other.Name)}";
        if (!_game.Script.EvalTrigger(f.Available, Ctx(f, c))) return "Not possible now";
        return null;
    }

    private string NameOf(string key) => Defs.TryGetValue(key, out var f) ? Localisation.Get(f.Name) : key;

    public List<(string Text, bool Ok)> Requirements(Country c, FocusDef f) => _game.Script.ExplainTrigger(f.Available, Ctx(f, c));
    public List<string> DescribeReward(Country c, FocusDef f) => _game.Script.Describe(f.Reward, Ctx(f, c));

    public string? Start(Country c, FocusDef f)
    {
        var why = CannotStart(c, f);
        if (why != null) return why;
        c.Vars.SetFlag(CurrentFlag(f.Key));
        c.Vars.Set(ProgressVar, 0);
        return null;
    }

    public void Cancel(Country c)
    {
        if (Current(c) is not { } cur) return;
        c.Vars.ClearFlag(CurrentFlag(cur.Key));
        c.Vars.Set(ProgressVar, 0);
    }

    private void Complete(Country c, FocusDef f)
    {
        c.Vars.ClearFlag(CurrentFlag(f.Key));
        c.Vars.Set(ProgressVar, 0);
        c.Vars.SetFlag(DoneFlag(f.Key));
        _game.Script.Execute(f.Reward, Ctx(f, c));
        _game.Events.FireOnAction("on_focus_completed", c);
        if (c.IsPlayer) _game.Notify($"Focus completed: {Localisation.Get(f.Name)}", NotificationKind.Good);
        else _game.NotifyPlayer($"{c.Name}: {Localisation.Get(f.Name)}", NotificationKind.Info);
        _game.RaiseMapChanged();
    }

    /// <summary>Monthly: work on the current focus, and an idle AI picks the next.</summary>
    public void MonthlyTick()
    {
        if (!Enabled) return;
        foreach (var c in _game.World.Countries)
        {
            if (!c.Exists || TreeOf(c) == null) continue;
            if (Current(c) is { } cur)
            {
                c.Vars.Set(ProgressVar, c.Vars.Get(ProgressVar) + 30);
                if (c.Vars.Get(ProgressVar) >= Rules.Days(_game, c, cur)) Complete(c, cur);
            }
            if (c.IsAI && Current(c) == null) AiPick(c);
        }
    }

    private void AiPick(Country c)
    {
        if (_game.Rng.NextDouble() > Rules.AiStartChance(_game, c)) return;
        var options = new List<(FocusDef F, double W)>();
        foreach (var f in FocusesOf(c))
        {
            if (CannotStart(c, f) != null) continue;
            double w = f.AiWillDo == null ? 1 : _game.Script.EvalWeight(f.AiWillDo, Ctx(f, c), 0);
            w = Rules.AiWeight(_game, c, f, w);
            if (w > 0) options.Add((f, w));
        }
        if (options.Count == 0) return;
        double r = _game.Rng.NextDouble() * options.Sum(o => o.W);
        foreach (var (f, w) in options)
        {
            r -= w;
            if (r <= 0) { if (Environment.GetEnvironmentVariable("BURGER_FOCUS_LOG") != null) Console.WriteLine($"PICK {_game.World.Date.ToDisplay()} {c.Tag} {f.Key} weight {w:0.00} of {options.Sum(o => o.W):0.0}"); Start(c, f); return; }
        }
        Start(c, options[^1].F);
    }
}
