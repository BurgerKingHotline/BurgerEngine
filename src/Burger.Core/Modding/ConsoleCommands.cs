using System.Globalization;
using Burger.Core.Model;

namespace Burger.Core.Modding;

/// <summary>
/// Developer console commands (open with ~ in game). Mods can add their own:
/// <code>Game.Commands.Register("hello", "says hello", args =&gt; "Hello " + string.Join(" ", args));</code>
/// </summary>
public sealed class ConsoleCommands
{
    private readonly Game _game;
    private readonly Dictionary<string, (string Help, Func<string[], string> Run)> _commands = new(StringComparer.OrdinalIgnoreCase);

    public ConsoleCommands(Game game)
    {
        _game = game;
        RegisterBuiltins();
    }

    public void Register(string name, string help, Func<string[], string> run) => _commands[name] = (help, run);

    public IEnumerable<string> Names => _commands.Keys.OrderBy(k => k);

    /// <summary>Runs a command line and returns the text to print.</summary>
    public string Execute(string line)
    {
        line = line.Trim();
        if (line.Length == 0) return "";
        int sp = line.IndexOf(' ');
        string cmd = sp < 0 ? line : line[..sp];
        string rest = sp < 0 ? "" : line[(sp + 1)..].Trim();
        if (!_commands.TryGetValue(cmd, out var c)) return $"Unknown command '{cmd}'. Type 'help'.";
        try
        {
            // effect/trigger take the whole rest of the line as script text
            var args = cmd is "effect" or "trigger" or "e" or "t" ? new[] { rest } : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return c.Run(args);
        }
        catch (Exception ex)
        {
            return "Error: " + ex.Message;
        }
    }

    private Country Player => _game.World.Player ?? throw new InvalidOperationException("No player country (observer mode)");

    private Country Tag(string s) => _game.World.GetCountry(s) ?? throw new ArgumentException($"Unknown tag '{s}'");

    private void RegisterBuiltins()
    {
        Register("help", "list commands", _ => string.Join("\n", _commands.OrderBy(k => k.Key).Select(k => $"{k.Key} - {k.Value.Help}")));
        Register("tag", "tag <TAG> - play as another country", a =>
        {
            var c = Tag(a[0]);
            _game.SetPlayer(c.Tag);
            return $"Now playing {c.Name}";
        });
        Register("observe", "watch the AI play (no player country)", _ => { _game.SetPlayer(null); return "Observer mode"; });
        Register("effect", "effect <script> - run an effect with ROOT = player, e.g. effect add_gold = 100", a =>
        {
            bool ok = _game.RunEffect(a[0], _game.World.Player);
            return ok ? "Effect executed" : "Effect had errors (see error log, F2)";
        });
        Register("e", "short for effect", a => _commands["effect"].Run(a));
        Register("trigger", "trigger <script> - evaluate a trigger with ROOT = player", a => _game.EvalTrigger(a[0], _game.World.Player) ? "TRUE" : "FALSE");
        Register("t", "short for trigger", a => _commands["trigger"].Run(a));
        Register("event", "event <id> [TAG|character id] - fire an event", a =>
        {
            IScope? root = _game.World.Player;
            if (a.Length > 1) root = (IScope?)_game.World.GetCountry(a[1]) ?? _game.World.GetCharacter(a[1]);
            var def = _game.Events.Get(a[0]) ?? throw new ArgumentException($"Unknown event '{a[0]}'");
            if (def.Kind == EventKind.Character && root is Country c) root = c.Ruler;
            return _game.Events.FireEvent(def, root, null, checkTrigger: false) ? "Fired" : "Did not fire";
        });
        Register("gold", "gold <amount> - add gold to the player", a => { Player.Gold += double.Parse(a[0], CultureInfo.InvariantCulture); return $"Gold: {Player.Gold:0}"; });
        Register("manpower", "manpower <amount>", a => { Player.Manpower += double.Parse(a[0], CultureInfo.InvariantCulture); return $"Manpower: {Player.Manpower:0}"; });
        Register("war", "war <TAG> [TAG2] - declare war (player or TAG2 attacks TAG)", a =>
        {
            var target = Tag(a[0]);
            var attacker = a.Length > 1 ? Tag(a[1]) : Player;
            var w = _game.Diplomacy.DeclareWar(attacker, target, null, null, force: true);
            return w != null ? $"War declared: {w.Name}" : "Could not declare war";
        });
        Register("whitepeace", "whitepeace <TAG> - end the war with TAG", a =>
        {
            var other = Tag(a[0]);
            var war = _game.World.WarBetween(Player, other) ?? throw new ArgumentException("Not at war");
            _game.Diplomacy.EndWar(war);
            return "Peace";
        });
        Register("annex", "annex <TAG> - player annexes TAG", a => { _game.Diplomacy.Annex(Player, Tag(a[0])); return "Annexed"; });
        Register("own", "own <state> [TAG] - give a state to the player (or TAG)", a =>
        {
            var s = _game.World.GetState(a[0]) ?? throw new ArgumentException($"Unknown state '{a[0]}'");
            _game.Diplomacy.TransferState(s, a.Length > 1 ? Tag(a[1]) : Player);
            return "Done";
        });
        Register("combat", "combat <hoi4|eu4|imperator|...> - switch combat model", a =>
        {
            if (a.Length == 0) return "Models: " + string.Join(", ", _game.Combat.Models.Select(m => m.Id)) + $" (current: {_game.Data.Defines.CombatModel})";
            _game.Combat.Get(a[0]);
            _game.Data.Defines.CombatModel = a[0].ToLowerInvariant();
            return $"Combat model: {a[0]}";
        });
        Register("front", "front <TAG> [attack] - all player armies hold (or attack) a front against TAG", a =>
        {
            var enemy = Tag(a[0]);
            var mode = a.Length > 1 && a[1].StartsWith("att", StringComparison.OrdinalIgnoreCase) ? ArmyMode.Offensive : ArmyMode.Hold;
            int n = 0;
            foreach (var army in _game.World.ArmiesOf(Player).ToList()) { _game.Warfare.SetFront(army, enemy, mode); n++; }
            return $"{n} armies: front vs {enemy.Tag} ({mode})";
        });
        Register("ai", "ai on|off - toggle AI", a => { _game.Ai.Enabled = a.Length == 0 || a[0] != "off"; return "AI " + (_game.Ai.Enabled ? "on" : "off"); });
        Register("speed", "speed <1-5> - set the game speed and unpause", a => { _game.Clock.Speed = Math.Clamp(int.Parse(a[0]), 1, 5); _game.Clock.Paused = false; return $"Speed {_game.Clock.Speed}"; });
        Register("date", "show the date", _ => _game.World.Date.ToDisplay(true));
        Register("tick", "tick <days> - advance time instantly", a =>
        {
            int days = a.Length > 0 ? int.Parse(a[0]) : 1;
            for (int i = 0; i < days * 24; i++) _game.Tick();
            return _game.World.Date.ToDisplay();
        });
        Register("spawn", "spawn <template> <location> [TAG] - create a division", a =>
        {
            var t = _game.Data.Templates.TryGetValue(a[0], out var tt) ? tt : throw new ArgumentException($"Unknown template '{a[0]}'");
            var l = _game.World.GetLocation(a[1]) ?? throw new ArgumentException($"Unknown location '{a[1]}'");
            var d = _game.Warfare.CreateDivision(a.Length > 2 ? Tag(a[2]) : Player, t, l);
            return $"Created {d.Name}";
        });
        Register("kill", "kill <character id>", a =>
        {
            var c = _game.World.GetCharacter(a[0]) ?? throw new ArgumentException($"Unknown character '{a[0]}'");
            _game.Characters.Kill(c, "the console");
            return $"{c.DisplayName} died";
        });
        Register("errors", "number of errors in the log", _ => $"{Log.ErrorCount} errors, {Log.WarningCount} warnings");
    }
}
