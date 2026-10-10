// Production rules: which buildings count as factories, factory output and efficiency, construction speed and
// where buildings may be built. The production system itself (lines, stockpile, queue) uses these rules.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable production rules (HOI4 style). Subclass and assign to <c>Game.Rules.Production</c> from a C# script
/// (the production feature puts an editable copy in backend/Production.cs).
/// </summary>
public class GameProductionRules : ProductionRules
{
    /// <summary>Building keys that count as each kind of factory.</summary>
    public override string CivilianFactory => "civilian_factory";
    public override string MilitaryFactory => "military_factory";
    public override string Dockyard => "dockyard";

    /// <summary>Factories of a kind a country can use: levels in its owned states, scaled by how much of each state it controls.</summary>
    public override int Factories(Game game, Country c, string building)
    {
        double n = 0;
        foreach (var s in c.OwnedStates) n += s.Working(building) * s.ControlledShare(c); // bombed levels do not work
        return (int)Math.Floor(n + 0.001);
    }

    /// <summary>Civilian factories left for construction after consumer goods.</summary>
    public override int CivilianForConstruction(Game game, Country c)
    {
        int civ = Factories(game, c, CivilianFactory);
        double consumer = game.Data.Defines.Get("consumer_goods", 0.25) * (1 + game.Modifiers.Sum(c, "consumer_goods"));
        return Math.Max(0, (int)Math.Floor(civ * (1 - Math.Clamp(consumer, 0, 1))));
    }

    /// <summary>True if this equipment is built in dockyards (naval equipment) instead of military factories.</summary>
    public override bool UsesDockyards(EquipmentType e) => e.Category.Equals("naval", StringComparison.OrdinalIgnoreCase);

    /// <summary>Production points one line adds per day.</summary>
    public override double LineOutput(Game game, Country c, ProductionLine line, int factories)
    {
        double perFactory = game.Data.Defines.Get("production_per_factory", 4.5);
        return factories * perFactory * line.Efficiency * game.Modifiers.Factor(c, "production");
    }

    /// <summary>New efficiency after a day of production.</summary>
    public override double GrowEfficiency(Game game, Country c, ProductionLine line)
    {
        double cap = game.Data.Defines.Get("efficiency_cap", 1) * game.Modifiers.Factor(c, "efficiency_cap");
        double growth = game.Data.Defines.Get("efficiency_growth", 0.02) * game.Modifiers.Factor(c, "efficiency_growth");
        return Math.Min(cap, line.Efficiency + growth * Math.Max(0, cap - line.Efficiency) / Math.Max(0.01, cap) + 0.001);
    }

    /// <summary>Construction points per civilian factory per day (infrastructure in the state helps, as in HOI4).</summary>
    public override double ConstructionPerFactory(Game game, Country c, ConstructionProject p)
    {
        double basePts = game.Data.Defines.Get("construction_per_factory", 5);
        double infra = p.State.Buildings.GetValueOrDefault("infrastructure");
        return basePts * (1 + 0.1 * infra) * game.Modifiers.Factor(c, "construction_speed");
    }

    /// <summary>Why a building cannot be queued in a state (null = allowed).</summary>
    public override string? CannotBuildReason(Game game, Country c, BuildingType b, State s)
    {
        if (s.Owner != c) return "You must own the state";
        if (s.ControlledShare(c) < 0.5) return "Most of the state is occupied";
        int queued = c.Construction.Count(p => p.State == s && p.Building == b);
        int level = s.Buildings.GetValueOrDefault(b.Key) + queued;
        if (b.SharesSlots)
        {
            int used = game.Data.BuildingTypes.Values.Where(x => x.SharesSlots).Sum(x => s.Buildings.GetValueOrDefault(x.Key) + c.Construction.Count(p => p.State == s && p.Building == x));
            if (used >= SlotsOf(game, s)) return $"No free building slots ({SlotsOf(game, s)})";
        }
        if (level >= b.MaxLevel) return $"Maximum level ({b.MaxLevel})";
        if (b.Potential != null && !game.Script.EvalTrigger(b.Potential, game.Context(s))) return "Not possible in this state";
        return null;
    }

    /// <summary>Shared factory slots of a state (HOI4: depends on state size; here: locations + tax).</summary>
    public override int SlotsOf(Game game, State s) => (int)Math.Clamp(s.Vars.Get("building_slots") > 0 ? s.Vars.Get("building_slots") : 2 + s.Locations.Count + s.Tax / 2, 1, 25);
}

/// <summary>Installs the production rules.</summary>
public sealed class ProductionSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Production = new GameProductionRules();
    }
}
