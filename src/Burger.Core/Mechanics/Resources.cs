using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Mechanics;

/// <summary>A custom per-country number such as prestige, stability or legitimacy.</summary>
public sealed class ResourceDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    public double Start;
    public double? Min, Max;
    /// <summary>Script value block added every month (country scope).</summary>
    public Node? Monthly;
    public bool TopBar = true;
    public Rgb Color = new(230, 210, 150);
    public int Decimals;
    /// <summary>Applies to: country (default) or character.</summary>
    public ScopeType Scope = ScopeType.Country;
}

/// <summary>
/// Custom resources - new mechanics without C#. In <c>common/resources/*.txt</c>:
/// <code>
/// stability = {
///     name = "Stability"
///     start = 50
///     min = 0  max = 100
///     monthly = { value = 0.5  if = { limit = { is_at_war = yes } subtract = 1 } }
///     top_bar = yes
///     color = { 120 200 255 }
/// }
/// </code>
/// This automatically creates the value <c>stability</c> (triggers: <c>stability &gt;= 50</c>), the effects
/// <c>add_stability = 5</c> / <c>set_stability = 50</c>, a top bar display with tooltip, and the monthly change.
/// </summary>
public sealed class ResourceSystem
{
    private readonly Game _game;
    public readonly List<ResourceDef> Defs = new();

    public ResourceSystem(Game game) { _game = game; }

    public static string VarName(ResourceDef d) => d.Key;

    public void Load(GameData data)
    {
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/resources"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var d = new ResourceDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Start = n.GetDouble("start", 0),
                    Min = n.Has("min") ? n.GetDouble("min") : null,
                    Max = n.Has("max") ? n.GetDouble("max") : null,
                    Monthly = n.Get("monthly"),
                    TopBar = n.GetBool("top_bar", true),
                    Decimals = n.GetInt("decimals", 0),
                    Scope = string.Equals(n.GetString("scope"), "character", StringComparison.OrdinalIgnoreCase) ? ScopeType.Character : ScopeType.Country,
                };
                if (Rgb.TryParse(n.Get("color"), out var c)) d.Color = c;
                Defs.RemoveAll(x => string.Equals(x.Key, d.Key, StringComparison.OrdinalIgnoreCase));
                Defs.Add(d);
                Register(d);
            }
    }

    public double Get(IScope? s, ResourceDef d) => s?.Vars.Get(d.Key, d.Start) ?? 0;

    public void Set(IScope s, ResourceDef d, double v)
    {
        if (d.Min.HasValue) v = Math.Max(d.Min.Value, v);
        if (d.Max.HasValue) v = Math.Min(d.Max.Value, v);
        s.Vars.Set(d.Key, v);
    }

    /// <summary>Monthly change of a resource for a scope (for tooltips).</summary>
    public double MonthlyChange(IScope s, ResourceDef d)
        => d.Monthly == null ? 0 : _game.ScriptValues.Evaluate(d.Monthly, _game.Context(s));

    private void Register(ResourceDef d)
    {
        var s = _game.Script;
        IScope? Target(Scripting.ScriptContext ctx) => d.Scope == ScopeType.Character ? ctx.This as Character : Scripting.ScopeHelpers.CountryOf(ctx.This);
        s.RegisterValue(d.Key, (ctx, scope) =>
        {
            IScope? t = d.Scope == ScopeType.Character ? scope as Character : Scripting.ScopeHelpers.CountryOf(scope);
            return t == null ? null : Get(t, d);
        });
        s.RegisterEffect("add_" + d.Key, (ctx, n) =>
        {
            var t = Target(ctx);
            if (t == null) { Log.ErrorOnce("res:" + n.Location, $"add_{d.Key} needs a {(d.Scope == ScopeType.Character ? "character" : "country")} scope", n.Location); return; }
            Set(t, d, Get(t, d) + s.ResolveNumber(n.Value, ctx));
        }, (ctx, n) => $"{Localisation.Get(d.Name)}: {Scripting.ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("set_" + d.Key, (ctx, n) =>
        {
            var t = Target(ctx);
            if (t != null) Set(t, d, s.ResolveNumber(n.Value, ctx));
        }, (ctx, n) => $"{Localisation.Get(d.Name)} becomes {n.Value}");
    }

    public void MonthlyTick()
    {
        foreach (var d in Defs)
        {
            if (d.Monthly == null) continue;
            IEnumerable<IScope> scopes = d.Scope == ScopeType.Character
                ? _game.World.Characters.Where(c => c.Alive)
                : _game.World.Countries.Where(c => c.Exists);
            foreach (var sc in scopes.ToList()) Set(sc, d, Get(sc, d) + MonthlyChange(sc, d));
        }
    }

    /// <summary>Sets start values for scopes that do not have the resource yet.</summary>
    public void InitStartValues()
    {
        foreach (var d in Defs)
        {
            IEnumerable<IScope> scopes = d.Scope == ScopeType.Character ? _game.World.Characters : _game.World.Countries;
            foreach (var sc in scopes)
                if (!sc.Vars.Has(d.Key)) sc.Vars.Set(d.Key, d.Start);
        }
    }
}
