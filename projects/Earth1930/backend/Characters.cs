// Character rules: succession, heirs, natural death and births.
//
// This file is BACKEND code: it is compiled into your game's EXE and players cannot change it.
// It starts as a copy of the engine's default. Edit anything you like; delete the file to use the engine default again.

/// <summary>
/// Overridable character rules (succession, mortality, fertility). Subclass and assign to
/// <c>Game.Rules.Characters</c> from a C# mod.
/// </summary>
public class GameCharacterRules : CharacterRules
{
    /// <summary>Chooses the next ruler when the current one dies. Return null to generate a new character.</summary>
    public override Character? ChooseSuccessor(Game game, Country country, Character? oldRuler)
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
    public override Character? ChooseHeir(Game game, Country country)
    {
        var ruler = country.Ruler;
        if (ruler == null) return null;
        var now = game.World.Date;
        var child = ruler.Children.Where(c => c.Alive && c.Country == country).OrderBy(c => c.Female).ThenByDescending(c => c.Age(now)).FirstOrDefault();
        return child;
    }

    /// <summary>Yearly chance (0-1) that a character dies of natural causes.</summary>
    public override double YearlyDeathChance(Game game, Character c)
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
    public override double YearlyBirthChance(Game game, Character mother)
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

/// <summary>Installs the character rules.</summary>
public sealed class CharactersSetup : GameScript
{
    // runs before mod scripts (priority 0), so moddable scripts can still replace these rules
    public override int Priority => -1000;

    public override void OnLoad()
    {
        Game.Rules.Characters = new GameCharacterRules();
    }
}
