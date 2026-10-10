using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Mechanics;

/// <summary>A named bundle of modifier values from <c>common/modifiers/*.txt</c>.</summary>
public sealed class ModifierDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    public string? Icon;
    public bool Hidden;
    public Dictionary<string, double> Values = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>For conditional modifiers: applied automatically while this is true (checked monthly).</summary>
    public Node? Trigger;
    /// <summary>country, state, location or character - which scopes a conditional modifier is checked for.</summary>
    public ScopeType AutoScope = ScopeType.None;
    public bool IsConditional => Trigger != null;
}

public sealed class ActiveModifier
{
    public ModifierDef Def = null!;
    public GameDate? Expires;
    /// <summary>True for conditional modifiers added automatically by their trigger.</summary>
    public bool Auto;
}

/// <summary>
/// Modifiers (EU4/HOI4/Imperator style). Define them in <c>common/modifiers/*.txt</c>:
/// <code>
/// war_economy = { name = "War Economy" income = -0.1 manpower = 0.25 attack = 0.05 }
/// overextended = { trigger = { num_of_states > 10 } scope = country  income = -0.2 }   # automatic while true
/// </code>
/// Give and take them with <c>add_modifier = { name = war_economy years = 2 }</c> / <c>remove_modifier = war_economy</c>,
/// check with <c>has_modifier = war_economy</c>, read totals with <c>modifier:income</c> in any script value.
/// <para>Built-in keys used by the engine: <c>income</c>, <c>upkeep</c>, <c>manpower</c>, <c>tax</c> (state),
/// <c>attack</c>, <c>defense</c>, <c>org_recovery</c>, <c>speed</c>, <c>opinion</c> (others' opinion of the country),
/// <c>relations</c> (extra diplomatic relations). Any other key is yours to use from script values or C#.</para>
/// </summary>
public sealed class ModifierSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, ModifierDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<IScope, List<ActiveModifier>> Active = new(ReferenceEqualityComparer.Instance);

    public ModifierSystem(Game game) { _game = game; }

    /// <summary>Extra modifier values computed by systems (politics: ruling ideology, laws, ministers; population:
    /// buildings, culture and religion). Each returns its total for a scope and key.</summary>
    public readonly List<Func<IScope, string, double>> Providers = new();

    public void Load(GameData data)
    {
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/modifiers"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var d = new ModifierDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Icon = n.GetString("icon"),
                    Hidden = n.GetBool("hidden"),
                    Trigger = n.Get("trigger"),
                };
                string scope = (n.GetString("scope") ?? "country").ToLowerInvariant();
                if (d.Trigger != null)
                    d.AutoScope = scope switch
                    {
                        "state" => ScopeType.State,
                        "location" => ScopeType.Location,
                        "character" => ScopeType.Character,
                        _ => ScopeType.Country,
                    };
                foreach (var c in n.All)
                    if (c.Value != null && c.Key is not ("name" or "desc" or "icon" or "hidden" or "scope") && Node.TryParseDouble(c.Value, out var v))
                        d.Values[c.Key] = v;
                Defs[d.Key] = d;
            }
    }

    public IReadOnlyList<ActiveModifier> Of(IScope scope)
        => Active.TryGetValue(scope, out var l) ? l : (IReadOnlyList<ActiveModifier>)Array.Empty<ActiveModifier>();

    public bool Has(IScope scope, string key) => Of(scope).Any(m => string.Equals(m.Def.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds (or refreshes) a modifier. days = null → permanent.</summary>
    public bool Add(IScope scope, string key, int? days = null, bool auto = false)
    {
        if (!Defs.TryGetValue(key, out var def))
        {
            Log.ErrorOnce("nomod:" + key, $"Unknown modifier '{key}' (define it in common/modifiers)");
            return false;
        }
        if (!Active.TryGetValue(scope, out var list)) Active[scope] = list = new List<ActiveModifier>();
        GameDate? exp = days.HasValue ? _game.World.Date.AddDays(days.Value) : null;
        var existing = list.FirstOrDefault(m => m.Def == def);
        if (existing != null)
        {
            if (exp == null || existing.Expires == null) existing.Expires = exp;
            else if (exp > existing.Expires) existing.Expires = exp;
            return true;
        }
        list.Add(new ActiveModifier { Def = def, Expires = exp, Auto = auto });
        return true;
    }

    public void Remove(IScope scope, string key)
    {
        if (Active.TryGetValue(scope, out var list)) list.RemoveAll(m => string.Equals(m.Def.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Total of a modifier key on a scope (characters also count their traits).</summary>
    public double Sum(IScope? scope, string key)
    {
        if (scope == null) return 0;
        double v = 0;
        if (Active.TryGetValue(scope, out var list))
            foreach (var m in list)
                if (m.Def.Values.TryGetValue(key, out var x)) v += x;
        if (scope is Character ch) v += ch.TraitModifier(key);
        foreach (var p in Providers) v += p(scope, key);
        return v;
    }

    /// <summary>1 + Sum (handy multiplier).</summary>
    public double Factor(IScope? scope, string key) => 1 + Sum(scope, key);

    public void DailyTick()
    {
        var now = _game.World.Date;
        foreach (var list in Active.Values)
            list.RemoveAll(m => m.Expires.HasValue && m.Expires.Value <= now);
    }

    /// <summary>Applies / removes conditional modifiers (monthly).</summary>
    public void MonthlyTick()
    {
        var conditional = Defs.Values.Where(d => d.IsConditional).ToList();
        if (conditional.Count == 0) return;
        foreach (var def in conditional)
        {
            IEnumerable<IScope> scopes = def.AutoScope switch
            {
                ScopeType.State => _game.World.States.Where(s => s.Owner != null),
                ScopeType.Location => _game.World.Locations.Where(l => l.IsLand),
                ScopeType.Character => _game.World.Characters.Where(c => c.Alive),
                _ => _game.World.Countries.Where(c => c.Exists),
            };
            foreach (var s in scopes)
            {
                bool on = _game.Script.EvalTrigger(def.Trigger, _game.Context(s));
                bool has = Of(s).Any(m => m.Def == def);
                if (on && !has) Add(s, def.Key, null, auto: true);
                else if (!on && has) Remove(s, def.Key);
            }
        }
    }

    public string Describe(ModifierDef d)
        => string.Join("\n", d.Values.Select(kv => $"{Localisation.GetOrPretty(kv.Key)}: {(kv.Value >= 0 ? "+" : "")}{kv.Value:0.##}"));

    internal void Register()
    {
        var s = _game.Script;
        s.RegisterEffect("add_modifier", (ctx, n) =>
        {
            if (ctx.This == null) return;
            string? key = n.Value ?? n.GetString("name") ?? n.GetString("modifier");
            if (key == null) { Log.ErrorOnce("am:" + n.Location, "add_modifier needs a name", n.Location); return; }
            int? days = null;
            if (n.Children != null)
            {
                double d = s.ResolveNumber(n.GetString("days") ?? "0", ctx) + 30 * s.ResolveNumber(n.GetString("months") ?? "0", ctx) + 365 * s.ResolveNumber(n.GetString("years") ?? "0", ctx);
                if (d > 0) days = (int)d;
            }
            Add(ctx.This, key, days);
        }, (ctx, n) =>
        {
            string? key = n.Value ?? n.GetString("name");
            if (key == null || !Defs.TryGetValue(key, out var d)) return null;
            string dur = n.Has("years") ? $" for {n.GetString("years")} years" : n.Has("months") ? $" for {n.GetString("months")} months" : n.Has("days") ? $" for {n.GetString("days")} days" : "";
            return $"Gain modifier '{Localisation.Get(d.Name)}'{dur}:\n  " + Describe(d).Replace("\n", "\n  ");
        });
        s.RegisterEffect("remove_modifier", (ctx, n) => { if (ctx.This != null) Remove(ctx.This, n.Value ?? ""); },
            (ctx, n) => $"Lose modifier '{(Defs.TryGetValue(n.Value ?? "", out var d) ? Localisation.Get(d.Name) : n.Value)}'");
        s.RegisterTrigger("has_modifier", (ctx, n) => ctx.This != null && Has(ctx.This, n.Value ?? ""),
            (ctx, n) => $"Has modifier '{(Defs.TryGetValue(n.Value ?? "", out var d) ? Localisation.Get(d.Name) : n.Value)}'");
        s.ModifierLookup = (scope, key) => Sum(scope, key);
    }
}
