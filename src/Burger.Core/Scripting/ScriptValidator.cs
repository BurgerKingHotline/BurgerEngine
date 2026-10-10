using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Scripting;

/// <summary>
/// Static checker run by <c>burger validate</c> and at game start: finds unknown triggers/effects,
/// misspelled scopes and references to events that do not exist, with file and line.
/// </summary>
public static class ScriptValidator
{
    private static readonly HashSet<string> Logic = new(StringComparer.OrdinalIgnoreCase) { "and", "or", "not", "nor", "nand", "always", "custom_tooltip", "custom_trigger_tooltip", "limit" };
    private static readonly HashSet<string> Control = new(StringComparer.OrdinalIgnoreCase) { "if", "else_if", "else", "random", "random_list", "hidden_effect", "effect", "custom_tooltip", "custom_effect_tooltip", "limit" };
    private static readonly HashSet<string> IteratorReserved = new(StringComparer.OrdinalIgnoreCase) { "limit", "count", "percent", "max", "order_by", "weight" };
    private static readonly HashSet<string> WeightKeys = new(StringComparer.OrdinalIgnoreCase) { "factor", "add", "base", "days", "months", "years", "opinion_factor", "strength_factor" };
    private static readonly HashSet<string> KnownScopeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "actor", "recipient", "target", "attacker", "defender", "war", "loser", "winner", "location", "state", "old_owner",
        "new_ruler", "old_ruler", "mother", "father", "country", "old_controller",
    };

    public static int ValidateAll(Game game)
    {
        int before = Log.ErrorCount + Log.WarningCount;
        var d = game.Data;
        foreach (var e in d.Events.Values)
        {
            Trigger(game, e.Trigger);
            Effect(game, e.Immediate);
            Effect(game, e.After);
            Weight(game, e.MtthBlock);
            if (e.Pause?.Children != null) Trigger(game, e.Pause);
            foreach (var o in e.Options)
            {
                Trigger(game, o.Trigger);
                Weight(game, o.AiChance);
                Effect(game, o.Effects, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "trigger", "ai_chance", "highlight" });
            }
        }
        foreach (var a in d.DiplomaticActions.Values.Concat(d.CharacterInteractions.Values))
        {
            Trigger(game, a.Potential);
            Trigger(game, a.Allow);
            Effect(game, a.OnAccept);
            Effect(game, a.OnDecline);
            Weight(game, a.AiAcceptance);
            Weight(game, a.AiWillDo);
        }
        foreach (var oa in d.OnActions.Values)
        {
            foreach (var e in oa.Effects) Effect(game, e);
            foreach (var id in oa.Events.Concat(oa.RandomEvents.Select(r => r.EventId)))
                if (id != "0" && !id.Equals("none", StringComparison.OrdinalIgnoreCase) && !d.Events.ContainsKey(id))
                    Log.Error($"on_action '{oa.Key}' references unknown event '{id}'");
        }
        foreach (var cb in d.CasusBelli.Values) Trigger(game, cb.Allow);
        foreach (var dec in game.Decisions.Defs.Values)
        {
            Trigger(game, dec.Potential);
            Trigger(game, dec.Allow);
            Effect(game, dec.Effect);
            Weight(game, dec.AiWillDo);
        }
        foreach (var m in game.Modifiers.Defs.Values) Trigger(game, m.Trigger);
        foreach (var p in game.Interface.Panels)
        {
            Trigger(game, p.Potential);
            PanelElements(game, p.Elements);
        }
        return Log.ErrorCount + Log.WarningCount - before;
    }

    private static void PanelElements(Game game, List<Burger.Core.Mechanics.PanelElement> elements)
    {
        foreach (var e in elements)
        {
            Trigger(game, e.Potential);
            Trigger(game, e.Allow);
            Trigger(game, e.Limit);
            Effect(game, e.Effect);
            if (e.Kind == Burger.Core.Mechanics.PanelElementKind.List && !game.Script.IteratorNames.Contains(e.Iterator, StringComparer.OrdinalIgnoreCase))
                Log.Error($"Panel list uses unknown iterator '{e.Iterator}' (e.g. owned_state, character, ally)");
            PanelElements(game, e.Children);
        }
    }

    private static void Weight(Game game, Node? block)
    {
        if (block?.Children == null) return;
        foreach (var m in block.GetAll("modifier")) Trigger(game, m, WeightKeys);
    }

    public static void Trigger(Game game, Node? block, ISet<string>? ignore = null)
    {
        if (block?.Children == null) return;
        foreach (var n in block.Children)
        {
            if (ignore != null && ignore.Contains(n.Key)) continue;
            if (n.Op == Op.None) { if (n.Children != null) Trigger(game, n); else Log.Error($"Unexpected '{n.Key}' in trigger", n.Location); continue; }
            if (Logic.Contains(n.Key)) { if (n.Children != null) Trigger(game, n, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tooltip", "text" }); continue; }
            if (game.Script.HasTrigger(n.Key)) continue;
            if (IsIterator(game, n.Key, "any", "all")) { Trigger(game, n, IteratorReserved); continue; }
            if (game.Script.ValueNames.Contains(n.Key, StringComparer.OrdinalIgnoreCase) && n.Value != null) continue;
            if (CheckScope(game, n, true)) continue;
            Log.Error($"Unknown trigger '{n.Key}'", n.Location);
        }
    }

    public static void Effect(Game game, Node? block, ISet<string>? ignore = null)
    {
        if (block?.Children == null) return;
        foreach (var n in block.Children)
        {
            if (ignore != null && ignore.Contains(n.Key)) continue;
            if (n.Op == Op.None) { if (n.Children != null) Effect(game, n); else Log.Error($"Unexpected '{n.Key}' in effect", n.Location); continue; }
            string k = n.Key.ToLowerInvariant();
            if (Control.Contains(k))
            {
                if (k is "if" or "else_if") Trigger(game, n.Get("limit"));
                if (k == "random_list")
                {
                    foreach (var c in n.All.Where(c => c.Children != null))
                    {
                        Trigger(game, c.Get("trigger"));
                        foreach (var m in c.GetAll("modifier")) Trigger(game, m, WeightKeys);
                        Effect(game, c, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "trigger", "modifier" });
                    }
                }
                else if (n.Children != null) Effect(game, n, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "limit", "chance", "tooltip", "text" });
                continue;
            }
            if (game.Script.HasEffect(n.Key))
            {
                if (k is "country_event" or "character_event" or "trigger_event")
                {
                    string? id = n.Value ?? n.GetString("id");
                    if (id != null && !game.Data.Events.ContainsKey(id)) Log.Error($"Unknown event id '{id}'", n.Location);
                }
                if (k == "create_character" || k == "create_division") continue;
                continue;
            }
            if (IsIterator(game, n.Key, "every", "random", "ordered"))
            {
                Trigger(game, n.Get("limit"));
                Effect(game, n, IteratorReserved);
                continue;
            }
            if (n.Children != null && CheckScope(game, n, false)) continue;
            Log.Error(game.Script.HasTrigger(n.Key) ? $"'{n.Key}' is a trigger, not an effect" : $"Unknown effect '{n.Key}'", n.Location);
        }
    }

    private static bool IsIterator(Game game, string key, params string[] prefixes)
    {
        int us = key.IndexOf('_');
        if (us <= 0) return false;
        string p = key[..us];
        if (!prefixes.Contains(p, StringComparer.OrdinalIgnoreCase)) return false;
        return game.Script.IteratorNames.Contains(key[(us + 1)..], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Checks a scope-switch key like GER, ROOT, ruler, scope:x, ROOT.capital.</summary>
    private static bool CheckScope(Game game, Node n, bool trigger)
    {
        foreach (var part in n.Key.Split('.'))
            if (!IsScopeWord(game, part, n)) return false;
        if (n.Children != null)
        {
            if (trigger) Trigger(game, n); else Effect(game, n);
        }
        else if (n.Value != null && trigger)
        {
            // scope comparison: owner = ROOT
            foreach (var part in n.Value.Split('.'))
                if (!IsScopeWord(game, part, n)) { Log.Error($"'{n.Value}' is not a scope", n.Location); break; }
        }
        return true;
    }

    private static bool IsScopeWord(Game game, string w, Node where)
    {
        string l = w.ToLowerInvariant();
        if (l is "root" or "this" or "prev" or "from" or "player") return true;
        int colon = w.IndexOf(':');
        if (colon > 0)
        {
            string prefix = w[..colon].ToLowerInvariant(), id = w[(colon + 1)..];
            switch (prefix)
            {
                case "scope": return true;
                case "c": case "country": case "tag":
                    if (game.World.GetCountry(id) == null) Log.Error($"Unknown country '{id}'", where.Location);
                    return true;
                case "s": case "state":
                    if (game.World.GetState(id) == null) Log.Error($"Unknown state '{id}'", where.Location);
                    return true;
                case "l": case "location": case "loc": case "p": case "province":
                    if (game.World.GetLocation(id) == null) Log.Error($"Unknown location '{id}'", where.Location);
                    return true;
                case "char": case "character":
                    if (game.World.GetCharacter(id) == null) Log.Warn($"Character '{id}' does not exist at game start", where.Location);
                    return true;
            }
            return false;
        }
        if (w.Length == 3 && Country.IsValidTag(w)) return game.World.GetCountry(w) != null;
        if (game.Script.LinkNames.Contains(w, StringComparer.OrdinalIgnoreCase)) return true;
        return KnownScopeNames.Contains(w);
    }
}
