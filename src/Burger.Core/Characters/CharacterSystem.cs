using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Characters;

/// <summary>
/// Overridable character rules (succession, mortality, fertility). Subclass and assign to
/// <c>Game.Rules.Characters</c> from a C# mod.
/// </summary>
public class CharacterRules
{
    /// <summary>Chooses the next ruler when the current one dies. Return null to generate a new character.</summary>
    public virtual Character? ChooseSuccessor(Game game, Country country, Character? oldRuler)
    {
        var now = game.World.Date;
        int adult = game.Data.Defines.AdultAge;
        if (country.Heir != null && country.Heir.Alive && country.Heir.Country == country) return country.Heir;
        if (oldRuler != null)
        {
            var child = oldRuler.Children.Where(c => c.Alive && c.Country == country)
                .OrderByDescending(c => c.Age(now) >= adult).ThenBy(c => c.Female).ThenByDescending(c => c.Age(now)).FirstOrDefault();
            if (child != null) return child;
        }
        return game.World.CharactersOf(country)
            .Where(c => c.Age(now) >= adult && c != oldRuler)
            .OrderByDescending(c => game.Data.Defines.CharacterStats.Sum(s => c.GetStat(s)))
            .FirstOrDefault();
    }

    /// <summary>Picks a new heir (called when there is none).</summary>
    public virtual Character? ChooseHeir(Game game, Country country)
    {
        var ruler = country.Ruler;
        if (ruler == null) return null;
        var now = game.World.Date;
        var child = ruler.Children.Where(c => c.Alive && c.Country == country).OrderBy(c => c.Female).ThenByDescending(c => c.Age(now)).FirstOrDefault();
        return child;
    }

    /// <summary>Yearly chance (0-1) that a character dies of natural causes.</summary>
    public virtual double YearlyDeathChance(Game game, Character c)
    {
        if (game.ScriptValues.TryEval("character_yearly_death_chance", c, out var scripted)) return Math.Clamp(scripted, 0, 1);
        var d = game.Data.Defines;
        int age = c.Age(game.World.Date);
        double chance = d.DeathChanceBase + Math.Max(0, age - 50) * d.DeathChancePerYearOver50;
        chance *= 1 - c.TraitModifier("health") / 100.0;
        if (age > 100) chance = 1;
        return Math.Clamp(chance, 0, 1);
    }

    /// <summary>Yearly chance (0-1) that a married woman has a child.</summary>
    public virtual double YearlyBirthChance(Game game, Character mother)
    {
        if (game.ScriptValues.TryEval("character_yearly_birth_chance", mother, out var scripted)) return Math.Clamp(scripted, 0, 1);
        var d = game.Data.Defines;
        int age = mother.Age(game.World.Date);
        if (age < d.AdultAge || age > d.MaxFertileAge || mother.Spouse == null || !mother.Spouse.Alive) return 0;
        double chance = d.BirthChancePerYear * (1 + mother.TraitModifier("fertility") + mother.Spouse.TraitModifier("fertility"));
        if (mother.Children.Count(c => c.Alive) >= 6) chance *= 0.3;
        return Math.Clamp(chance, 0, 1);
    }
}

/// <summary>Creates, ages, marries and kills characters and handles succession.</summary>
public sealed class CharacterSystem
{
    private readonly Game _game;
    public CharacterSystem(Game game) { _game = game; }

    private static readonly HashSet<string> CharacterKnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "first_name", "name", "family", "dynasty", "birth_date", "age", "female", "country", "traits", "ruler", "heir",
        "general", "father", "mother", "spouse", "portrait", "loyalty", "health", "save_scope_as", "random_traits", "flags", "dead", "death_date",
    };

    /// <summary>Registers a character (assigns id and unique key).</summary>
    public Character Register(Character c)
    {
        var w = _game.World;
        c.Id = w.NextCharacterId++;
        if (string.IsNullOrEmpty(c.Key) || w.CharacterByKey.ContainsKey(c.Key))
        {
            if (!string.IsNullOrEmpty(c.Key)) Log.Warn($"Character id '{c.Key}' already used - renamed to char_{c.Id}");
            c.Key = "char_" + c.Id;
        }
        w.Characters.Add(c);
        w.CharacterByKey[c.Key] = c;
        return c;
    }

    /// <summary>
    /// Creates a character from a script block (history/characters files and the <c>create_character</c> effect).
    /// Unspecified fields are randomised.
    /// </summary>
    public Character CreateFromNode(Node n, Country? defaultCountry, Scripting.ScriptContext? ctx = null)
    {
        var w = _game.World;
        var d = _game.Data.Defines;
        var c = new Character { Key = n.GetString("id", "")! };
        string? countryTag = n.GetString("country");
        if (countryTag != null)
        {
            if (ctx != null && _game.Script.TryResolveScope(countryTag, ctx, out var cs)) c.Country = Scripting.ScopeHelpers.CountryOf(cs);
            else c.Country = w.GetCountry(countryTag);
            if (c.Country == null) Log.Error($"Unknown country '{countryTag}' for character", n.Location);
        }
        else c.Country = defaultCountry;

        c.Female = n.GetBool("female", false);
        if (!n.Has("female") && n.Has("random_gender")) c.Female = _game.Rng.NextDouble() < 0.5;
        var names = c.Country?.Names ?? _game.Data.DefaultNames;
        c.FirstName = n.GetString("first_name") ?? n.GetString("name") ?? RandomName(names, c.Female);
        c.Family = n.GetString("family") ?? n.GetString("dynasty") ?? (n.Has("family") ? "" : RandomFamily(names));

        if (n.Has("birth_date"))
            c.BirthDate = GameDate.Parse(n.GetString("birth_date")!, w.Date.AddYears(-30));
        else
        {
            int age = n.Has("age") ? (int)ResolveNum(n, "age", ctx, 30) : 20 + _game.Rng.Next(30);
            c.BirthDate = w.Date.AddYears(-age).AddDays(-_game.Rng.Next(365));
        }

        foreach (var stat in d.CharacterStats)
            c.Stats[stat] = n.Has(stat) ? ResolveNum(n, stat, ctx, 5) : 2 + _game.Rng.Next(7);
        // extra numeric keys become stats too (e.g. a custom "prestige" stat)
        foreach (var kv in n.All)
            if (kv.Value != null && !CharacterKnownKeys.Contains(kv.Key) && !c.Stats.ContainsKey(kv.Key) && Node.TryParseDouble(kv.Value, out var v))
                c.Stats[kv.Key] = v;

        c.Loyalty = n.GetDouble("loyalty", 50);
        c.Health = n.GetDouble("health", 100);
        c.Portrait = n.GetString("portrait");
        c.IsGeneral = n.GetBool("general", false);

        foreach (var t in n.GetList("traits"))
        {
            if (_game.Data.Traits.TryGetValue(t, out var trait)) { if (!c.Traits.Contains(trait)) c.Traits.Add(trait); }
            else Log.Error($"Unknown trait '{t}'", n.Location);
        }
        int randomTraits = n.GetInt("random_traits", n.Has("traits") ? 0 : 1 + _game.Rng.Next(2));
        AddRandomTraits(c, randomTraits);

        foreach (var f in n.GetList("flags")) c.Vars.SetFlag(f);
        Register(c);

        // family links by id (characters defined earlier in the files)
        LinkParent(c, n, "father", ctx, isFather: true);
        LinkParent(c, n, "mother", ctx, isFather: false);
        var spouseKey = n.GetString("spouse");
        if (spouseKey != null)
        {
            var sp = ResolveCharacter(spouseKey, ctx);
            if (sp != null) Marry(c, sp);
            else Log.Error($"Unknown spouse '{spouseKey}'", n.Location);
        }
        if (n.GetBool("dead") || n.Has("death_date"))
            c.DeathDate = GameDate.Parse(n.GetString("death_date") ?? w.Date.ToScript(), w.Date);

        if (c.Country != null && c.Alive)
        {
            if (n.GetBool("ruler")) c.Country.Ruler = c;
            if (n.GetBool("heir")) c.Country.Heir = c;
        }
        return c;
    }

    private double ResolveNum(Node n, string key, Scripting.ScriptContext? ctx, double def)
        => ctx != null ? _game.Script.ResolveNumber(n.GetString(key), ctx, def) : n.GetDouble(key, def);

    private Character? ResolveCharacter(string key, Scripting.ScriptContext? ctx)
    {
        if (ctx != null && _game.Script.TryResolveScope(key, ctx, out var s) && s is Character sc) return sc;
        return _game.World.GetCharacter(key);
    }

    private void LinkParent(Character c, Node n, string key, Scripting.ScriptContext? ctx, bool isFather)
    {
        var pk = n.GetString(key);
        if (pk == null) return;
        var p = ResolveCharacter(pk, ctx);
        if (p == null) { Log.Error($"Unknown {key} '{pk}'", n.Location); return; }
        if (isFather) c.Father = p; else c.Mother = p;
        if (!p.Children.Contains(c)) p.Children.Add(c);
    }

    public string RandomName(NameList names, bool female)
    {
        var list = female ? names.Female : names.Male;
        if (list.Count == 0) list = female ? _game.Data.DefaultNames.Female : _game.Data.DefaultNames.Male;
        return list.Count == 0 ? "Nameless" : list[_game.Rng.Next(list.Count)];
    }

    public string RandomFamily(NameList names)
    {
        var list = names.Family.Count > 0 ? names.Family : _game.Data.DefaultNames.Family;
        return list.Count == 0 ? "" : list[_game.Rng.Next(list.Count)];
    }

    public void AddRandomTraits(Character c, int count)
    {
        var pool = _game.Data.Traits.Values.Where(t => t.RandomWeight > 0).ToList();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            var candidates = pool.Where(t => !c.Traits.Contains(t) && !c.Traits.Any(o => o.Opposites.Contains(t.Key, StringComparer.OrdinalIgnoreCase) || t.Opposites.Contains(o.Key, StringComparer.OrdinalIgnoreCase))).ToList();
            if (candidates.Count == 0) break;
            double total = candidates.Sum(t => t.RandomWeight);
            double r = _game.Rng.NextDouble() * total;
            foreach (var t in candidates)
            {
                r -= t.RandomWeight;
                if (r <= 0) { c.Traits.Add(t); break; }
            }
        }
    }

    /// <summary>Generates a random adult character for a country.</summary>
    public Character Generate(Country? country, bool? female = null, int? age = null, string? family = null)
    {
        var d = _game.Data.Defines;
        var c = new Character
        {
            Country = country,
            Female = female ?? false,
        };
        var names = country?.Names ?? _game.Data.DefaultNames;
        c.FirstName = RandomName(names, c.Female);
        c.Family = family ?? RandomFamily(names);
        int a = age ?? (25 + _game.Rng.Next(30));
        c.BirthDate = _game.World.Date.AddYears(-a).AddDays(-_game.Rng.Next(365));
        foreach (var stat in d.CharacterStats) c.Stats[stat] = 2 + _game.Rng.Next(7);
        AddRandomTraits(c, 1 + _game.Rng.Next(2));
        return Register(c);
    }

    public void Marry(Character a, Character b)
    {
        if (a == b) return;
        if (a.Spouse != null && a.Spouse != b) a.Spouse.Spouse = null;
        if (b.Spouse != null && b.Spouse != a) b.Spouse.Spouse = null;
        a.Spouse = b;
        b.Spouse = a;
    }

    public void AddTrait(Character c, Trait t)
    {
        if (c.Traits.Contains(t)) return;
        c.Traits.RemoveAll(o => o.Opposites.Contains(t.Key, StringComparer.OrdinalIgnoreCase) || t.Opposites.Contains(o.Key, StringComparer.OrdinalIgnoreCase));
        c.Traits.Add(t);
    }

    /// <summary>Kills a character, handling succession, armies and on_actions.</summary>
    public void Kill(Character c, string reason = "natural causes")
    {
        if (!c.Alive) return;
        c.DeathDate = _game.World.Date;
        c.DeathReason = reason;
        var country = c.Country;
        foreach (var army in _game.World.Armies.Where(a => a.General == c)) army.General = null;
        bool wasRuler = country != null && country.Ruler == c;
        if (country != null && country.Heir == c) country.Heir = null;
        _game.Mods.Invoke(m => m.OnCharacterDied(c));
        _game.Events.FireOnAction("on_character_death", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["country"] = country });
        if (wasRuler && country != null)
        {
            _game.NotifyPlayer($"{c.DisplayName}, ruler of {country.Name}, has died ({reason}).", NotificationKind.Character, country);
            Succession(country, c);
        }
        else if (country != null && country.IsPlayer)
        {
            _game.Notify($"{c.DisplayName} has died ({reason}).", NotificationKind.Character);
        }
    }

    /// <summary>Makes a character the ruler of their country (or of <paramref name="country"/>).</summary>
    public void SetRuler(Country country, Character newRuler)
    {
        var old = country.Ruler;
        if (old == newRuler) return;
        newRuler.Country = country;
        country.Ruler = newRuler;
        if (country.Heir == newRuler) country.Heir = null;
        country.Heir ??= _game.Rules.Characters.ChooseHeir(_game, country);
        _game.Events.FireOnAction("on_new_ruler", country, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase)
        {
            ["new_ruler"] = newRuler,
            ["old_ruler"] = old,
        });
    }

    private void Succession(Country country, Character oldRuler)
    {
        var next = _game.Rules.Characters.ChooseSuccessor(_game, country, oldRuler)
                   ?? Generate(country, false, 30 + _game.Rng.Next(20));
        country.Ruler = null;
        SetRuler(country, next);
        _game.NotifyPlayer($"{next.DisplayName} is the new ruler of {country.Name}.", NotificationKind.Character, country);
    }

    /// <summary>Gives a country a ruler if it has none (e.g. at game start).</summary>
    public void EnsureRuler(Country c)
    {
        if (c.Ruler == null || !c.Ruler.Alive || c.Ruler.Country != c)
        {
            var r = _game.Rules.Characters.ChooseSuccessor(_game, c, null) ?? Generate(c, false, 35 + _game.Rng.Next(20));
            c.Ruler = r;
        }
        if (c.Heir != null && (!c.Heir.Alive || c.Heir.Country != c || c.Heir == c.Ruler)) c.Heir = null;
        c.Heir ??= _game.Rules.Characters.ChooseHeir(_game, c);
    }

    public void MonthlyTick()
    {
        foreach (var c in _game.World.Countries.Where(c => c.Exists)) EnsureRuler(c);
    }

    public void YearlyTick()
    {
        var w = _game.World;
        var rules = _game.Rules.Characters;
        foreach (var c in w.Characters.Where(c => c.Alive).ToList())
        {
            if (_game.Rng.NextDouble() < rules.YearlyDeathChance(_game, c)) { Kill(c); continue; }
            if (c.Female && _game.Rng.NextDouble() < rules.YearlyBirthChance(_game, c)) Birth(c);
        }
    }

    /// <summary>A child is born to <paramref name="mother"/> and her spouse.</summary>
    public Character Birth(Character mother)
    {
        var father = mother.Spouse;
        var country = mother.Country ?? father?.Country;
        var child = new Character
        {
            Country = country,
            Female = _game.Rng.NextDouble() < 0.5,
            Mother = mother,
            Father = father,
            BirthDate = _game.World.Date,
            Family = father?.Family ?? mother.Family,
        };
        child.FirstName = RandomName(country?.Names ?? _game.Data.DefaultNames, child.Female);
        foreach (var stat in _game.Data.Defines.CharacterStats)
        {
            double parentAvg = ((father?.Stats.GetValueOrDefault(stat) ?? 5) + mother.Stats.GetValueOrDefault(stat)) / 2;
            child.Stats[stat] = Math.Clamp(Math.Round(parentAvg + _game.Rng.Next(-2, 3)), 0, 20);
        }
        foreach (var t in mother.Traits.Concat(father?.Traits ?? new List<Trait>()))
            if (t.InheritChance > 0 && _game.Rng.NextDouble() < t.InheritChance) AddTrait(child, t);
        Register(child);
        mother.Children.Add(child);
        father?.Children.Add(child);
        if (country != null && country.Ruler != null && (mother == country.Ruler || father == country.Ruler) && country.Heir == null)
            country.Heir = _game.Rules.Characters.ChooseHeir(_game, country);
        _game.Events.FireOnAction("on_character_born", child, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["mother"] = mother, ["father"] = father });
        if (country?.IsPlayer == true && (mother.IsRuler || father?.IsRuler == true))
            _game.Notify($"{child.DisplayName} was born to {mother.DisplayName}.", NotificationKind.Character);
        return child;
    }
}
