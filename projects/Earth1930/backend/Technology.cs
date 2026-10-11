// Technology rules: research points and slots, what a technology costs (more when researched ahead of its time), how far ahead
// countries may research, how far behind 1930 they start, and what the AI researches.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

public class GameTechRules : TechRules
{
    /// <summary>
    /// How well equipped and trained a country's army is, as a factor added to its attack, defence and breakthrough: 0.35 per point of wealth index
    /// above or below the world's average (1), at most +0.15 and at least -0.5 (a poor country's divisions are half the worth of a rich one's),
    /// plus the modifier army_quality (doctrine, training, discipline). Recomputed monthly.
    /// </summary>
    public override double ArmyQuality(Game game, Country c)
    {
        double wealth = game.Politics.Enabled ? game.Politics.WealthIndex(c) : 1;
        return Math.Clamp(game.Data.Defines.Get("army_quality_per_wealth", 0.35) * (wealth - 1), -game.Data.Defines.Get("army_quality_floor", 0.5), 0.15) + game.Modifiers.Sum(c, "army_quality");
    }

    /// <summary>Research points a country gains each month: a base, its factories, and the modifier research_speed.</summary>
    public override double MonthlyPoints(Game game, Country c)
    {
        var d = game.Data.Defines;
        double factories = game.Production.Enabled ? game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c) : c.OwnedStates.Sum(s => s.Tax) * 2;
        double pts = d.Get("research_base", 1.0) + factories * d.Get("research_per_factory", 0.06);
        return pts * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "research_speed"));
    }

    /// <summary>Technologies a country researches at the same time: one, more with a big industry (research_slot_factories, 60 and 150) or the modifier research_slots.</summary>
    public override int Slots(Game game, Country c)
    {
        var d = game.Data.Defines;
        double factories = game.Production.Enabled ? game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c) : 0;
        int n = 1 + (factories >= d.Get("research_slot_factories", 60) ? 1 : 0) + (factories >= d.Get("research_slot_factories_2", 150) ? 1 : 0);
        return Math.Max(1, n + (int)Math.Round(game.Modifiers.Sum(c, "research_slots")));
    }

    /// <summary>Points a technology costs this country: more than listed when it is researched ahead of its time (research_ahead_penalty a year, 25%).</summary>
    public override double Cost(Game game, Country c, TechDef t)
    {
        double ahead = Math.Max(0, t.Year - (game.World.Date.Year + game.World.Date.Month / 12.0));
        return t.Cost * (1 + ahead * game.Data.Defines.Get("research_ahead_penalty", 0.25));
    }

    /// <summary>How many years ahead of its date a technology may be researched (research_ahead_years, 3).</summary>
    public override double AheadYears(Game game, Country c) => game.Data.Defines.Get("research_ahead_years", 3);

    /// <summary>Years behind 1930 a country's technology starts at: poor and non-industrial countries lag (history/technologies.txt can say more).</summary>
    public override double StartLag(Game game, Country c)
    {
        if (!game.Production.Enabled) return 0;
        int f = game.Production.CivilianFactories(c) + game.Production.MilitaryFactories(c);
        return f >= 60 ? 0 : f >= 25 ? 2 : f >= 10 ? 5 : 9;
    }

    /// <summary>What the AI wants to research: older technologies first, its profile's categories more, what it needs.</summary>
    public override double AiScore(Game game, Country c, TechDef t)
    {
        double score = 10 - Math.Min(9, Math.Max(0, game.World.Date.Year - t.Year)) * 0.2;
        score *= game.AiProfiles.CategoryWeight(c, t.Category);
        score += game.Rng.NextDouble() * 0.5;
        return score / Math.Max(1, Cost(game, c, t) / 40.0);
    }
}

/// <summary>Installs these rules.</summary>
public sealed class TechSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Tech = new GameTechRules();
    }
}
