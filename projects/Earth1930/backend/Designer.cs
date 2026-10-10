// Division designer rules: how many battalions a division may have, which designs are allowed and which
// unit types a country may use (by default: a unit type with potential = { ... } needs that trigger).
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>Overridable division designer rules. Subclass and assign to <c>Game.Designer.Rules</c>.</summary>
public class GameDesignerRules : DesignerRules
{
    public override int MaxBattalions(Game game, Country c) => (int)game.Data.Defines.Get("designer_max_battalions", 25);

    /// <summary>Why a design is not allowed (null = fine).</summary>
    public override string? CannotSaveReason(Game game, Country c, string name, IReadOnlyList<(UnitType Type, int Count)> units)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the division a name";
        int total = units.Sum(u => u.Count);
        if (total <= 0) return "Add at least one battalion";
        int max = MaxBattalions(game, c);
        if (total > max) return $"At most {max} battalions";
        return null;
    }

    /// <summary>Unit types the country may use in the designer (all by default; scripts can add requirements).</summary>
    public override bool CanUse(Game game, Country c, UnitType u)
        => u.Definition.Get("potential") is not { } p || game.Script.EvalTrigger(p, game.Context(c));
}

/// <summary>Installs the designer rules.</summary>
public sealed class DesignerSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Designer.Rules = new GameDesignerRules();
    }
}
