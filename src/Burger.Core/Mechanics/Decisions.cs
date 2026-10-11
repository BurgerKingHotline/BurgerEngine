using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Mechanics;

/// <summary>A decision a country can take (EU4/CK3 style) from <c>common/decisions/*.txt</c>.</summary>
public sealed class DecisionDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    public string Category = "";
    public string? Picture;
    public Node? Potential;
    public Node? Allow;
    public Node? Effect;
    public Node? AiWillDo;
    public double CostGold;
    public int CooldownDays;
    /// <summary>If false (default) the decision disappears after being taken once.</summary>
    public bool Repeatable;
    public string File = "";
    public int Line;
}

/// <summary>
/// Decisions: one-click actions a country can take. In <c>common/decisions/*.txt</c>:
/// <code>
/// proclaim_empire = {
///     name = "Proclaim the Empire"
///     desc = "Our realm is great enough to call itself an empire."
///     potential = { NOT = { government = empire } }       # shown if
///     allow = { num_of_states >= 8 gold >= 200 }            # clickable if (each line is shown)
///     effect = { set_government = empire add_gold = -200 set_country_name = "[ROOT] Empire" }
///     ai_will_do = { base = 1 }
///     repeatable = no        cooldown_days = 0     cost = 0
/// }
/// </code>
/// ROOT is the country taking the decision.
/// </summary>
public sealed class DecisionSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, DecisionDef> Defs = new(StringComparer.OrdinalIgnoreCase);

    public DecisionSystem(Game game) { _game = game; }

    public void Load(GameData data)
    {
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/decisions"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                Defs[n.Key] = new DecisionDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Category = n.GetString("category", "")!,
                    Picture = n.GetString("picture"),
                    Potential = n.Get("potential"),
                    Allow = n.Get("allow"),
                    Effect = n.Get("effect"),
                    AiWillDo = n.Get("ai_will_do"),
                    CostGold = n.GetDouble("cost", 0),
                    CooldownDays = n.GetInt("cooldown_days", 0),
                    Repeatable = n.GetBool("repeatable", false),
                    File = n.File,
                    Line = n.Line,
                };
            }
    }

    private static string TakenFlag(DecisionDef d) => "decision_taken:" + d.Key;
    private static string LastVar(DecisionDef d) => "decision_day:" + d.Key;

    private ScriptContext Ctx(DecisionDef d, Country c)
    {
        var ctx = _game.Context(c);
        ctx.Where = $"{d.File}:{d.Line}";
        return ctx;
    }

    public bool IsVisible(DecisionDef d, Country c)
    {
        if (!d.Repeatable && c.Vars.HasFlag(TakenFlag(d))) return false;
        return _game.Script.EvalTrigger(d.Potential, Ctx(d, c));
    }

    public List<(string Text, bool Ok)> Requirements(DecisionDef d, Country c)
    {
        var list = _game.Script.ExplainTrigger(d.Allow, Ctx(d, c));
        double cost = _game.Features.Has(Burger.Core.Modding.FeatureSet.Economy) ? d.CostGold : 0;
        if (cost > 0) list.Add(($"Costs {cost:0} gold", c.Gold >= cost));
        int left = CooldownLeft(d, c);
        if (left > 0) list.Add(($"Available again in {left} days", false));
        return list;
    }

    public int CooldownLeft(DecisionDef d, Country c)
    {
        if (d.CooldownDays <= 0 || !c.Vars.Has(LastVar(d))) return 0;
        return Math.Max(0, (int)c.Vars.Get(LastVar(d)) + d.CooldownDays - _game.World.Date.TotalDays);
    }

    public bool CanTake(DecisionDef d, Country c) => IsVisible(d, c) && Requirements(d, c).All(r => r.Ok);

    public List<string> DescribeEffect(DecisionDef d, Country c) => _game.Script.Describe(d.Effect, Ctx(d, c));

    public bool Take(DecisionDef d, Country c, bool force = false)
    {
        if (!force && !CanTake(d, c)) return false;
        if (_game.Features.Has(Burger.Core.Modding.FeatureSet.Economy)) c.Gold -= d.CostGold;
        c.Vars.SetFlag(TakenFlag(d));
        c.Vars.Set(LastVar(d), _game.World.Date.TotalDays);
        if (Environment.GetEnvironmentVariable("BURGER_DECISION_LOG") is { } dl && d.Key.Contains(dl)) Console.WriteLine($"DECISION {_game.World.Date.ToDisplay()} {c.Tag}: {d.Key} [{(_game.Features.Has(Burger.Core.Modding.FeatureSet.Demilitarization) ? string.Join(",", _game.Diplomacy.DemilitarizedOf(c).Select(x => x.Key + " by " + _game.Diplomacy.ImposedBy(x)?.Tag)) : "")}]");
        _game.Script.Execute(d.Effect, Ctx(d, c));
        _game.Events.FireOnAction("on_decision_taken", c);
        if (!c.IsPlayer && _game.World.Player != null)
            _game.NotifyPlayer($"{c.Name}: {Localisation.Get(d.Name)}", NotificationKind.Info);
        _game.RaiseMapChanged();
        return true;
    }

    /// <summary>AI takes decisions it wants (monthly).</summary>
    public void AiMonthly(Country c)
    {
        foreach (var d in Defs.Values)
        {
            if (d.AiWillDo == null || !CanTake(d, c)) continue;
            double w = _game.Script.EvalWeight(d.AiWillDo, Ctx(d, c), 0);
            if (w > 0 && _game.Rng.NextDouble() < Math.Min(1, w * _game.Data.Defines.Get("ai_decision_speed", 0.1))) { Take(d, c); return; }
        }
    }
}
