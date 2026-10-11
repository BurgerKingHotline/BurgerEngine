// Focus rules: how long a focus takes and how often an idle AI country starts one.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

public class GameFocusRules : FocusRules
{
    /// <summary>Days a focus takes for a country: its days, faster with the modifier focus_speed (a fraction, 0.1 = 10% faster).</summary>
    public override double Days(Game game, Country c, FocusDef f)
        => Math.Max(7, f.Days / Math.Max(0.25, 1 + game.Modifiers.Sum(c, "focus_speed")));

    /// <summary>
    /// What the Historical Bias does to the weight the AI gives a focus: a country's own tree is its history, so its weights count in full at bias 1
    /// and a quarter at bias 0; the generic default tree is not history and is not scaled.
    /// </summary>
    public override double AiWeight(Game game, Country c, FocusDef f, double weight)
        => f.Tree.Default ? weight : weight * (0.25 + 0.75 * Math.Clamp(game.HistoricalBias, 0, 1));

    /// <summary>Chance per month that an idle AI country starts a focus at all (the AI otherwise picks one by its ai_will_do weights).</summary>
    public override double AiStartChance(Game game, Country c) => game.Data.Defines.Get("ai_focus_chance", 0.9);
}

/// <summary>Installs these rules.</summary>
public sealed class FocusSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Focus = new GameFocusRules();
    }
}
