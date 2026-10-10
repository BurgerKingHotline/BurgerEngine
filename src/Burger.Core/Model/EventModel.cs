using Burger.Core.Script;

namespace Burger.Core.Model;

public enum EventKind { Country, Character }

public sealed class EventOption
{
    public string Name = "";
    public Node? Trigger;
    /// <summary>The option block itself (effects are all keys except name/trigger/ai_chance).</summary>
    public Node Effects = Node.Root("");
    public Node? AiChance;
    public bool Highlight;
}

/// <summary>An event from <c>events/*.txt</c> (<c>country_event</c> or <c>character_event</c>).</summary>
public sealed class EventDef
{
    public string Id = "";
    public EventKind Kind;
    public string Title = "";
    public string Desc = "";
    public string? Picture;
    public Node? Trigger;
    /// <summary>Mean time to happen in days. 0 = only fired by script (<c>is_triggered_only</c>).</summary>
    public double MtthDays;
    public Node? MtthBlock;
    public bool TriggeredOnly;
    public bool FireOnlyOnce;
    public bool Hidden;
    /// <summary>Major events are shown to every player, not just the scope's country.</summary>
    public bool Major;
    /// <summary><c>pause = yes/no</c> or <c>pause = { trigger }</c> (ROOT = the event's scope). Null = use the pause_on_event define.</summary>
    public Node? Pause;
    public Node? Immediate;
    public Node? After;
    public List<EventOption> Options = new();
    public string File = "";
    public int Line;
}

public enum ActionTarget { Country, Character }

/// <summary>
/// A data-driven diplomatic action (<c>common/diplomatic_actions</c>) or character interaction
/// (<c>common/character_interactions</c>). Scopes: ROOT / scope:actor = the country doing it,
/// scope:recipient = the target country (or scope:target = the target character).
/// </summary>
public sealed class ActionDef
{
    public string Key = "";
    public ActionTarget TargetType;
    public string Name = "";
    public string Desc = "";
    public string Category = "";
    /// <summary>Shown in the list only if this passes.</summary>
    public Node? Potential;
    /// <summary>Clickable only if this passes (failing triggers are shown as reasons).</summary>
    public Node? Allow;
    /// <summary>If true the recipient (AI or player) must accept.</summary>
    public bool RequiresAcceptance;
    public Node? AiAcceptance;
    public Node? OnAccept;
    public Node? OnDecline;
    public Node? AiWillDo;
    public int CooldownDays;
    /// <summary>Gold cost paid by the actor when the action is used.</summary>
    public double CostGold;
    /// <summary>Opens a window in the game instead of acting at once (volunteers, lend_lease); the window does the work.</summary>
    public string? Dialog;
    public string File = "";
    public int Line;
}

/// <summary>A hook from <c>common/on_actions/*.txt</c>.</summary>
public sealed class OnAction
{
    public string Key = "";
    public List<Node> Effects = new();
    public List<string> Events = new();
    /// <summary>Weighted random events: weight → event id.</summary>
    public List<(double Weight, string EventId)> RandomEvents = new();
}
