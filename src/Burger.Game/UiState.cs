using Burger.Core.Model;

namespace Burger.Runtime;

/// <summary>Panels on the left: your own country's menus (left menu bar) and the location/country you clicked.</summary>
public enum LeftPanel { None, Location, Country, Military, Recruit, Characters, Decisions, Custom, Production, Designer, Navy, Air, Politics, Population }
/// <summary>Panels on the right: world-wide overviews opened from the top right.</summary>
public enum RightPanel { None, Wars, Peace, World, Ledger }
/// <summary>Battle plan drawing tools: front segment, offensive arrow, fallback line.</summary>
public enum PlanTool { None, Front, Arrow, Fallback }

/// <summary>Peace deal being built by the player.</summary>
public sealed class PeaceDraft
{
    public War War = null!;
    public Country Enemy = null!;
    public HashSet<State> States = new();
    public bool Subjugate;
    public bool Annex;
    public double Gold;
    public bool Concede; // offer to give things up instead of demanding
    public bool FreeSubjects;   // independence for the loser's subjects that fight with the winner
    public bool RegimeChange;   // put the winner's ideology in power
    public bool Humiliate;
    public HashSet<string> Release = new(); // peoples of the loser to free (cultures)
}

/// <summary>Everything the player has selected or opened.</summary>
public sealed class UiState
{
    public Location? SelectedLocation;
    public Country? SelectedCountry;
    public Character? SelectedCharacter;
    public readonly List<Division> SelectedDivisions = new();
    public Army? SelectedArmy;
    /// <summary>Fleet selected on the map (navy feature): right-click moves it.</summary>
    public Fleet? SelectedFleet;
    /// <summary>Air menu: the wing being given a region (map click) or a new air base.</summary>
    public AirWing? OrderingWing;
    /// <summary>What a map click does for <see cref="OrderingWing"/>: assign a region with this mission, or rebase (Mission = null).</summary>
    public AirMission? WingMission;
    /// <summary>Battle plans: the army being planned and what map clicks draw for it.</summary>
    public Army? PlanArmy;
    public PlanTool Tool;
    public LeftPanel Left;
    public RightPanel Right;
    public bool ConsoleOpen;
    public bool ErrorsOpen;
    public bool EditorActive;
    public bool PauseMenuOpen;
    public bool SaveMenuOpen;
    public PeaceDraft? Peace;
    public string Status = "";
    public float StatusTime;
    /// <summary>Army whose "choose general" list is open.</summary>
    public Army? ChoosingGeneralFor;
    /// <summary>Division template picked in the recruit menu; clicking an own location trains it there.</summary>
    public DivisionTemplate? RecruitTemplate;
    /// <summary>Building picked in the construction tab; clicking an own state queues it there.</summary>
    public BuildingType? BuildType;
    public int ProductionTab;

    public void SetStatus(string s)
    {
        Status = s;
        StatusTime = 4f;
    }

    public void ClearSelection()
    {
        SelectedDivisions.Clear();
        SelectedArmy = null;
    }
}
