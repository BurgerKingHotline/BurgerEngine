using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>Random name lists used when the engine generates characters for a country.</summary>
public sealed class NameList
{
    public List<string> Male = new();
    public List<string> Female = new();
    public List<string> Family = new();
}

/// <summary>A unit being trained.</summary>
public sealed class RecruitOrder
{
    public DivisionTemplate Template = null!;
    public Location Location = null!;
    public int HoursLeft;
    public int TotalHours;
    /// <summary>Training is done but there is not enough equipment yet (production feature).</summary>
    public bool WaitingForEquipment;
}

/// <summary>
/// A country, identified by a three-letter tag such as <c>GER</c>. Declared in <c>common/countries/*.txt</c>.
/// Name, color and flag can all be changed at runtime (scripts: <c>set_country_name</c>, <c>set_country_color</c>,
/// <c>set_flag_image</c>).
/// </summary>
public sealed class Country : IScope
{
    public string Tag = "";
    public int Index;

    private string _name = "";
    private string _flagPath = "";

    /// <summary>Display name (localised if it is a localisation key).</summary>
    public string Name
    {
        get => Localisation.Get(_name);
        set => _name = value;
    }
    /// <summary>The raw (unlocalised) name value, for saving.</summary>
    public string RawName => _name;
    public string DefaultName = "";
    public Rgb Color;
    public Rgb DefaultColor;

    /// <summary>Flag image path relative to the game folder. Changing it bumps <see cref="FlagVersion"/> so the renderer reloads it.</summary>
    public string FlagPath
    {
        get => _flagPath;
        set
        {
            if (_flagPath == value) return;
            _flagPath = value;
            FlagVersion++;
        }
    }
    public int FlagVersion;
    public string DefaultFlagPath = "";

    public Location? Capital;
    public double Gold;
    public double Manpower;
    public bool Playable = true;
    /// <summary>Created during the game (civil war rebels...), not in common/countries. Saved in full.</summary>
    public bool Dynamic;
    /// <summary>For dynamic countries: the tag of the country they came from.</summary>
    public string? Origin;
    public string Government = "";
    public NameList Names = new();
    public Character? Ruler;
    public Character? Heir;
    public List<RecruitOrder> Recruitment = new();
    /// <summary>Equipment in storage (production feature): equipment key -> amount.</summary>
    public Dictionary<string, double> Stockpile = new(StringComparer.OrdinalIgnoreCase);
    public List<ProductionLine> ProductionLines = new();
    public List<ConstructionProject> Construction = new();
    public VarStore Vars { get; } = new();
    public Node Definition = Node.Root("");

    // maintained by the simulation
    public bool IsPlayer;
    public List<State> OwnedStates = new();
    public double LastIncome, LastExpenses, LastManpowerGain;

    public bool Exists => OwnedStates.Count > 0;
    public bool IsAI => !IsPlayer;

    public ScopeType ScopeType => ScopeType.Country;
    public string ScopeKey => Tag;
    public string DisplayName => Name;

    public override string ToString() => Tag;

    /// <summary>Valid tags are exactly three characters, A-Z or 0-9.</summary>
    public static bool IsValidTag(string s)
        => s.Length == 3 && s.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));
}
