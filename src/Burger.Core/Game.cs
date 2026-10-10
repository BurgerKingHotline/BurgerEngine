using F = Burger.Core.Modding.FeatureSet;
using Burger.Core.AI;
using Burger.Core.Characters;
using Burger.Core.Data;
using Burger.Core.Diplomacy;
using Burger.Core.Economy;
using Burger.Core.Events;
using Burger.Core.Map;
using Burger.Core.Mechanics;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Scripting;
using Burger.Core.Warfare;

namespace Burger.Core;

public enum NotificationKind { Info, Good, Bad, War, Diplomacy, Character }

public sealed record Notification(GameDate Date, string Text, NotificationKind Kind, Location? Location = null);

public enum ProposalKind { DiplomaticAction, CharacterInteraction, Peace, CallToArms }

/// <summary>Something waiting for the player's answer (AI asked for an alliance, sent a peace offer...).</summary>
public sealed class Proposal
{
    public ProposalKind Kind;
    public ActionDef? Action;
    public Country From = null!;
    public Country To = null!;
    public Character? TargetCharacter;
    public PeaceOffer? Peace;
    public War? War;
    public GameDate Date;
    public string Title = "";
    public string Text = "";
}

/// <summary>
/// Swappable rule objects. Replace any of them from a C# mod to change how the game works:
/// <code>Game.Rules.Diplomacy = new MyDiplomacyRules();</code>
/// </summary>
public sealed class GameRules
{
    public DiplomacyRules Diplomacy = new();
    public EconomyRules Economy = new();
    public CharacterRules Characters = new();
    public WarfareRules Warfare = new();
    /// <summary>Fog of war (which locations a country can see).</summary>
    public Burger.Core.Warfare.VisionRules Vision = new();
    /// <summary>Factories, production lines and construction (production feature).</summary>
    public Burger.Core.Production.ProductionRules Production = new();
    /// <summary>Supply map and attrition (supply_range / supply_hubs features).</summary>
    public Burger.Core.Supply.SupplyRules Supply = new();
    /// <summary>Naval combat, ports, repairs and transports (navy feature).</summary>
    public Burger.Core.Navy.NavyRules Navy = new();
    /// <summary>Air wings, air superiority, close air support and bombing (air feature).</summary>
    public Burger.Core.Air.AirRules Air = new();
    /// <summary>Lend-lease and volunteers (lend_lease feature).</summary>
    public Burger.Core.Warfare.SupportRules Support = new();
}

/// <summary>
/// The whole simulation. Create with <see cref="Load"/>, then call <see cref="StartNewGame"/> (or load a save)
/// and advance time with <see cref="Tick"/> (one in-game hour) or <see cref="Clock"/>.
/// </summary>
public sealed class Game
{
    public GameData Data { get; private set; } = new();
    public World World { get; private set; } = new();
    public Random Rng { get; private set; } = new();
    public GameRules Rules { get; } = new();
    /// <summary>The mechanics this game uses (game/features.txt). Systems of missing features do not run.</summary>
    public Burger.Core.Modding.FeatureSet Features { get; private set; } = new();
    /// <summary>Blocks of the save file owned by systems and scripts.</summary>
    public Burger.Core.Modding.SaveSectionRegistry SaveSections { get; } = new();

    public ScriptEngine Script { get; }
    public EventSystem Events { get; }
    public CharacterSystem Characters { get; }
    public DiplomacySystem Diplomacy { get; }
    public WarfareSystem Warfare { get; }
    public EconomySystem Economy { get; }
    public CombatRegistry Combat { get; }
    /// <summary>Factories, production lines, stockpiles and construction (production feature).</summary>
    public Burger.Core.Production.ProductionSystem Production { get; }
    /// <summary>Supply map and attrition (supply features).</summary>
    public Burger.Core.Supply.SupplySystem Supply { get; }
    /// <summary>Division templates made during the game (division_designer feature).</summary>
    public Burger.Core.Warfare.DesignerSystem Designer { get; }
    /// <summary>Fleets, naval battles and ship building (navy feature).</summary>
    public Burger.Core.Navy.NavySystem Navy { get; }
    /// <summary>Air regions, air wings and their missions (air feature).</summary>
    public Burger.Core.Air.AirSystem Air { get; }
    /// <summary>Lend-lease and volunteers (lend_lease feature).</summary>
    public Burger.Core.Warfare.SupportSystem Support { get; }
    /// <summary>Named formulas from common/script_values (some replace engine formulas).</summary>
    public ScriptValueSystem ScriptValues { get; }
    /// <summary>Modifiers from common/modifiers (timed, permanent and conditional).</summary>
    public ModifierSystem Modifiers { get; }
    /// <summary>Custom resources from common/resources (prestige, stability...).</summary>
    public ResourceSystem Resources { get; }
    /// <summary>Decisions from common/decisions.</summary>
    public DecisionSystem Decisions { get; }
    /// <summary>Custom panels and map modes from interface/ (or added from C#).</summary>
    public InterfaceRegistry Interface { get; } = new();
    public ModManager Mods { get; }
    /// <summary>The AI. Replace with your own subclass from a C# mod: <c>Game.Ai = new MyAi(Game);</c></summary>
    public AiSystem Ai { get; set; }
    public GameClock Clock { get; }
    /// <summary>Developer console commands. Mods can register their own.</summary>
    public ConsoleCommands Commands { get; }

    public readonly List<Notification> Notifications = new();
    public readonly List<Proposal> PendingProposals = new();
    public bool Started { get; private set; }

    /// <summary>Neighbouring countries per country (cache used by NeighborCountries).</summary>
    internal readonly Dictionary<Country, (long Stamp, int Version, List<Country> List)> NeighborCache = new();
    /// <summary>Changes every time ownership or control changes.</summary>
    public int MapVersion { get; private set; }

    /// <summary>Raised whenever ownership/control changes so the renderer can refresh map colors.</summary>
    public event Action? MapChanged;
    public event Action<Notification>? NotificationAdded;

    public Game()
    {
        Script = new ScriptEngine(this);
        Events = new EventSystem(this);
        Characters = new CharacterSystem(this);
        Diplomacy = new DiplomacySystem(this);
        Warfare = new WarfareSystem(this);
        Economy = new EconomySystem(this);
        Combat = new CombatRegistry();
        Production = new Burger.Core.Production.ProductionSystem(this);
        Supply = new Burger.Core.Supply.SupplySystem(this);
        Designer = new Burger.Core.Warfare.DesignerSystem(this);
        Navy = new Burger.Core.Navy.NavySystem(this);
        Air = new Burger.Core.Air.AirSystem(this);
        Support = new Burger.Core.Warfare.SupportSystem(this);
        ScriptValues = new ScriptValueSystem(this);
        Modifiers = new ModifierSystem(this);
        Resources = new ResourceSystem(this);
        Decisions = new DecisionSystem(this);
        Mods = new ModManager(this);
        Ai = new AiSystem(this);
        Clock = new GameClock(this);
        Commands = new ConsoleCommands(this);
        Builtins.RegisterAll(this);
        Modifiers.Register();
    }

    /// <summary>Loads a game folder (the one containing game.txt, common/, map/ ...).</summary>
    /// <param name="gameRoot">Path to the game folder.</param>
    /// <param name="compileScripts">Compile C# scripts in scripts/ (disable for pure data validation).</param>
    public static Game Load(string gameRoot, bool compileScripts = true, int? seed = null)
    {
        var game = new Game();
        game.Rng = seed.HasValue ? new Random(seed.Value) : new Random();
        game.Data.Root = Path.GetFullPath(gameRoot);
        if (!File.Exists(Path.Combine(game.Data.Root, "game.txt")) && !Directory.Exists(Path.Combine(game.Data.Root, "common")))
            throw new GameLoadException($"'{game.Data.Root}' is not a game folder (no game.txt or common/ folder found).");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        game.Features = Burger.Core.Modding.FeatureSet.Load(game.Data.Root);
        DataLoader.LoadDefinitions(game);
        if (compileScripts) game.Mods.CompileAndLoad();
        WorldLoader.LoadWorld(game);
        game.Mods.Invoke(m => m.OnLoad());
        ScriptValidator.ValidateAll(game);
        for (int i = 0; i < 5; i++) game.Clock.DaysPerSecond[i + 1] = game.Data.Defines.GameSpeeds[i];
        Log.Info($"Game '{game.Data.Info.Name}' loaded in {sw.ElapsedMilliseconds} ms");
        return game;
    }

    /// <summary>Starts the game with the given player country (null = observer mode, all AI).</summary>
    public void StartNewGame(string? playerTag)
    {
        SetPlayer(playerTag);
        Started = true;
        Resources.InitStartValues();
        Modifiers.MonthlyTick();
        foreach (var c in World.Countries.Where(c => c.Exists))
        {
            if (Features.Has(F.Characters)) Characters.EnsureRuler(c);
            Economy.UpdateEstimates(c);
        }
        if (Features.Has(F.Military)) Warfare.DailyTick();
        if (Features.Has(F.Navy)) Navy.OnGameStart();
        if (Features.Has(F.Air)) Air.OnGameStart();
        if (Features.Has(F.Production)) Production.OnGameStart();
        if (Features.Has(F.Diplomacy)) Diplomacy.UpdateWarScores();
        Mods.Invoke(m => m.OnGameStart());
        foreach (var c in World.Countries.Where(c => c.Exists))
            Events.FireOnAction("on_game_start", c);
        Events.FireOnAction("on_game_start_global", null);
        RaiseMapChanged();
    }

    /// <summary>Used by the save loader.</summary>
    internal void MarkStartedFromSave()
    {
        Started = true;
        foreach (var c in World.Countries.Where(c => c.Exists)) Economy.UpdateEstimates(c);
        Diplomacy.UpdateWarScores();
    }

        public void SetPlayer(string? playerTag)
    {
        foreach (var c in World.Countries) c.IsPlayer = false;
        World.Player = World.GetCountry(playerTag);
        if (World.Player != null) World.Player.IsPlayer = true;
    }

    // =============================================================================================
    // Time
    // =============================================================================================

    /// <summary>Set to measure how long each system takes (milliseconds per system in <see cref="ProfileMs"/>).</summary>
    public bool Profiling { get; set; }
    public readonly Dictionary<string, double> ProfileMs = new();

    public void P(string name, Action a)
    {
        if (!Profiling) { a(); return; }
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        a();
        ProfileMs[name] = ProfileMs.GetValueOrDefault(name) + (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Advances the simulation by one hour.</summary>
    public void Tick()
    {
        World.Date = World.Date.AddHours(1);
        if (Features.Has(F.Military)) P("Warfare.HourlyTick", () => Warfare.HourlyTick());
        if (Features.Has(F.Navy)) P("Navy.HourlyTick", () => Navy.HourlyTick());
        Mods.Invoke(m => m.OnHourlyTick());
        if (World.Date.Hour == 0) DailyTick();
    }

    // ---------------------------------------------------------------------------------------- fog of war
    private bool[]? _visible;
    private Country? _visibleFor;

    /// <summary>False if fog of war hides this location from the player (always true without fog or without a player).</summary>
    public bool IsVisible(Location l)
    {
        if (!FogActive || World.Player == null) return true;
        if (_visible == null || _visibleFor != World.Player) UpdateVisibility();
        return _visible == null || l.Index >= _visible.Length || _visible[l.Index];
    }

    /// <summary>Recomputes what the player can see (daily, and when the player changes).</summary>
    public void UpdateVisibility()
    {
        _visibleFor = World.Player;
        _visible = World.Player != null && FogActive ? Rules.Vision.Compute(this, World.Player) : null;
        VisibilityVersion++;
    }

    /// <summary>Fog of war is on: the fog_of_war feature is installed and the fog_of_war define is not 'no'.</summary>
    public bool FogActive => Data.Defines.FogOfWar && Features.Has(F.FogOfWar);

    /// <summary>Changes every time visibility is recomputed (the renderer refreshes the map then).</summary>
    public int VisibilityVersion { get; private set; }

    private void DailyTick()
    {
        P("Visibility", UpdateVisibility);
        if (Features.Has(F.Military)) P("Warfare.DailyTick", () => Warfare.DailyTick());
        if (Features.HasSupply && Features.Has(F.Military)) P("Supply.DailyTick", () => Supply.DailyTick());
        if (Features.Has(F.Production)) P("Production.DailyTick", () => Production.DailyTick());
        if (Features.Has(F.Navy)) P("Navy.DailyTick", () => Navy.DailyTick());
        if (Features.Has(F.Air)) P("Air.DailyTick", () => Air.DailyTick());
        if (Features.Has(F.LendLease) && Features.Has(F.Military)) P("Support.DailyTick", () => Support.DailyTick());
        P("Modifiers.DailyTick", () => Modifiers.DailyTick());
        if (Features.Has(F.Military)) P("Economy.DailyTick", () => Economy.DailyTick()); // training and reinforcing divisions
        if (Features.Has(F.Diplomacy)) P("Diplomacy.DailyTick", () => Diplomacy.DailyTick());
        if (Features.Has(F.Ai)) P("Ai.DailyTick", () => Ai.DailyTick());
        P("Events.DailyTick", () => Events.DailyTick());
        Mods.Invoke(m => m.OnDailyTick());
        if (World.Date.Day == 1) MonthlyTick();
    }

    private void MonthlyTick()
    {
        if (Features.Has(F.Economy) || Features.Has(F.Military)) P("Economy.MonthlyTick", () => Economy.MonthlyTick()); // gold (economy) and manpower (military)
        if (Features.Has(F.LendLease)) P("Support.MonthlyTick", () => Support.MonthlyTick());
        P("Resources.MonthlyTick", () => Resources.MonthlyTick());
        P("Modifiers.MonthlyTick", () => Modifiers.MonthlyTick());
        if (Features.Has(F.Diplomacy)) P("Diplomacy.MonthlyTick", () => Diplomacy.MonthlyTick());
        if (Features.Has(F.Characters)) P("Characters.MonthlyTick", () => Characters.MonthlyTick());
        P("Events.MonthlyTick", () => Events.MonthlyTick());
        if (Features.Has(F.Ai)) P("Ai.MonthlyTick", () => Ai.MonthlyTick());
        Mods.Invoke(m => m.OnMonthlyTick());
        if (World.Date.Month == 1)
        {
            if (Features.Has(F.Characters)) Characters.YearlyTick();
            foreach (var c in World.Countries.Where(c => c.Exists))
                Events.FireOnAction("on_yearly_pulse", c);
            Mods.Invoke(m => m.OnYearlyTick());
        }
        if (Notifications.Count > 300) Notifications.RemoveRange(0, Notifications.Count - 300);
    }

    // =============================================================================================
    // Helpers used by every system
    // =============================================================================================

    public void Notify(string text, NotificationKind kind = NotificationKind.Info, Location? location = null)
    {
        var n = new Notification(World.Date, text, kind, location);
        Notifications.Add(n);
        NotificationAdded?.Invoke(n);
    }

    /// <summary>Notify only if the player is one of the given countries.</summary>
    public void NotifyPlayer(string text, NotificationKind kind, params Country?[] involved)
    {
        if (World.Player == null) return;
        if (involved.Length == 0 || involved.Contains(World.Player)) Notify(text, kind);
    }

    public void RaiseMapChanged() { MapVersion++; MapChanged?.Invoke(); }

    /// <summary>Creates a script context with ROOT = <paramref name="root"/>.</summary>
    public ScriptContext Context(IScope? root, Dictionary<string, IScope?>? scopes = null) => new(this, root, scopes);

    /// <summary>Runs a block of script effects (e.g. from the console). Returns false if parsing failed.</summary>
    public bool RunEffect(string effectText, IScope? root)
    {
        var errorsBefore = Log.ErrorCount;
        var node = Burger.Core.Script.ScriptParser.Parse(effectText, "console");
        if (Log.ErrorCount > errorsBefore) return false;
        Script.Execute(node, Context(root));
        RaiseMapChanged();
        return Log.ErrorCount == errorsBefore;
    }

    public bool EvalTrigger(string triggerText, IScope? root)
    {
        var node = Burger.Core.Script.ScriptParser.Parse(triggerText, "console");
        return Script.EvalTrigger(node, Context(root));
    }
}

/// <summary>Real-time to game-time conversion with Paradox-style speed levels 1-5.</summary>
public sealed class GameClock
{
    private readonly Game _game;
    private double _accumulator;
    private readonly System.Diagnostics.Stopwatch _budget = new();

    /// <summary>
    /// In-game DAYS per real second for speeds 1..5 (index 0 unused). 0 = as fast as the computer allows.
    /// Set from <c>game_speeds = { 0.5 1.5 4 10 0 }</c> in defines.txt.
    /// </summary>
    public double[] DaysPerSecond = { 0, 0.5, 1.5, 4, 10, 0 };
    public int Speed { get; set; } = 2;
    public bool Paused { get; set; } = true;
    /// <summary>Real milliseconds per frame the simulation may use (keeps the game responsive at high speed).</summary>
    public double FrameBudgetMs = 14;

    /// <summary>Milliseconds the last Update spent simulating.</summary>
    public double LastUpdateMs { get; private set; }
    /// <summary>Measured in-game days per real second (for the UI).</summary>
    public double ActualDaysPerSecond { get; private set; }
    private double _measureTime, _measureHours;

    public GameClock(Game game) { _game = game; }

    /// <summary>Call once per frame with the frame time; runs as many hourly ticks as needed.</summary>
    public int Update(double deltaSeconds)
    {
        if (Paused || !_game.Started) { _accumulator = 0; LastUpdateMs = 0; return 0; }
        double daysPerSec = DaysPerSecond[Math.Clamp(Speed, 1, 5)];
        bool unlimited = daysPerSec <= 0;
        if (!unlimited) _accumulator += deltaSeconds * daysPerSec * 24;
        int ticks = 0;
        _budget.Restart();
        double budget = Speed >= 5 ? Math.Max(FrameBudgetMs, 30) : FrameBudgetMs; // top speed trades some smoothness for speed
        while ((unlimited || _accumulator >= 1) && _budget.Elapsed.TotalMilliseconds < budget)
        {
            if (!unlimited) _accumulator -= 1;
            _game.Tick();
            ticks++;
            if (Paused) { _accumulator = 0; break; }
        }
        LastUpdateMs = _budget.Elapsed.TotalMilliseconds;
        // never build up a backlog that makes the game run on after the player pauses
        if (_accumulator > 48) _accumulator = 48;
        _measureTime += deltaSeconds;
        _measureHours += ticks;
        if (_measureTime >= 1)
        {
            ActualDaysPerSecond = _measureHours / 24 / _measureTime;
            _measureTime = 0;
            _measureHours = 0;
        }
        return ticks;
    }
}
