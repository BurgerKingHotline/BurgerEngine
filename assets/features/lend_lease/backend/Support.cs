// Lend-lease and volunteer rules: how many volunteer divisions a country may send, who may receive volunteers or
// equipment, and the opinion the receiver gains.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>Overridable rules for lend-lease and volunteers. Subclass and assign to <c>Game.Rules.Support</c> (the lend_lease feature puts a copy in backend/Support.cs).</summary>
public class GameSupportRules : SupportRules
{
    /// <summary>Most divisions a country may have abroad as volunteers.</summary>
    public override int MaxVolunteers(Game game, Country from)
    {
        int own = game.World.Divisions.Count(d => d.Owner == from) + game.World.Divisions.Count(d => d.VolunteerFrom == from);
        return Math.Max(1, (int)Math.Floor(own * game.Data.Defines.Get("volunteer_share", 0.15)));
    }

    /// <summary>Why <paramref name="from"/> cannot send volunteers to <paramref name="to"/> (null = it can).</summary>
    public override string? CannotSendVolunteers(Game game, Country from, Country to)
    {
        if (from == to) return "Not to yourself";
        var w = game.World;
        if (!w.WarsOf(to).Any()) return $"{to.Name} is not at war";
        if (w.AtWar(from, to)) return "You are at war with them";
        var enemies = w.WarsOf(to).SelectMany(x => x.EnemiesOf(to)).ToHashSet();
        if (enemies.Contains(from)) return "You are their enemy";
        if (enemies.Any(e => w.Relations.AreAllied(from, e))) return "You are allied with their enemy";
        if (w.WarsOf(from).Any(x => x.SideOf(from).Contains(to))) return "You fight in the same war already";
        if (w.Divisions.Count(d => d.VolunteerFrom == from) >= MaxVolunteers(game, from)) return $"At most {MaxVolunteers(game, from)} volunteer divisions";
        if (to.Capital == null || to.Capital.Controller != to) return $"{to.Name} has no capital to land in";
        return null;
    }

    /// <summary>Why <paramref name="from"/> cannot lend-lease to <paramref name="to"/> (null = it can).</summary>
    public override string? CannotLendLease(Game game, Country from, Country to)
    {
        if (from == to) return "Not to yourself";
        if (!game.Features.Has(FeatureSet.Production)) return "Needs the production feature";
        if (game.World.AtWar(from, to)) return "You are at war with them";
        if (!to.Exists) return "They do not exist";
        return null;
    }

    /// <summary>Opinion the receiver gains (once) for volunteers / for lend-lease.</summary>
    public override double VolunteerOpinion(Game game) => game.Data.Defines.Get("volunteer_opinion", 20);
    public override double LendLeaseOpinion(Game game) => game.Data.Defines.Get("lend_lease_opinion", 15);
}

/// <summary>Installs the support rules.</summary>
public sealed class SupportSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Support = new GameSupportRules();
    }
}
