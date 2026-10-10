using Burger.Core.Model;

namespace Burger.Core.Modding;

/// <summary>
/// Base class for C# mod scripts. Put <c>.cs</c> files in the game's <c>scripts/</c> folder; every non-abstract
/// class deriving from <see cref="GameScript"/> is created automatically when the game loads.
/// <code>
/// public class MyMod : GameScript
/// {
///     public override void OnLoad()
///     {
///         Game.Script.RegisterEffect("add_prestige", (ctx, node) =&gt;
///             ctx.ThisCountry!.Vars.Set("prestige", ctx.ThisCountry.Vars.Get("prestige") + node.AsDouble()));
///         Game.Combat.Register(new MyCombatModel());
///         Game.Rules.Diplomacy = new MyDiplomacyRules();
///     }
///     public override void OnMonthlyTick() { ... }
/// }
/// </code>
/// </summary>
public abstract class GameScript
{
    /// <summary>The running game. Available from <see cref="OnLoad"/> onwards.</summary>
    public Game Game { get; internal set; } = null!;

    /// <summary>Lower runs first.</summary>
    public virtual int Priority => 0;

    /// <summary>After all data is loaded, before the game starts. Register triggers, effects, combat models, rules here.</summary>
    public virtual void OnLoad() { }
    /// <summary>When a new game starts (not when a save is loaded).</summary>
    public virtual void OnGameStart() { }
    /// <summary>After a save game was loaded.</summary>
    public virtual void OnSaveLoaded() { }
    public virtual void OnHourlyTick() { }
    public virtual void OnDailyTick() { }
    public virtual void OnMonthlyTick() { }
    public virtual void OnYearlyTick() { }

    public virtual void OnEventFired(EventDef ev, IScope root) { }
    public virtual void OnWarDeclared(War war) { }
    public virtual void OnPeace(PeaceOffer peace) { }
    public virtual void OnBattleEnded(Battle battle) { }
    public virtual void OnLocationControllerChanged(Location location, Country? oldController, Country? newController) { }
    public virtual void OnStateOwnerChanged(State state, Country? oldOwner, Country newOwner) { }
    public virtual void OnCharacterDied(Character character) { }
    public virtual void OnDiplomaticAction(ActionDef action, Country actor, IScope? target, bool accepted) { }
    public virtual void OnDivisionDestroyed(Division division) { }
    public virtual void OnCountryEliminated(Country country) { }
}
