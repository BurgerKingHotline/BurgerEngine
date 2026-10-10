using Burger.Core.Model;

namespace Burger.Core.Scripting;

/// <summary>Registers every built-in trigger, effect, value, scope link and iterator.</summary>
public static partial class Builtins
{
    public static void RegisterAll(Game game)
    {
        RegisterLinks(game);
        RegisterIterators(game);
        RegisterValues(game);
        RegisterTriggers(game);
        RegisterEffects(game);
    }

    private static Country? C(IScope? s) => ScopeHelpers.CountryOf(s);

    private static void RegisterLinks(Game game)
    {
        var s = game.Script;
        s.RegisterLink("owner", (ctx, x) => x switch
        {
            Location l => l.Owner,
            State st => st.Owner,
            Character ch => ch.Country,
            _ => null,
        });
        s.RegisterLink("controller", (ctx, x) => (x as Location)?.Controller);
        s.RegisterLink("capital", (ctx, x) => C(x)?.Capital);
        s.RegisterLink("capital_scope", (ctx, x) => C(x)?.Capital);
        s.RegisterLink("ruler", (ctx, x) => C(x)?.Ruler);
        s.RegisterLink("heir", (ctx, x) => C(x)?.Heir);
        s.RegisterLink("country", (ctx, x) => C(x));
        s.RegisterLink("employer", (ctx, x) => (x as Character)?.Country);
        s.RegisterLink("state", (ctx, x) => (x as Location)?.State);
        s.RegisterLink("father", (ctx, x) => (x as Character)?.Father);
        s.RegisterLink("mother", (ctx, x) => (x as Character)?.Mother);
        s.RegisterLink("spouse", (ctx, x) => (x as Character)?.Spouse);
        s.RegisterLink("overlord", (ctx, x) => C(x) is { } c ? game.World.Relations.OverlordOf(c) : null);
        s.RegisterLink("attacker_leader", (ctx, x) => (x as War)?.AttackerLeader);
        s.RegisterLink("defender_leader", (ctx, x) => (x as War)?.DefenderLeader);
    }

    private static void RegisterIterators(Game game)
    {
        var s = game.Script;
        var w = game.World;
        s.RegisterIterator("country", (ctx, x) => game.World.Countries.Where(c => c.Exists));
        s.RegisterIterator("owned_state", (ctx, x) => C(x)?.OwnedStates.ToList() ?? Enumerable.Empty<IScope>());
        s.RegisterIterator("owned_location", (ctx, x) => C(x)?.OwnedStates.SelectMany(st => st.Locations).ToList() ?? Enumerable.Empty<IScope>());
        s.RegisterIterator("controlled_location", (ctx, x) => C(x) is { } c ? game.World.Locations.Where(l => l.Controller == c).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("location", (ctx, x) => x switch
        {
            State st => st.Locations,
            Country c => c.OwnedStates.SelectMany(st => st.Locations).ToList(),
            _ => Enumerable.Empty<IScope>(),
        });
        s.RegisterIterator("neighbor_location", (ctx, x) => (x as Location)?.Neighbors.Select(a => a.To).ToList() ?? Enumerable.Empty<IScope>());
        s.RegisterIterator("neighbor_country", (ctx, x) => C(x) is { } c ? NeighborCountries(game, c) : Enumerable.Empty<IScope>());
        s.RegisterIterator("ally", (ctx, x) => C(x) is { } c ? game.World.Relations.AlliesOf(c).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("subject", (ctx, x) => C(x) is { } c ? game.World.Relations.SubjectsOf(c).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("enemy", (ctx, x) => C(x) is { } c ? game.World.WarsOf(c).SelectMany(war => war.EnemiesOf(c)).Distinct().ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("war", (ctx, x) => C(x) is { } c ? game.World.WarsOf(c).ToList() : game.World.Wars.ToList());
        s.RegisterIterator("character", (ctx, x) => C(x) is { } c && x is not Character
            ? game.World.CharactersOf(c).ToList()
            : game.World.LivingCharacters.ToList());
        s.RegisterIterator("child", (ctx, x) => (x as Character)?.Children.Where(ch => ch.Alive).ToList() ?? Enumerable.Empty<IScope>());
        s.RegisterIterator("core_state", (ctx, x) => C(x) is { } c ? game.World.States.Where(st => st.Cores.Contains(c)).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("claimed_state", (ctx, x) => C(x) is { } c ? game.World.States.Where(st => st.Claims.Contains(c)).ToList() : Enumerable.Empty<IScope>());
        s.RegisterIterator("state", (ctx, x) => game.World.States);
        s.RegisterIterator("war_participant", (ctx, x) => x is War war ? war.Attackers.Concat(war.Defenders).ToList() : Enumerable.Empty<IScope>());
        _ = w;
    }

    public static List<Country> NeighborCountries(Game game, Country c)
    {
        // asked very often by the AI and by triggers: cached for the current in-game hour
        long stamp = (long)game.World.Date.TotalDays * 24 + game.World.Date.Hour;
        if (game.NeighborCache.TryGetValue(c, out var cached) && cached.Stamp == stamp && cached.Version == game.MapVersion) return cached.List;
        var set = new HashSet<Country>();
        foreach (var st in c.OwnedStates)
            foreach (var l in st.Locations)
                foreach (var a in l.Neighbors)
                {
                    var o = a.To.Owner;
                    if (o != null && o != c && a.Kind != AdjacencyKind.Sea) set.Add(o);
                }
        var list = set.ToList();
        game.NeighborCache[c] = (stamp, game.MapVersion, list);
        return list;
    }

    private static void RegisterValues(Game game)
    {
        var s = game.Script;
        var w = game.World;
        s.RegisterValue("gold", (ctx, x) => C(x)?.Gold);
        s.RegisterValue("manpower", (ctx, x) => x is State st ? st.Manpower : C(x)?.Manpower);
        s.RegisterValue("tax", (ctx, x) => (x as State)?.Tax);
        s.RegisterValue("income", (ctx, x) => C(x)?.LastIncome);
        s.RegisterValue("expenses", (ctx, x) => C(x)?.LastExpenses);
        s.RegisterValue("num_of_states", (ctx, x) => C(x)?.OwnedStates.Count);
        s.RegisterValue("num_of_locations", (ctx, x) => x is State st ? st.Locations.Count : C(x)?.OwnedStates.Sum(o => o.Locations.Count));
        s.RegisterValue("num_of_divisions", (ctx, x) => C(x) is { } c ? game.World.Divisions.Count(d => d.Owner == c) : null);
        s.RegisterValue("num_of_allies", (ctx, x) => C(x) is { } c ? game.World.Relations.AlliesOf(c).Count() : null);
        s.RegisterValue("num_of_subjects", (ctx, x) => C(x) is { } c ? game.World.Relations.SubjectsOf(c).Count() : null);
        s.RegisterValue("num_of_wars", (ctx, x) => C(x) is { } c ? game.World.WarsOf(c).Count() : null);
        s.RegisterValue("num_of_characters", (ctx, x) => C(x) is { } c ? game.World.CharactersOf(c).Count() : null);
        s.RegisterValue("num_of_children", (ctx, x) => (x as Character)?.Children.Count(ch => ch.Alive));
        s.RegisterValue("military_strength", (ctx, x) => C(x) is { } c ? game.Rules.Diplomacy.MilitaryStrength(game, c) : null);
        s.RegisterValue("free_relations", (ctx, x) => C(x) is { } c ? game.Diplomacy.MaxRelations(c) - game.Diplomacy.RelationsUsed(c) : null);
        s.RegisterValue("age", (ctx, x) => (x as Character)?.Age(game.World.Date));
        s.RegisterValue("loyalty", (ctx, x) => (x as Character)?.Loyalty);
        s.RegisterValue("health", (ctx, x) => (x as Character)?.Health);
        s.RegisterValue("fort", (ctx, x) => (x as Location)?.Fort);
        s.RegisterValue("war_score", (ctx, x) => (x as War)?.WarScore);
        s.RegisterValue("year", (ctx, x) => game.World.Date.Year);
        s.RegisterValue("month", (ctx, x) => game.World.Date.Month);
        s.RegisterValue("ruler_age", (ctx, x) => C(x)?.Ruler?.Age(game.World.Date));
        // character stats (martial, finesse, ...) are registered after defines are loaded, see RegisterStatValues
        _ = w;
    }

    /// <summary>Registers numeric values and add_/set_ effects for every stat in <c>character_stats</c>.</summary>
    public static void RegisterStatValues(Game game)
    {
        foreach (var stat in game.Data.Defines.CharacterStats)
        {
            string st = stat;
            game.Script.RegisterValue(st, (ctx, x) => x switch
            {
                Character ch => ch.GetStat(st),
                Country c => c.Ruler?.GetStat(st),
                _ => null,
            });
            game.Script.RegisterEffect("add_" + st, (ctx, n) =>
            {
                if (ctx.This is not Character ch) { Log.ErrorOnce("addstat:" + n.Location, $"add_{st} needs a character scope", n.Location); return; }
                ch.Stats[st] = ch.Stats.GetValueOrDefault(st) + game.Script.ResolveNumber(n.Value, ctx);
            }, (ctx, n) => $"{Localisation.GetOrPretty(st)}: {ScriptEngine.FormatSigned(game.Script.ResolveNumber(n.Value, ctx))}");
        }
    }
}
