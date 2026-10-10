using Burger.Core.Map;
using Burger.Core.Model;

namespace Burger.Core.Data;

/// <summary>Everything that changes during a game: ownership, characters, armies, wars, the date...</summary>
public sealed class World
{
    public MapData Map = null!;
    public GameDate Date;

    public readonly List<Location> Locations = new();
    public readonly Dictionary<string, Location> LocationByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<int, Location> LocationByColor = new();

    public readonly List<State> States = new();
    public readonly Dictionary<string, State> StateByKey = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<Country> Countries = new();
    public readonly Dictionary<string, Country> CountryByTag = new(StringComparer.OrdinalIgnoreCase);

    public readonly List<Character> Characters = new();
    public readonly Dictionary<string, Character> CharacterByKey = new(StringComparer.OrdinalIgnoreCase);
    public int NextCharacterId = 1;

    public readonly List<Division> Divisions = new();
    public readonly List<Army> Armies = new();
    public readonly List<Battle> Battles = new();
    public readonly List<War> Wars = new();
    public int NextDivisionId = 1, NextArmyId = 1, NextBattleId = 1, NextWarId = 1;

    public readonly Diplomacy.Relations Relations = new();
    public readonly VarStore Global = new();
    public readonly HashSet<string> FiredOnceEvents = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Last day each country used each diplomatic action, for cooldowns. Key: "TAG:action".</summary>
    public readonly Dictionary<string, int> ActionCooldowns = new(StringComparer.OrdinalIgnoreCase);

    public Country? Player;

    public Country? GetCountry(string? tag) => tag != null && CountryByTag.TryGetValue(tag, out var c) ? c : null;
    public State? GetState(string? key) => key != null && StateByKey.TryGetValue(key, out var s) ? s : null;
    public Location? GetLocation(string? key) => key != null && LocationByKey.TryGetValue(key, out var l) ? l : null;
    public Character? GetCharacter(string? key) => key != null && CharacterByKey.TryGetValue(key, out var c) ? c : null;

    public Location? LocationAtPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Map.Width || y >= Map.Height) return null;
        int idx = Map.PixelLocation[y * Map.Width + x];
        return idx >= 0 ? Locations[idx] : null;
    }

    public IEnumerable<Character> LivingCharacters => Characters.Where(c => c.Alive);
    public IEnumerable<Character> CharactersOf(Country c) => Characters.Where(ch => ch.Alive && ch.Country == c);
    public IEnumerable<Division> DivisionsOf(Country c) => Divisions.Where(d => d.Owner == c);
    public IEnumerable<Army> ArmiesOf(Country c) => Armies.Where(a => a.Owner == c);
    public IEnumerable<War> WarsOf(Country c) => Wars.Where(w => w.Involves(c));

    public bool AtWar(Country a, Country b)
    {
        foreach (var w in Wars)
            if ((w.IsAttacker(a) && w.IsDefender(b)) || (w.IsDefender(a) && w.IsAttacker(b))) return true;
        return false;
    }

    public War? WarBetween(Country a, Country b)
    {
        foreach (var w in Wars)
            if ((w.IsAttacker(a) && w.IsDefender(b)) || (w.IsDefender(a) && w.IsAttacker(b))) return w;
        return null;
    }

    /// <summary>Rebuilds <see cref="Country.OwnedStates"/> caches. Call after changing state owners.</summary>
    public void RefreshOwnership()
    {
        foreach (var c in Countries) c.OwnedStates.Clear();
        foreach (var s in States) s.Owner?.OwnedStates.Add(s);
    }
}
