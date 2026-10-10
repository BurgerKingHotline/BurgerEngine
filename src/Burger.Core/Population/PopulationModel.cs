using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Population;

/// <summary>A culture from <c>common/cultures/*.txt</c> (inside a culture group).</summary>
public sealed class CultureDef
{
    public string Key = "";
    public string Name = "";
    public CultureGroup Group = null!;
    public Rgb Color;
    /// <summary>Religion of new pops of this culture when nothing else says so.</summary>
    public string? Religion;
    /// <summary>Name of the country its people form when they break away (independence feature); default "&lt;Name&gt; Republic".</summary>
    public string? CountryName;
}

public sealed class CultureGroup
{
    public string Key = "";
    public string Name = "";
    public Rgb Color;
    public List<CultureDef> Cultures = new();
}

/// <summary>A religion from <c>common/religions/*.txt</c> (inside a religion group). Religion feature.</summary>
public sealed class ReligionDef
{
    public string Key = "";
    public string Name = "";
    public ReligionGroup Group = null!;
    public Rgb Color;
}

public sealed class ReligionGroup
{
    public string Key = "";
    public string Name = "";
    public Rgb Color;
    public List<ReligionDef> Religions = new();
}

/// <summary>A social class from <c>common/pop_types/*.txt</c> (social_classes feature).</summary>
public sealed class PopTypeDef
{
    public string Key = "";
    public string Name = "";
    public Rgb Color = new(160, 160, 160);
    /// <summary>Share of new pops of this class when a state's pops are made up (0-1).</summary>
    public double Share;
    /// <summary>Extra yearly growth of this class.</summary>
    public double Growth;
    /// <summary>State modifiers at 100% of the state's people in this class (scaled by its share).</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Class some of these pops rise into every year (and the yearly share).</summary>
    public string? PromoteTo;
    public double PromoteRate;
    public int Order;
}

/// <summary>A group of people in a state with one culture, religion and class.</summary>
public sealed class Pop
{
    public string Culture = "";
    /// <summary>Null without the religion feature.</summary>
    public string? Religion;
    /// <summary>Null without the social_classes feature.</summary>
    public string? Type;
    /// <summary>Number of people.</summary>
    public double Size;

    public bool Same(string culture, string? religion, string? type)
        => Culture == culture && Religion == religion && Type == type;
}

/// <summary>The people of one state and what the population system keeps for it.</summary>
public sealed class StatePopulation
{
    public List<Pop> Pops = new();
    /// <summary>Total people when the game started (growth and tax are measured against it).</summary>
    public double InitialTotal;
    /// <summary>Tax and manpower of the state without population effects (from the state files).</summary>
    public double BaseTax, BaseManpower;
    /// <summary>The tax / manpower values this system last wrote (to notice changes made by scripts).</summary>
    public double WrittenTax = double.NaN, WrittenManpower = double.NaN;
    /// <summary>Who owned the state and since when (for cores).</summary>
    public Country? Owner;
    public int OwnedSinceDay;
    /// <summary>Colonization of an unowned state (colonization feature).</summary>
    public Country? Colonizer;
    public double ColonyProgress;
    /// <summary>Last month's change of the total (for the interface).</summary>
    public double LastGrowth;

    public double Total => Pops.Sum(p => p.Size);
}

/// <summary>Culture and religion of a country.</summary>
public sealed class CountryPopulation
{
    public string? Culture;
    public HashSet<string> Accepted = new(StringComparer.OrdinalIgnoreCase);
    public string? Religion;
}

/// <summary>A building being built with gold (games without the production feature).</summary>
public sealed class GoldProject
{
    public Country Country = null!;
    public State State = null!;
    public BuildingType Building = null!;
    public int MonthsLeft;
    public int TotalMonths;
}
