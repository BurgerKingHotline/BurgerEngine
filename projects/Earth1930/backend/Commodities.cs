// Commodity rules: monthly output of a state, what a country needs besides its military lines, who sells to whom and how
// a shortage slows production.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

public class GameCommodityRules : CommodityRules
{
    /// <summary>Monthly output of a state: its deposits (units a year / 12) scaled by what the owner controls and by the modifier extraction_factor.</summary>
    public override double Output(Game game, Country c, State s, CommodityDef k, double deposit)
        => deposit / 12.0 * s.ControlledShare(c) * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "extraction") + game.Modifiers.Sum(c, k.Key + "_output"));

    /// <summary>Monthly use of a country besides its military lines: its civilian factories (industry, consumer goods, construction).</summary>
    public override double BaseDemand(Game game, Country c, CommodityDef k)
        => game.Production.Enabled ? game.Production.CivilianFactories(c) * k.CivilianUse : 0;

    /// <summary>May the seller sell to the buyer? Not at war, not hated, and not a country that has put an embargo on it (flag embargo_TAG).</summary>
    public override bool WillTrade(Game game, Country seller, Country buyer)
    {
        if (game.World.AtWar(seller, buyer)) return false;
        if (seller.Vars.HasFlag("embargo_" + buyer.Tag)) return false;
        // allies of the seller's enemies do not get its goods
        foreach (var war in game.World.WarsOf(seller)) if (war.Involves(buyer) && war.EnemiesOf(seller).Contains(buyer)) return false;
        return game.Diplomacy.Opinion(seller, buyer) > game.Data.Defines.Get("trade_min_opinion", -60);
    }

    /// <summary>Who the seller prefers to sell to (higher first): allies and subjects, then friends.</summary>
    public override double TradePriority(Game game, Country seller, Country buyer)
    {
        var r = game.World.Relations;
        double p = game.Diplomacy.Opinion(seller, buyer);
        if (r.AreAllied(seller, buyer)) p += 100;
        if (r.OverlordOf(buyer) == seller || r.OverlordOf(seller) == buyer) p += 150;
        return p;
    }

    /// <summary>Share (0-1) of its demand a country gets, as a speed for what it makes with it: a shortage hurts, but never stops everything (commodity_floor, 0.15).</summary>
    public override double Speed(Game game, double satisfied) => Math.Clamp(Math.Max(game.Data.Defines.Get("commodity_floor", 0.15), satisfied), 0, 1);
}

/// <summary>Installs these rules.</summary>
public sealed class CommoditiesSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Commodities = new GameCommodityRules();
    }
}
