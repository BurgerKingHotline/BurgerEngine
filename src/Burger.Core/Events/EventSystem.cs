using System.Text.RegularExpressions;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Events;

/// <summary>Reads <c>events/*.txt</c>.</summary>
public static class EventLoader
{
    public static void LoadEvents(GameData data)
    {
        foreach (var (path, display) in DataLoader.Files(data, "events"))
        {
            var root = ScriptParser.ParseFile(path, display);
            foreach (var n in root.All)
            {
                string k = n.Key.ToLowerInvariant();
                if (k == "namespace") continue;
                EventKind kind;
                if (k is "country_event" or "event") kind = EventKind.Country;
                else if (k == "character_event") kind = EventKind.Character;
                else
                {
                    Log.Error($"Unknown event type '{n.Key}' (use country_event or character_event)", n.Location);
                    continue;
                }
                if (n.Children == null) continue;
                if (k == "event" && string.Equals(n.GetString("type"), "character", StringComparison.OrdinalIgnoreCase)) kind = EventKind.Character;
                var id = n.GetString("id");
                if (string.IsNullOrEmpty(id))
                {
                    Log.Error("Event without id", n.Location);
                    continue;
                }
                var ev = new EventDef
                {
                    Id = id,
                    Kind = kind,
                    Title = n.GetString("title", id + ".title")!,
                    Desc = n.GetString("desc", id + ".desc")!,
                    Picture = n.GetString("picture"),
                    Trigger = n.Get("trigger"),
                    MtthBlock = n.Get("mean_time_to_happen"),
                    FireOnlyOnce = n.GetBool("fire_only_once"),
                    Hidden = n.GetBool("hidden"),
                    Major = n.GetBool("major"),
                    Pause = n.Get("pause"),
                    Immediate = n.Get("immediate"),
                    After = n.Get("after"),
                    File = n.File,
                    Line = n.Line,
                };
                ev.TriggeredOnly = n.GetBool("is_triggered_only") || ev.MtthBlock == null;
                foreach (var o in n.GetAll("option"))
                {
                    if (o.Children == null) continue;
                    ev.Options.Add(new EventOption
                    {
                        Name = o.GetString("name", "OK")!,
                        Trigger = o.Get("trigger"),
                        AiChance = o.Get("ai_chance"),
                        Highlight = o.GetBool("highlight"),
                        Effects = o,
                    });
                }
                if (ev.Options.Count == 0)
                    ev.Options.Add(new EventOption { Name = "OK", Effects = new Node { Key = "option", Children = new List<Node>(), File = n.File } });
                if (data.Events.ContainsKey(ev.Id)) Log.Error($"Duplicate event id '{ev.Id}'", n.Location);
                data.Events[ev.Id] = ev;
            }
        }
    }
}

/// <summary>An event shown to the player that is waiting for an option to be chosen.</summary>
public sealed class PendingEvent
{
    public EventDef Def = null!;
    public ScriptContext Context = null!;
    public GameDate FiredOn;
    public string Title = "";
    public string Desc = "";
}

public sealed class ScheduledEvent
{
    public GameDate When;
    public EventDef Def = null!;
    public IScope? Root;
    public Dictionary<string, IScope?> Scopes = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Fires events (by mean time to happen, by script, by on_actions) and resolves their options.</summary>
public sealed class EventSystem
{
    /// <summary>Does this event pause the game when it pops up for the player? (event's pause = ..., else the define)</summary>
    public bool ShouldPause(EventDef def, ScriptContext ctx)
    {
        if (def.Pause == null) return _game.Data.Defines.PauseOnEvent;
        if (def.Pause.Value != null) return def.Pause.AsBool(_game.Data.Defines.PauseOnEvent);
        return _game.Script.EvalTrigger(def.Pause, ctx);
    }

    private static readonly HashSet<string> OptionReserved = new(StringComparer.OrdinalIgnoreCase) { "name", "trigger", "ai_chance", "highlight" };
    private static readonly Regex Placeholder = new(@"\[([^\[\]]+)\]", RegexOptions.Compiled);

    private readonly Game _game;
    public readonly List<PendingEvent> PlayerQueue = new();
    public readonly List<ScheduledEvent> Scheduled = new();

    public EventSystem(Game game) { _game = game; }

    public EventDef? Get(string id) => _game.Data.Events.TryGetValue(id, out var e) ? e : null;

    /// <summary>Fires an event by id, optionally after a delay in days.</summary>
    public void FireEvent(string id, IScope? root, Dictionary<string, IScope?>? scopes = null, int delayDays = 0, string? where = null)
    {
        var def = Get(id);
        if (def == null)
        {
            Log.ErrorOnce("noevent:" + id, $"Unknown event id '{id}'", where);
            return;
        }
        if (delayDays > 0)
        {
            Scheduled.Add(new ScheduledEvent
            {
                When = _game.World.Date.AddDays(delayDays),
                Def = def,
                Root = root,
                Scopes = scopes != null ? new Dictionary<string, IScope?>(scopes, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase),
            });
            return;
        }
        FireEvent(def, root, scopes, checkTrigger: false);
    }

    /// <summary>Fires an event now. Returns true if it fired.</summary>
    public bool FireEvent(EventDef def, IScope? root, Dictionary<string, IScope?>? scopes = null, bool checkTrigger = true)
    {
        if (root == null) return false;
        if (def.Kind == EventKind.Country && root is not Country)
        {
            var c = ScopeHelpers.CountryOf(root);
            if (c == null) return false;
            root = c;
        }
        if (def.Kind == EventKind.Character && root is not Character)
        {
            Log.ErrorOnce("evkind:" + def.Id, $"character_event '{def.Id}' was fired for a {root.ScopeType} scope (needs a character)", $"{def.File}:{def.Line}");
            return false;
        }
        string onceKey = def.Id;
        if (def.FireOnlyOnce && _game.World.FiredOnceEvents.Contains(onceKey)) return false;

        var ctx = _game.Context(root, scopes != null ? new Dictionary<string, IScope?>(scopes, StringComparer.OrdinalIgnoreCase) : null);
        ctx.Where = $"{def.File}:{def.Line}";
        if (checkTrigger && !_game.Script.EvalTrigger(def.Trigger, ctx)) return false;
        if (def.FireOnlyOnce) _game.World.FiredOnceEvents.Add(onceKey);

        _game.Script.Execute(def.Immediate, ctx);
        _game.Mods.Invoke(m => m.OnEventFired(def, root));

        var country = ScopeHelpers.CountryOf(root);
        if (def.Hidden)
        {
            var opt = def.Options.FirstOrDefault(o => _game.Script.EvalTrigger(o.Trigger, ctx));
            if (opt != null) _game.Script.Execute(opt.Effects, ctx, OptionReserved);
            _game.Script.Execute(def.After, ctx);
            return true;
        }

        if (country != null && country.IsPlayer)
        {
            var pe = new PendingEvent
            {
                Def = def,
                Context = ctx,
                FiredOn = _game.World.Date,
                Title = FormatText(def.Title, ctx),
                Desc = FormatText(def.Desc, ctx),
            };
            PlayerQueue.Add(pe);
            if (ShouldPause(def, ctx)) _game.Clock.Paused = true;
            return true;
        }

        // AI chooses
        var chosen = ChooseAiOption(def, ctx);
        if (chosen >= 0) ApplyOption(def, ctx, chosen);
        if (def.Major && _game.World.Player != null)
            _game.Notify($"{country?.Name}: {FormatText(def.Title, ctx)}", NotificationKind.Info);
        return true;
    }

    public int ChooseAiOption(EventDef def, ScriptContext ctx)
    {
        double total = 0;
        var weights = new double[def.Options.Count];
        int firstAvailable = -1;
        for (int i = 0; i < def.Options.Count; i++)
        {
            var o = def.Options[i];
            if (!_game.Script.EvalTrigger(o.Trigger, ctx)) continue;
            if (firstAvailable < 0) firstAvailable = i;
            weights[i] = Math.Max(0, _game.Script.EvalWeight(o.AiChance, ctx, 1));
            total += weights[i];
        }
        if (total <= 0) return firstAvailable;
        double r = _game.Rng.NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (weights[i] > 0 && r <= 0) return i;
        }
        return firstAvailable;
    }

    private void ApplyOption(EventDef def, ScriptContext ctx, int index)
    {
        if (index < 0 || index >= def.Options.Count) return;
        _game.Script.Execute(def.Options[index].Effects, ctx, OptionReserved);
        _game.Script.Execute(def.After, ctx);
        _game.RaiseMapChanged();
    }

    /// <summary>Is the option selectable (its trigger passes)?</summary>
    public bool IsOptionAvailable(PendingEvent pe, int index)
        => index >= 0 && index < pe.Def.Options.Count && _game.Script.EvalTrigger(pe.Def.Options[index].Trigger, pe.Context);

    /// <summary>Tooltip lines describing what an option does.</summary>
    public List<string> DescribeOption(PendingEvent pe, int index)
        => _game.Script.Describe(pe.Def.Options[index].Effects, pe.Context, 0, OptionReserved);

    public string OptionName(PendingEvent pe, int index) => FormatText(pe.Def.Options[index].Name, pe.Context);

    /// <summary>Player picks an option.</summary>
    public void ChooseOption(PendingEvent pe, int index)
    {
        if (!PlayerQueue.Remove(pe)) return;
        if (!IsOptionAvailable(pe, index))
        {
            index = Enumerable.Range(0, pe.Def.Options.Count).FirstOrDefault(i => IsOptionAvailable(pe, i));
        }
        ApplyOption(pe.Def, pe.Context, index);
    }

    public void DailyTick()
    {
        var now = _game.World.Date;
        for (int i = 0; i < Scheduled.Count; i++)
        {
            var s = Scheduled[i];
            if (s.When.TotalDays > now.TotalDays) continue;
            Scheduled.RemoveAt(i--);
            FireEvent(s.Def, s.Root, s.Scopes, checkTrigger: false);
        }
        // auto-resolve events the player ignored for too long
        int timeout = _game.Data.Defines.EventTimeoutDays;
        if (timeout > 0)
        {
            foreach (var pe in PlayerQueue.Where(p => now.TotalDays - p.FiredOn.TotalDays >= timeout).ToList())
                ChooseOption(pe, Math.Max(0, ChooseAiOption(pe.Def, pe.Context)));
        }
    }

    public void MonthlyTick()
    {
        var events = _game.Data.Events.Values.Where(e => !e.TriggeredOnly).ToList();
        var countryEvents = events.Where(e => e.Kind == EventKind.Country).ToList();
        var characterEvents = events.Where(e => e.Kind == EventKind.Character).ToList();

        foreach (var c in _game.World.Countries.Where(c => c.Exists).ToList())
        {
            foreach (var e in countryEvents) TryMtth(e, c);
            FireOnAction("on_monthly_pulse", c);
        }
        if (characterEvents.Count > 0)
        {
            foreach (var ch in _game.World.Characters.Where(ch => ch.Alive && ch.Country != null && ch.Country.Exists).ToList())
                foreach (var e in characterEvents) TryMtth(e, ch);
        }
    }

    private void TryMtth(EventDef e, IScope scope)
    {
        if (e.FireOnlyOnce && _game.World.FiredOnceEvents.Contains(e.Id)) return;
        var ctx = _game.Context(scope);
        ctx.Where = $"{e.File}:{e.Line}";
        if (!_game.Script.EvalTrigger(e.Trigger, ctx)) return;
        double mtth = e.MtthBlock != null ? _game.Script.EvalMtthDays(e.MtthBlock, ctx) : 365;
        double p = 1 - Math.Pow(0.5, 30.0 / mtth);
        if (_game.Rng.NextDouble() < p) FireEvent(e, scope, null, checkTrigger: false);
    }

    /// <summary>Runs an on_action from <c>common/on_actions</c>: its effects, events and random events.</summary>
    public void FireOnAction(string key, IScope? root, Dictionary<string, IScope?>? scopes = null)
    {
        if (!_game.Data.OnActions.TryGetValue(key, out var oa)) return;
        var ctx = _game.Context(root, scopes != null ? new Dictionary<string, IScope?>(scopes, StringComparer.OrdinalIgnoreCase) : null);
        foreach (var eff in oa.Effects) _game.Script.Execute(eff, ctx);
        if (root == null) return;
        foreach (var id in oa.Events)
        {
            var def = Get(id);
            if (def == null) { Log.ErrorOnce("oa-ev:" + id, $"on_action '{key}' references unknown event '{id}'"); continue; }
            FireEvent(def, root, ctx.Scopes, checkTrigger: true);
        }
        if (oa.RandomEvents.Count > 0)
        {
            double total = oa.RandomEvents.Sum(r => r.Weight);
            double r = _game.Rng.NextDouble() * total;
            foreach (var (w, id) in oa.RandomEvents)
            {
                r -= w;
                if (r > 0) continue;
                if (id != "0" && !string.Equals(id, "none", StringComparison.OrdinalIgnoreCase))
                {
                    var def = Get(id);
                    if (def != null) FireEvent(def, root, ctx.Scopes, checkTrigger: true);
                    else Log.ErrorOnce("oa-rev:" + id, $"on_action '{key}' references unknown event '{id}'");
                }
                break;
            }
        }
    }

    /// <summary>
    /// Localises text and replaces placeholders: <c>[ROOT]</c>, <c>[ROOT.ruler]</c>, <c>[scope:target]</c>,
    /// <c>[ROOT.gold]</c> (numbers). A trailing <c>.GetName</c> is allowed for Paradox compatibility.
    /// </summary>
    public string FormatText(string text, ScriptContext ctx)
    {
        var s = Localisation.Get(text);
        if (s.IndexOf('[') < 0) return s;
        return Placeholder.Replace(s, m =>
        {
            string expr = m.Groups[1].Value.Trim();
            foreach (var suffix in new[] { ".GetName", ".name", ".Name", ".GetFullName" })
                if (expr.EndsWith(suffix, StringComparison.Ordinal)) { expr = expr[..^suffix.Length]; break; }
            if (_game.Script.TryResolveScope(expr, ctx, out var scope)) return scope?.DisplayName ?? "?";
            int dot = expr.LastIndexOf('.');
            double v = _game.Script.ResolveNumber(expr, ctx, double.NaN);
            if (!double.IsNaN(v)) return v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            return m.Value;
        });
    }
}
