using Burger.Core.Script;

namespace Burger.Core.Model;

/// <summary>A reason for war from <c>common/casus_belli.txt</c>.</summary>
public sealed class CasusBelli
{
    public string Key = "";
    public string Name = "";
    /// <summary>Trigger with ROOT = attacker, scope:recipient = target.</summary>
    public Node? Allow;
    public double PeaceCostMultiplier = 1.0;
    /// <summary>Opinion every other country gets of the attacker when this CB is used (negative = infamy).</summary>
    public double OthersOpinion;
    public double AiWeight = 1;
    /// <summary>
    /// What the war is about (EU style): conquest (any land), claims, cores, culture (states where our culture is the
    /// majority), religion (states where our faith is the majority), liberation (free a people of the enemy), ideology
    /// (regime change and land), independence (a subject breaking free), subjugation, humiliation (gold).
    /// Demands that match the goal cost <see cref="GoalCostMultiplier"/> of their price.
    /// </summary>
    public WarGoalType WarGoal = WarGoalType.Conquest;
    public double GoalCostMultiplier = 0.5;
    /// <summary>Holding the war goal states adds war score over time (and not holding them after a year takes it away).</summary>
    public bool TickingWarScore;
    /// <summary>Highest total war-score cost of the demands of a peace deal under this casus belli.</summary>
    public double MaxDemands = 100;
    public Node Definition = Node.Root("");
}

public enum WarGoalType { Conquest, Claims, Cores, Culture, Religion, Liberation, Ideology, Independence, Subjugation, Humiliation }

/// <summary>What one participant of a war fights for (the attacker's casus belli, or an ally's own goal: faction war goals).</summary>
public sealed class WarGoal
{
    public Country Holder = null!;
    public Country Target = null!;
    public CasusBelli CasusBelli = null!;
}

/// <summary>A type of subject relationship (vassal, client state, tributary...) from <c>common/subject_types.txt</c>.</summary>
public sealed class SubjectType
{
    public string Key = "";
    public string Name = "";
    /// <summary>Share (0-1) of the subject's income paid to the overlord.</summary>
    public double IncomeShare = 0.1;
    public bool JoinsOverlordWars = true;
    public bool CanDeclareWar;
    /// <summary>Does a subject of this type use up one of its overlord's diplomatic relations (counts_as_relation, default yes; dominions and colonies do not).</summary>
    public bool CountsAsRelation = true;
    /// <summary>Base liberty desire of this kind of subject (EU style; added to the computed value).</summary>
    public double LibertyDesire;
    public Node Definition = Node.Root("");
}

/// <summary>An opinion modifier one country has towards another (decays over time unless permanent).</summary>
public sealed class OpinionModifier
{
    public string Name = "";
    public double Value;
    /// <summary>Points the value moves toward 0 per month (0 = permanent).</summary>
    public double DecayPerMonth;
    public GameDate? Expires;
}

/// <summary>A war between two sides. Attacker/defender leaders are the original countries.</summary>
public sealed class War : IScope
{
    public int Id;
    public string Name = "";
    public Country AttackerLeader = null!;
    public Country DefenderLeader = null!;
    public List<Country> Attackers = new();
    public List<Country> Defenders = new();
    public CasusBelli? CasusBelli;
    /// <summary>Optional war goal state.</summary>
    public State? Goal;
    /// <summary>War goals of the participants (the attacker's first; allies that join add their own).</summary>
    public List<WarGoal> Goals = new();
    /// <summary>War score from holding (or not holding) the attacker's war goal, attacker's point of view.</summary>
    public double TickingScore;
    /// <summary>Last day the front moved (occupied land changed); a long stalemate wears both sides down.</summary>
    public int LastChangeDay;
    public int LastOccupied = -1;
    public GameDate StartDate;
    /// <summary>Battle score from the attacker's point of view (-100..100 before capping).</summary>
    public double BattleScore;
    /// <summary>Cached total war score from the attacker's point of view (-100..100).</summary>
    public double WarScore;
    public VarStore Vars { get; } = new();

    public bool IsAttacker(Country c) => Attackers.Contains(c);
    public bool IsDefender(Country c) => Defenders.Contains(c);
    public bool Involves(Country c) => IsAttacker(c) || IsDefender(c);
    public List<Country> SideOf(Country c) => IsAttacker(c) ? Attackers : Defenders;
    public List<Country> EnemiesOf(Country c) => IsAttacker(c) ? Defenders : IsDefender(c) ? Attackers : new List<Country>();
    public Country LeaderOf(Country c) => IsAttacker(c) ? AttackerLeader : DefenderLeader;
    public Country EnemyLeaderOf(Country c) => IsAttacker(c) ? DefenderLeader : AttackerLeader;

    /// <summary>War score from <paramref name="c"/>'s point of view.</summary>
    public double ScoreFor(Country c) => IsAttacker(c) ? WarScore : -WarScore;

    public ScopeType ScopeType => ScopeType.War;
    public string ScopeKey => "war_" + Id;
    public string DisplayName => Name;

    public override string ToString() => Name;
}

public enum PeaceTermType { WhitePeace, TransferState, Subjugate, Annex, Gold, Independence, ReleaseCountry, RegimeChange, Humiliate, Demilitarize }

public sealed class PeaceTerm
{
    public PeaceTermType Type;
    public State? State;
    /// <summary>Country receiving the state / becoming overlord.</summary>
    public Country? Beneficiary;
    public double Amount;
    public string SubjectType = "";
    /// <summary>ReleaseCountry: the culture whose states are released.</summary>
    public string Culture = "";
    /// <summary>RegimeChange: ideology put in power.</summary>
    public string Ideology = "";
    /// <summary>ReleaseCountry: the freed people become a subject of the beneficiary (a puppet) instead of a free country.</summary>
    public bool AsSubject;

    public override string ToString() => Type switch
    {
        PeaceTermType.TransferState => $"Cede {State?.DisplayName} to {Beneficiary?.Name}",
        PeaceTermType.Subjugate => $"Become a subject of {Beneficiary?.Name}",
        PeaceTermType.Annex => $"Be annexed by {Beneficiary?.Name}",
        PeaceTermType.Gold => $"Pay {Amount:0} gold to {Beneficiary?.Name}",
        PeaceTermType.Independence => $"Free {Beneficiary?.Name} from its overlord",
        PeaceTermType.ReleaseCountry => AsSubject ? $"Release {Culture} people of {State?.DisplayName} as a subject of {Beneficiary?.Name}" : $"Release {Culture} people of {State?.DisplayName} as a free country",
        PeaceTermType.RegimeChange => $"Put the {Ideology} party in power (like {Beneficiary?.Name})",
        PeaceTermType.Humiliate => $"Be humiliated by {Beneficiary?.Name}",
        PeaceTermType.Demilitarize => $"Demilitarize {State?.DisplayName}",
        _ => "White peace",
    };
}

/// <summary>A peace proposal from one war leader to the other.</summary>
public sealed class PeaceOffer
{
    public War War = null!;
    public Country From = null!;
    public Country To = null!;
    public List<PeaceTerm> Terms = new();
    /// <summary>True if <see cref="From"/> is the one making demands (otherwise it is offering concessions).</summary>
    public bool FromIsWinner = true;
}
