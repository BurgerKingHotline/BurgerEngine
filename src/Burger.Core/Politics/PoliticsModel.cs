using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>An ideology from <c>common/ideologies/*.txt</c> (democratic, communist, fascist, neutrality...).</summary>
public sealed class IdeologyDef
{
    public string Key = "";
    public string Name = "";
    /// <summary>Used for country names of rebels ("Nationalist Spain") and in texts.</summary>
    public string Adjective = "";
    /// <summary>Default party name (countries can name their own party: set_party_name, history).</summary>
    public string PartyName = "";
    public string Desc = "";
    public Rgb Color = new(160, 160, 160);
    /// <summary>Countries ruled by this ideology hold elections.</summary>
    public bool Elections;
    /// <summary>Gains support when stability is low or a war goes badly.</summary>
    public bool Extremist;
    /// <summary>Ideologies whose parties this one will form a coalition with (both must accept each other).</summary>
    public HashSet<string> Coalition = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Opinion a country ruled by this ideology has of countries ruled by each ideology.</summary>
    public Dictionary<string, double> Opinion = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Modifier values for countries ruled by this ideology.</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional government word set when this ideology takes power (e.g. socialist_republic).</summary>
    public string? Government;
    public int Order;
    public Node Definition = Node.Root("");
}

/// <summary>Starting politics for countries with a government word, from <c>common/governments/*.txt</c>.</summary>
public sealed class GovernmentDef
{
    public string Key = "";
    public string? Ruling;
    public Dictionary<string, double> Popularity = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Starting law per law group.</summary>
    public Dictionary<string, string> Laws = new(StringComparer.OrdinalIgnoreCase);
    public int? Seats;
}

/// <summary>A group of laws (conscription, economy...) of which a country has exactly one, from <c>common/laws/*.txt</c>.</summary>
public sealed class LawGroup
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Law countries start with when nothing else says so (first law if not set).</summary>
    public string Default = "";
    public List<LawDef> Laws = new();
    public int Order;
}

public sealed class LawDef
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    public LawGroup Group = null!;
    /// <summary>Political power to enact it.</summary>
    public double Cost;
    /// <summary>Shown only while true (country scope).</summary>
    public Node? Potential;
    /// <summary>Can be enacted only while true (country scope).</summary>
    public Node? Allow;
    public Node? AiWillDo;
    /// <summary>Effects run when the law is enacted (country scope).</summary>
    public Node? OnEnact;
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Parties of these ideologies may not win seats or govern.</summary>
    public HashSet<string> Bans = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>No elections while this law is in force.</summary>
    public bool SuspendsElections;
    /// <summary>One-party state: every party except the ruling one is banned and there are no elections.</summary>
    public bool SingleParty;
    public Node Definition = Node.Root("");
}

/// <summary>A cabinet post (cabinet feature), from <c>common/cabinet/*.txt</c>.</summary>
public sealed class CabinetPosition
{
    public string Key = "";
    public string Name = "";
    public string Desc = "";
    /// <summary>Character stat that makes a minister good at this post.</summary>
    public string Stat = "";
    /// <summary>Modifier values per point of that stat.</summary>
    public Dictionary<string, double> PerPoint = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Modifier values while the post is filled at all.</summary>
    public Dictionary<string, double> Modifier = new(StringComparer.OrdinalIgnoreCase);
    public double Cost;
    public int Order;
}

/// <summary>The political state of one country.</summary>
public sealed class CountryPolitics
{
    /// <summary>Ideology of the ruling party.</summary>
    public string Ruling = "";
    /// <summary>Ideologies of the parties in government (ruling party first, then coalition partners).</summary>
    public List<string> Government = new();
    /// <summary>Support of each ideology (fractions that add up to 1).</summary>
    public Dictionary<string, double> Popularity = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Seats in parliament per ideology (from the last election).</summary>
    public Dictionary<string, int> Seats = new(StringComparer.OrdinalIgnoreCase);
    public int TotalSeats = 100;
    public GameDate? LastElection;
    public GameDate? NextElection;
    /// <summary>Law in force per law group.</summary>
    public Dictionary<string, string> Laws = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Day each law group last changed (for the cooldown).</summary>
    public Dictionary<string, int> LawChangedDay = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Minister per cabinet post.</summary>
    public Dictionary<string, Character> Ministers = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Party names that replace the ideology's default party name.</summary>
    public Dictionary<string, string> PartyNames = new(StringComparer.OrdinalIgnoreCase);
}

public enum MovementDemand
{
    /// <summary>Lift the ban on the ideology's party (change to <see cref="Movement.TargetLaw"/>).</summary>
    Legalize,
    /// <summary>Put the ideology in power.</summary>
    Power,
}

/// <summary>
/// A political movement (Victoria 3 style): supporters of an ideology that is shut out of power gather
/// strength until they revolt. Giving in to the demand dissolves it.
/// </summary>
public sealed class Movement
{
    public Country Country = null!;
    public string Ideology = "";
    public MovementDemand Demand;
    public string? TargetLaw;
    /// <summary>0-100. At 100 the movement revolts.</summary>
    public double Progress;
    public GameDate Started;
}

/// <summary>A running civil war between a country and the rebels that split from it.</summary>
public sealed class CivilWar
{
    public War War = null!;
    public Country Origin = null!;
    public Country Rebel = null!;
    public string Ideology = "";
}
