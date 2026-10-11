using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Diplomacy;

/// <summary>Demilitarized zones (demilitarization feature): the Rhineland, straits, islands.</summary>
public sealed partial class DiplomacySystem
{
    /// <summary>Who imposed the demilitarization of a state (null = a treaty of the start of the game).</summary>
    private readonly Dictionary<State, Country?> _demilBy = new();
    private bool _demilSaveRegistered;

    public bool DemilEnabled => _game.Features.Has(FeatureSet.Demilitarization);

    public IEnumerable<State> DemilitarizedStates => _game.World.States.Where(s => s.Demilitarized);
    public List<State> DemilitarizedOf(Country c) => c.OwnedStates.Where(s => s.Demilitarized).ToList();
    public Country? ImposedBy(State s) => _demilBy.GetValueOrDefault(s);

    private void RegisterDemilSave()
    {
        if (_demilSaveRegistered) return;
        _demilSaveRegistered = true;
        _game.SaveSections.Add(new SaveSection
        {
            Name = "demilitarization",
            Reset = () => { _demilBy.Clear(); foreach (var s in _game.World.States) s.Demilitarized = false; },
            Write = root =>
            {
                foreach (var s in _game.World.States.Where(s => s.Demilitarized))
                {
                    var n = root.AddBlock("state");
                    n.Add("key", s.Key);
                    if (_demilBy.GetValueOrDefault(s) is { } by) n.Add("by", by.Tag);
                }
            },
            Read = root =>
            {
                foreach (var n in root.GetAll("state"))
                {
                    var s = _game.World.GetState(n.GetString("key"));
                    if (s == null) continue;
                    s.Demilitarized = true;
                    _demilBy[s] = _game.World.GetCountry(n.GetString("by"));
                }
            },
        });
    }

    /// <summary>The demilitarized states at the start of the game (history/demilitarized.txt: <c>state_key = { by = FRA }</c>).</summary>
    public void LoadDemilitarized()
    {
        RegisterDemilSave();
        _demilBy.Clear();
        foreach (var s in _game.World.States) s.Demilitarized = false;
        if (!DemilEnabled) return;
        foreach (var root in Data.DataLoader.ParseFileOrFolder(_game.Data, "history/demilitarized"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var s = _game.World.GetState(n.Key);
                if (s == null) { Log.ErrorOnce("demil:" + n.Key, $"history/demilitarized: unknown state '{n.Key}'", n.Location); continue; }
                s.Demilitarized = true;
                _demilBy[s] = _game.World.GetCountry(n.GetString("by"));
            }
    }

    /// <summary>Demilitarizes a state: its owner's forces leave it (they walk home when the day ends) and none can be raised there.</summary>
    public void Demilitarize(State s, Country? by)
    {
        if (s.Demilitarized) return;
        s.Demilitarized = true;
        _demilBy[s] = by;
        _game.Warfare.EvacuateTrespassers();
        _game.NotifyPlayer($"{s.DisplayName} is demilitarized.", NotificationKind.Diplomacy, s.Owner, by);
        _game.RaiseMapChanged();
    }

    /// <summary>
    /// The owner puts forces into a demilitarized state again: how = "decision" (it dared) or "war" (it is at war, the treaty is void).
    /// A challenge goes to the countries around it and the country that imposed it (on_state_remilitarized).
    /// </summary>
    public void Remilitarize(State s, string how)
    {
        if (!s.Demilitarized) return;
        s.Demilitarized = false;
        var imposer = _demilBy.GetValueOrDefault(s);
        _demilBy.Remove(s);
        var owner = s.Owner;
        Record($"{s.DisplayName} of {owner?.Tag} remilitarized ({how})");
        _game.RaiseMapChanged();
        if (owner == null) return;
        if (how == "war") return; // a country at war does not ask anybody
        _game.Notify($"{owner.Name} has remilitarized {s.DisplayName}!", NotificationKind.War);
        var challengers = new HashSet<Country>();
        foreach (var n in _game.Population.NeighbourStates(s)) if (n.Owner is { } o && o != owner) challengers.Add(o);
        if (imposer != null && imposer != owner) challengers.Add(imposer);
        foreach (var g in R.GuarantorsOf(owner).ToList()) challengers.Remove(g);
        foreach (var c in challengers.Where(c => c.Exists))
            _game.Events.FireOnAction("on_state_remilitarized", c, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["state"] = s, ["remilitarizer"] = owner });
    }

    /// <summary>Remilitarizes every demilitarized state of a country (the decision).</summary>
    public int RemilitarizeAll(Country c)
    {
        var list = DemilitarizedOf(c);
        foreach (var s in list) Remilitarize(s, "decision");
        return list.Count;
    }

    /// <summary>Daily: a country at war is bound by no treaty - its demilitarized states are open again.</summary>
    private void CheckDemilitarizedAtWar()
    {
        if (!DemilEnabled) return;
        foreach (var s in DemilitarizedStates.ToList())
            if (s.Owner != null && _game.World.WarsOf(s.Owner).Any()) Remilitarize(s, "war");
    }

    /// <summary>
    /// How well the country would stand if it remilitarized: its strength (and its allies') against the strongest of the countries that
    /// border its demilitarized states and the ones that imposed them (and their allies). 1 = even, 2 = twice as strong.
    /// </summary>
    public double RemilitarizationOdds(Country c)
    {
        double Side(Country x) => Strength(x) + R.AlliesOf(x).Sum(a => Strength(a) * 0.6) + R.SubjectsOf(x).Sum(a => Strength(a) * 0.5);
        double mine = Side(c);
        double worst = 1;
        var opponents = new HashSet<Country>();
        foreach (var s in DemilitarizedOf(c))
        {
            if (ImposedBy(s) is { } by && by != c) opponents.Add(by);
            foreach (var n in _game.Population.NeighbourStates(s)) if (n.Owner is { } o && o != c && !R.AreAllied(o, c)) opponents.Add(o);
        }
        foreach (var o in opponents) worst = Math.Max(worst, Side(o));
        return mine / worst;
    }
}
