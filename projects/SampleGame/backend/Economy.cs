// Economy rules: income, army upkeep, manpower and who may recruit what where.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>Overridable economy formulas. Assign a subclass to <c>Game.Rules.Economy</c> from a C# mod.</summary>
public class GameEconomyRules : EconomyRules
{
    /// <summary>Monthly gold income of a country (before upkeep).</summary>
    public override double MonthlyIncome(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_income", c, out var scripted)) return scripted;
        double income = 0;
        foreach (var s in c.OwnedStates) income += s.Tax * game.Modifiers.Factor(s, "tax") * s.ControlledShare(c);
        // occupied enemy land gives a little income too
        foreach (var l in game.World.Locations)
            if (l.Controller == c && l.Owner != null && l.Owner != c && l.State != null && l.State.Locations.Count > 0)
                income += l.State.Tax / l.State.Locations.Count * 0.25;
        income *= game.Data.Defines.IncomeMultiplier * (1 + c.Vars.Get("income_modifier") + game.Modifiers.Sum(c, "income"));
        return income;
    }

    /// <summary>Monthly upkeep of armies.</summary>
    public override double MonthlyExpenses(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_expenses", c, out var scripted)) return scripted;
        double cost = 0;
        foreach (var d in game.World.Divisions)
            if (d.Owner == c) cost += d.Template.Cost * game.Data.Defines.DivisionUpkeepFactor;
        return cost * (1 + c.Vars.Get("upkeep_modifier") + game.Modifiers.Sum(c, "upkeep"));
    }

    public override double MonthlyManpower(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_manpower", c, out var scripted)) return scripted;
        double mp = 0;
        foreach (var s in c.OwnedStates) mp += s.Manpower * s.ControlledShare(c);
        return mp * game.Data.Defines.ManpowerMultiplier * (1 + c.Vars.Get("manpower_modifier") + game.Modifiers.Sum(c, "manpower"));
    }

    /// <summary>Can the country start building this template at this location right now?</summary>
    public override string? CannotRecruitReason(Game game, Country c, DivisionTemplate t, Location loc)
    {
        if (!string.IsNullOrEmpty(t.OwnerTag) && !string.Equals(t.OwnerTag, c.Tag, StringComparison.OrdinalIgnoreCase)) return "This template belongs to another country";
        if (loc.Controller != c || loc.Owner != c) return "You must own and control the location";
        if (game.Economy.GoldCosts && c.Gold < t.Cost) return $"Needs {t.Cost:0} gold";
        if (c.Manpower < t.Manpower) return $"Needs {t.Manpower:0} manpower";
        return null;
    }
}

/// <summary>Installs the economy rules.</summary>
public sealed class EconomySetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Economy = new GameEconomyRules();
    }
}
