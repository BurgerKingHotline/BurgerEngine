namespace Burger.Core;

public enum ScopeType { None, Country, Character, State, Location, War }

/// <summary>Flags and numeric variables that scripts can attach to any scope (country, character, state, location).</summary>
public sealed class VarStore
{
    public readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, double> Vars = new(StringComparer.OrdinalIgnoreCase);

    public bool HasFlag(string f) => Flags.Contains(f);
    public void SetFlag(string f) => Flags.Add(f);
    public void ClearFlag(string f) => Flags.Remove(f);
    public double Get(string name, double def = 0) => Vars.TryGetValue(name, out var v) ? v : def;
    public void Set(string name, double v) => Vars[name] = v;
    public bool Has(string name) => Vars.ContainsKey(name);

    public void CopyFrom(VarStore o)
    {
        Flags.Clear();
        Vars.Clear();
        foreach (var f in o.Flags) Flags.Add(f);
        foreach (var kv in o.Vars) Vars[kv.Key] = kv.Value;
    }
}

/// <summary>Anything a script can "scope" into: countries, characters, states, locations, wars.</summary>
public interface IScope
{
    ScopeType ScopeType { get; }
    /// <summary>Stable key used in scripts and saves (tag, character id, state key, location key...).</summary>
    string ScopeKey { get; }
    string DisplayName { get; }
    VarStore Vars { get; }
}
