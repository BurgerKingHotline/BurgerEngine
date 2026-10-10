using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Politics;

/// <summary>
/// Politics (HOI4 style): ideologies with a popularity in every country, a ruling party, elections with parliament
/// seats and coalitions, ideology-based opinion between countries (politics feature); laws (laws feature);
/// ministers (cabinet feature); political movements and civil wars (civil_wars feature).
/// </summary>
public sealed partial class PoliticsSystem
{
    private readonly Game _game;
    public readonly List<IdeologyDef> Ideologies = new();
    public readonly Dictionary<string, IdeologyDef> IdeologyByKey = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, GovernmentDef> Governments = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<LawGroup> LawGroups = new();
    public readonly Dictionary<string, LawDef> Laws = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<CabinetPosition> Positions = new();
    public readonly List<Movement> Movements = new();
    public readonly List<CivilWar> CivilWars = new();
    private readonly Dictionary<Country, CountryPolitics> _state = new();

    /// <summary>Statistics since the game was loaded (not saved), shown by 'burger validate'.</summary>
    public int ElectionsHeld, GovernmentChanges, CivilWarsStarted, Revolutions, Coups, LawsChanged;
    private int _monthsTicked;

    public PoliticsSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "politics", Write = Save, Read = LoadSave, Reset = ResetRuntime });
        RegisterScript();
    }

    private PoliticsRules Rules => _game.Rules.Politics;

    /// <summary>The politics feature is installed and at least one ideology is defined.</summary>
    public bool Enabled => _game.Features.Has(FeatureSet.Politics) && Ideologies.Count > 0;
    public bool LawsEnabled => Enabled && _game.Features.Has(FeatureSet.Laws) && LawGroups.Count > 0;
    public bool CabinetEnabled => Enabled && _game.Features.Has(FeatureSet.Cabinet) && _game.Features.Has(FeatureSet.Characters) && Positions.Count > 0;
    public bool CivilWarsEnabled => Enabled && _game.Features.Has(FeatureSet.CivilWars);

    public IdeologyDef? Ideology(string? key) => key != null && IdeologyByKey.TryGetValue(key, out var i) ? i : null;

    // =============================================================================================
    // Definitions
    // =============================================================================================

    public void Load(GameData data)
    {
        Ideologies.Clear(); IdeologyByKey.Clear(); Governments.Clear(); LawGroups.Clear(); Laws.Clear(); Positions.Clear();
        _modifierKeys.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/ideologies"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var i = new IdeologyDef
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Elections = n.GetBool("elections"),
                    Extremist = n.GetBool("extremist"),
                    Government = n.GetString("government"),
                    Order = Ideologies.Count,
                    Definition = n,
                };
                i.PeacePuppets = n.Get("peace_puppets") != null ? n.GetBool("peace_puppets") : i.Elections;
                i.Adjective = n.GetString("adjective") ?? i.Name;
                i.PartyName = n.GetString("party_name") ?? i.Name;
                if (Rgb.TryParse(n.Get("color"), out var col)) i.Color = col;
                foreach (var k in n.GetList("coalition")) i.Coalition.Add(k);
                ReadNumbers(n.Get("opinion"), i.Opinion);
                ReadNumbers(n.Get("modifier"), i.Modifier);
                if (IdeologyByKey.ContainsKey(i.Key)) { Log.Error($"Ideology '{i.Key}' is defined twice", n.Location); continue; }
                Ideologies.Add(i);
                IdeologyByKey[i.Key] = i;
            }
        foreach (var i in Ideologies)
        {
            var key = i.Key;
            _game.Script.RegisterValue("popularity_" + key, (ctx, scope) => scope is Country c && Enabled ? PopularityOf(c, key) : null);
            _game.Script.RegisterValue("seats_" + key, (ctx, scope) => scope is Country c && Enabled ? Of(c).Seats.GetValueOrDefault(key) : null);
            foreach (var other in i.Coalition.Concat(i.Opinion.Keys))
                if (!IdeologyByKey.ContainsKey(other)) Log.Warn($"Ideology '{i.Key}' refers to unknown ideology '{other}'", i.Definition.Location);
        }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/governments"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var g = new GovernmentDef { Key = n.Key, Ruling = n.GetString("ruling_party") };
                ReadNumbers(n.Get("popularity"), g.Popularity);
                foreach (var l in n.Get("laws")?.All ?? Enumerable.Empty<Node>()) if (l.Value != null) g.Laws[l.Key] = l.Value;
                if (n.Has("seats")) g.Seats = n.GetInt("seats");
                if (g.Ruling != null && !IdeologyByKey.ContainsKey(g.Ruling)) Log.Error($"Government '{g.Key}': unknown ideology '{g.Ruling}'", n.Location);
                Governments[g.Key] = g;
            }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/laws"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var grp = new LawGroup { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Desc = n.GetString("desc", "")!, Order = LawGroups.Count, Potential = n.Get("potential") };
                foreach (var ln in n.All)
                {
                    if (ln.Children == null || ln.Key is "name" or "desc" or "potential") continue;
                    var law = new LawDef
                    {
                        Key = ln.Key,
                        Name = ln.GetString("name") ?? Localisation.GetOrPretty(ln.Key),
                        Desc = ln.GetString("desc", "")!,
                        Group = grp,
                        Cost = ln.GetDouble("cost", data.Defines.Get("law_cost", 100)),
                        Potential = ln.Get("potential"),
                        Allow = ln.Get("allow"),
                        AiWillDo = ln.Get("ai_will_do"),
                        OnEnact = ln.Get("on_enact"),
                        SuspendsElections = ln.GetBool("suspends_elections") || ln.GetBool("single_party"),
                        SingleParty = ln.GetBool("single_party"),
                        Definition = ln,
                    };
                    ReadNumbers(ln.Get("modifier"), law.Modifier);
                    foreach (var b in ln.GetList("bans")) law.Bans.Add(b);
                    if (Laws.ContainsKey(law.Key)) { Log.Error($"Law '{law.Key}' is defined twice (law keys must be unique across groups)", ln.Location); continue; }
                    grp.Laws.Add(law);
                    Laws[law.Key] = law;
                }
                if (grp.Laws.Count == 0) { Log.Warn($"Law group '{grp.Key}' has no laws", n.Location); continue; }
                grp.Default = n.GetString("default") ?? grp.Laws[0].Key;
                if (!grp.Laws.Any(l => l.Key == grp.Default)) { Log.Error($"Law group '{grp.Key}': default '{grp.Default}' is not one of its laws", n.Location); grp.Default = grp.Laws[0].Key; }
                LawGroups.Add(grp);
            }

        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/cabinet"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var p = new CabinetPosition
                {
                    Key = n.Key,
                    Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key),
                    Desc = n.GetString("desc", "")!,
                    Stat = n.GetString("stat", "")!,
                    Cost = n.GetDouble("cost", data.Defines.Get("minister_cost", 50)),
                    Order = Positions.Count,
                };
                ReadNumbers(n.Get("per_point"), p.PerPoint);
                ReadNumbers(n.Get("modifier"), p.Modifier);
                Positions.Add(p);
            }

        // modifier keys this system can provide (so Sum() skips the lookup for every other key)
        foreach (var i in Ideologies) foreach (var k in i.Modifier.Keys) _modifierKeys.Add(k);
        foreach (var l in Laws.Values) foreach (var k in l.Modifier.Keys) _modifierKeys.Add(k);
        foreach (var p in Positions) { foreach (var k in p.Modifier.Keys) _modifierKeys.Add(k); foreach (var k in p.PerPoint.Keys) _modifierKeys.Add(k); }
    }

    private static void ReadNumbers(Node? block, Dictionary<string, double> into)
    {
        if (block?.Children == null) return;
        foreach (var c in block.All)
            if (c.Value != null && Node.TryParseDouble(c.Value, out var v)) into[c.Key] = v;
    }

    // =============================================================================================
    // Country state
    // =============================================================================================

    /// <summary>The politics of a country (created with defaults on first use).</summary>
    public CountryPolitics Of(Country c)
    {
        if (_state.TryGetValue(c, out var p)) return p;
        p = new CountryPolitics();
        _state[c] = p;
        Setup(c, p, null);
        return p;
    }

    public bool HasState(Country c) => _state.ContainsKey(c);

    /// <summary>Starting politics: history/politics entry, else the country's government type, else 'default'.</summary>
    private void Setup(Country c, CountryPolitics p, Node? history)
    {
        if (Ideologies.Count == 0) return;
        var g = (c.Government.Length > 0 ? Governments.GetValueOrDefault(c.Government) : null) ?? Governments.GetValueOrDefault("default");
        p.Popularity.Clear();
        if (g != null) foreach (var kv in g.Popularity) if (IdeologyByKey.ContainsKey(kv.Key)) p.Popularity[kv.Key] = kv.Value;
        p.Ruling = g?.Ruling ?? "";
        p.TotalSeats = g?.Seats ?? (int)_game.Data.Defines.Get("parliament_seats", 100);
        foreach (var grp in LawGroups) p.Laws[grp.Key] = g != null && g.Laws.TryGetValue(grp.Key, out var l) && Laws.ContainsKey(l) ? l : grp.Default;
        if (history != null)
        {
            if (history.GetString("ruling_party") is { } r) p.Ruling = r;
            var pop = history.Get("popularity");
            if (pop != null) { p.Popularity.Clear(); ReadNumbers(pop, p.Popularity); }
            if (history.Has("seats")) p.TotalSeats = Math.Max(1, history.GetInt("seats"));
            foreach (var l in history.Get("laws")?.All ?? Enumerable.Empty<Node>())
            {
                if (l.Value == null) continue;
                if (Laws.TryGetValue(l.Value, out var law)) p.Laws[law.Group.Key] = law.Key;
                else Log.Error($"{c.Tag}: unknown law '{l.Value}'", l.Location);
            }
            foreach (var pn in history.Get("parties")?.All ?? Enumerable.Empty<Node>()) if (pn.Value != null) p.PartyNames[pn.Key] = pn.Value;
            if (GameDate.TryParse(history.GetString("last_election"), out var le)) p.LastElection = le;
        }
        if (!IdeologyByKey.ContainsKey(p.Ruling)) p.Ruling = p.Popularity.Count > 0 ? p.Popularity.MaxBy(kv => kv.Value).Key : Ideologies[0].Key;
        foreach (var i in Ideologies) if (!p.Popularity.ContainsKey(i.Key)) p.Popularity[i.Key] = 0;
        if (p.Popularity.Values.Sum() <= 0) p.Popularity[p.Ruling] = 1;
        if (p.Popularity[p.Ruling] <= 0) p.Popularity[p.Ruling] = 0.3;
        Normalize(p);
        p.Government = new List<string> { p.Ruling };
        // a first parliament: seats from popularity (no chance), the ruling party heads the government
        p.Seats = SeatsFrom(c, p, new Dictionary<string, double>(p.Popularity));
        if (HoldsElections(c, p))
        {
            var coalition = FormCoalition(c, p, p.Ruling);
            if (coalition != null) p.Government = coalition;
            int freq = ElectionMonths();
            var now = _game.World.Date;
            if (p.LastElection is { } last)
            {
                var next = last.AddMonths(freq);
                while (next <= now) next = next.AddMonths(freq);
                p.NextElection = next;
            }
            else p.NextElection = now.AddDays(30 + (c.Index * 97 + 13) % Math.Max(1, freq * 30 - 30)); // spread elections out
        }
        _modsDirty.Add(c);
    }

    /// <summary>Reads history/politics (called once the countries exist).</summary>
    public void LoadHistory()
    {
        _state.Clear();
        if (Ideologies.Count == 0) return;
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/politics"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var c = _game.World.GetCountry(n.Key);
                if (c == null) { Log.Error($"history/politics: unknown country '{n.Key}'", n.Location); continue; }
                var p = new CountryPolitics();
                _state[c] = p;
                Setup(c, p, n);
            }
    }

    private void ResetRuntime()
    {
        _state.Clear();
        Movements.Clear();
        CivilWars.Clear();
        _revolts.Clear();
        _modsDirty.Clear();
        _mods.Clear();
    }

    private static void Normalize(CountryPolitics p)
    {
        foreach (var k in p.Popularity.Keys.ToList()) if (p.Popularity[k] < 0 || double.IsNaN(p.Popularity[k])) p.Popularity[k] = 0;
        double sum = p.Popularity.Values.Sum();
        if (sum <= 0) return;
        foreach (var k in p.Popularity.Keys.ToList()) p.Popularity[k] /= sum;
    }

    public IdeologyDef? RulingIdeology(Country c) => Enabled ? Ideology(Of(c).Ruling) : null;

    public double PopularityOf(Country c, string ideology) => Of(c).Popularity.GetValueOrDefault(ideology);

    /// <summary>Adds popularity (a fraction, e.g. 0.05 = 5 points) to an ideology; the others shrink to keep 100%.</summary>
    public void AddPopularity(Country c, string ideology, double amount)
    {
        var p = Of(c);
        if (!p.Popularity.ContainsKey(ideology)) return;
        double cur = p.Popularity[ideology];
        double target = Math.Clamp(cur + amount, 0, 1);
        double others = 1 - cur;
        double scale = others > 1e-9 ? (1 - target) / others : 0;
        foreach (var k in p.Popularity.Keys.ToList()) p.Popularity[k] = k.Equals(ideology, StringComparison.OrdinalIgnoreCase) ? target : p.Popularity[k] * scale;
        if (others <= 1e-9 && target < 1) // everybody supported this ideology: spread the rest evenly
            foreach (var k in p.Popularity.Keys.ToList()) if (!k.Equals(ideology, StringComparison.OrdinalIgnoreCase)) p.Popularity[k] = (1 - target) / Math.Max(1, p.Popularity.Count - 1);
        Normalize(p);
    }

    public string PartyName(Country c, string ideology)
    {
        if (Of(c).PartyNames.TryGetValue(ideology, out var n)) return Localisation.Get(n);
        if (IsMinorityParty(ideology)) return MinorityPartyName(ideology);
        return Ideology(ideology)?.PartyName ?? ideology;
    }

    // ------------------------------------------------------------------------------------- minority parties
    // Parties of a people or a faith (population features): "culture:KEY" and "religion:KEY" seats in parliament.
    // They win seats but never govern; separatist seats speed up independence movements.

    public static bool IsMinorityParty(string key) => key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase) || key.StartsWith("religion:", StringComparison.OrdinalIgnoreCase);

    public string MinorityPartyName(string key)
    {
        string k = key[(key.IndexOf(':') + 1)..];
        return key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase)
            ? $"{_game.Population.Culture(k)?.Name ?? Localisation.GetOrPretty(k)} National Party"
            : $"{_game.Population.Religion(k)?.Name ?? Localisation.GetOrPretty(k)} Party";
    }

    /// <summary>Color of a party: its ideology, or the culture/religion of a minority party.</summary>
    public Rgb PartyColor(string key)
    {
        if (Ideology(key) is { } i) return i.Color;
        string k = key[(key.IndexOf(':') + 1)..];
        if (key.StartsWith("culture:", StringComparison.OrdinalIgnoreCase)) return _game.Population.Culture(k)?.Color ?? Rgb.Gray;
        if (key.StartsWith("religion:", StringComparison.OrdinalIgnoreCase)) return _game.Population.Religion(k)?.Color ?? Rgb.Gray;
        return Rgb.Gray;
    }

    /// <summary>Short label of a party for lists: ideology name, or the minority party's name.</summary>
    public string PartyLabel(string key) => Ideology(key)?.Name ?? (IsMinorityParty(key) ? MinorityPartyName(key) : key);

    /// <summary>Share of the seats a party holds (0-1).</summary>
    public double SeatShare(Country c, string key)
    {
        var p = Of(c);
        int total = p.Seats.Values.Sum();
        return total <= 0 ? 0 : p.Seats.GetValueOrDefault(key) / (double)total;
    }

    public bool IsBanned(Country c, string ideology) => IsBanned(c, Of(c), ideology);

    private bool IsBanned(Country c, CountryPolitics p, string ideology)
    {
        if (!LawsEnabled) return false;
        foreach (var kv in p.Laws)
            if (Laws.TryGetValue(kv.Value, out var l) && (l.Bans.Contains(ideology) || l.SingleParty && !ideology.Equals(p.Ruling, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    public bool InGovernment(Country c, string ideology) => Of(c).Government.Contains(ideology, StringComparer.OrdinalIgnoreCase);

    /// <summary>True if the country holds elections: its ruling ideology has elections and no law suspends them.</summary>
    public bool HoldsElections(Country c) => Enabled && HoldsElections(c, Of(c));

    private bool HoldsElections(Country c, CountryPolitics p)
    {
        if (Ideology(p.Ruling)?.Elections != true) return false;
        if (LawsEnabled)
            foreach (var kv in p.Laws)
                if (Laws.TryGetValue(kv.Value, out var l) && l.SuspendsElections) return false;
        return true;
    }

    private int ElectionMonths() => Math.Max(1, (int)_game.Data.Defines.Get("election_frequency_months", 48));

    // =============================================================================================
    // Elections and governments
    // =============================================================================================

    /// <summary>Seats per ideology by the D'Hondt method (banned parties and parties under the threshold get none).</summary>
    private Dictionary<string, int> SeatsFrom(Country c, CountryPolitics p, Dictionary<string, double> votes)
    {
        double threshold = _game.Data.Defines.Get("election_threshold", 0.05);
        var eligible = votes.Where(kv => kv.Value >= threshold && !IsBanned(c, p, kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        var seats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in eligible.Keys) seats[k] = 0;
        if (eligible.Count == 0) { seats[p.Ruling] = p.TotalSeats; return seats; }
        for (int s = 0; s < p.TotalSeats; s++)
        {
            var best = eligible.MaxBy(kv => kv.Value / (seats[kv.Key] + 1)).Key;
            seats[best]++;
        }
        return seats;
    }

    /// <summary>Parties that govern together with <paramref name="leader"/> for a majority (null if no majority is possible).</summary>
    private List<string>? FormCoalition(Country c, CountryPolitics p, string leader)
    {
        var li = Ideology(leader);
        if (li == null) return null;
        int total = Math.Max(1, p.Seats.Values.Sum());
        var gov = new List<string> { leader };
        int seats = p.Seats.GetValueOrDefault(leader);
        foreach (var kv in p.Seats.Where(kv => kv.Key != leader && kv.Value > 0).OrderByDescending(kv => kv.Value))
        {
            if (seats * 2 > total) break;
            var other = Ideology(kv.Key);
            if (other == null || !gov.All(g => Ideology(g) is { } gi && Rules.WillGovernWith(_game, c, gi, other))) continue;
            gov.Add(kv.Key);
            seats += kv.Value;
        }
        return seats * 2 > total ? gov : null;
    }

    /// <summary>Holds an election now: new seats, then the biggest party that can find a majority governs.</summary>
    public void HoldElection(Country c)
    {
        if (!Enabled) return;
        var p = Of(c);
        var votes = Rules.Votes(_game, c);
        // parties of minorities and faiths take their voters' share; the ideologies share the rest
        var minority = _game.Population.Enabled ? Rules.MinorityVotes(_game, c) : new Dictionary<string, double>();
        double minShare = Math.Min(0.6, minority.Values.Sum());
        if (minShare > 0)
        {
            foreach (var k in votes.Keys.ToList()) votes[k] *= 1 - minShare;
            double scale = minShare / Math.Max(1e-9, minority.Values.Sum());
            foreach (var kv in minority) votes[kv.Key] = kv.Value * scale;
        }
        p.Seats = SeatsFrom(c, p, votes);
        p.LastElection = _game.World.Date;
        p.NextElection = _game.World.Date.AddMonths(ElectionMonths());
        ElectionsHeld++;
        List<string>? gov = null;
        foreach (var leader in p.Seats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
        {
            gov = FormCoalition(c, p, leader);
            if (gov != null) break;
        }
        // no majority: the biggest party governs alone (minority government)
        gov ??= new List<string> { p.Seats.Where(kv => kv.Value > 0 && IdeologyByKey.ContainsKey(kv.Key)).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? p.Ruling };
        string before = p.Ruling;
        SetGovernment(c, gov, "election");
        if (c.IsPlayer || before != p.Ruling)
        {
            string result = string.Join(", ", p.Seats.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{PartyName(c, kv.Key)} {kv.Value}"));
            string text = $"Election in {c.Name}: {result}. " + (gov.Count > 1 ? $"Coalition of {string.Join(" and ", gov.Select(g => PartyName(c, g)))}." : $"{PartyName(c, gov[0])} governs.");
            if (c.IsPlayer) _game.Notify(text, NotificationKind.Diplomacy);
        }
        _game.Events.FireOnAction("on_election", c);
    }

    /// <summary>Puts an ideology's party in power alone (coups, revolutions, scripts).</summary>
    public void SetRulingParty(Country c, string ideology, string reason = "")
    {
        if (!IdeologyByKey.ContainsKey(ideology)) return;
        SetGovernment(c, new List<string> { ideology }, reason);
        var p = Of(c);
        if (HoldsElections(c, p) && p.NextElection == null) p.NextElection = _game.World.Date.AddMonths(ElectionMonths());
    }

    private void SetGovernment(Country c, List<string> gov, string reason)
    {
        var p = Of(c);
        string old = p.Ruling;
        p.Government = gov.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.Ruling = p.Government[0];
        _modsDirty.Add(c);
        if (old == p.Ruling) return;
        GovernmentChanges++;
        var ideology = Ideology(p.Ruling)!;
        if (ideology.Government != null) c.Government = ideology.Government;
        if (!HoldsElections(c, p)) p.NextElection = null;
        else p.NextElection ??= _game.World.Date.AddMonths(ElectionMonths());
        // ministers of parties that left the government step down
        foreach (var kv in p.Ministers.ToList())
            if (!p.Government.Contains(MinisterParty(kv.Value) ?? "", StringComparer.OrdinalIgnoreCase)) p.Ministers.Remove(kv.Key);
        if (_game.World.Player != null)
        {
            string how = reason switch { "election" => "after an election", "revolution" => "after a revolution", "coup" => "in a coup", "overlord" => "backed by its overlord", _ => "" };
            _game.Notify($"{c.Name}: the {PartyName(c, p.Ruling)} ({ideology.Name}) took power {how}.".Replace("  ", " ").Replace(" .", "."), c.IsPlayer ? NotificationKind.Diplomacy : NotificationKind.Info);
        }
        _game.Events.FireOnAction("on_ruling_party_changed", c);
        _game.RaiseMapChanged(); // ideology map mode colors
    }

    // =============================================================================================
    // Effects on the rest of the game
    // =============================================================================================

    /// <summary>Opinion <paramref name="from"/> has of <paramref name="to"/> because of their ruling ideologies.</summary>
    public double IdeologyOpinion(Country from, Country to)
    {
        if (!Enabled || from == to) return 0;
        var a = Ideology(Of(from).Ruling);
        if (a == null) return 0;
        return a.Opinion.GetValueOrDefault(Of(to).Ruling);
    }

    // modifier values from the ruling ideology, laws and ministers, cached per country
    private readonly HashSet<string> _modifierKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, Dictionary<string, double>> _mods = new();
    private readonly HashSet<Country> _modsDirty = new();

    /// <summary>Modifier total this system adds to a scope (used by <see cref="Mechanics.ModifierSystem.Sum"/>).</summary>
    public double ModifierValue(IScope scope, string key)
    {
        if (scope is not Country c || !_modifierKeys.Contains(key) || !Enabled) return 0;
        if (_modsDirty.Remove(c) || !_mods.TryGetValue(c, out var m)) _mods[c] = m = ComputeModifiers(c);
        return m.GetValueOrDefault(key);
    }

    /// <summary>Where a country's political modifiers come from (for tooltips): (source, key, value).</summary>
    public List<(string Source, string Key, double Value)> ModifierSources(Country c)
    {
        var list = new List<(string, string, double)>();
        var p = Of(c);
        if (Ideology(p.Ruling) is { } i) foreach (var kv in i.Modifier) list.Add(($"{i.Name} government", kv.Key, kv.Value));
        if (LawsEnabled)
            foreach (var kv in p.Laws)
                if (Laws.TryGetValue(kv.Value, out var l)) foreach (var m in l.Modifier) list.Add((l.Name, m.Key, m.Value));
        if (CabinetEnabled)
            foreach (var pos in Positions)
            {
                if (!p.Ministers.TryGetValue(pos.Key, out var ch) || !ch.Alive) continue;
                foreach (var m in pos.Modifier) list.Add((pos.Name, m.Key, m.Value));
                double stat = pos.Stat.Length > 0 ? ch.GetStat(pos.Stat) : 0;
                foreach (var m in pos.PerPoint) list.Add(($"{pos.Name} ({ch.DisplayName})", m.Key, m.Value * stat));
            }
        return list;
    }

    private Dictionary<string, double> ComputeModifiers(Country c)
    {
        var m = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, key, value) in ModifierSources(c)) m[key] = m.GetValueOrDefault(key) + value;
        return m;
    }

    /// <summary>Call after something changes a country's laws, ministers or government.</summary>
    public void Invalidate(Country c) => _modsDirty.Add(c);

    // =============================================================================================
    // Ticks
    // =============================================================================================

    public void DailyTick()
    {
        if (!Enabled) return;
        var today = _game.World.Date;
        foreach (var c in _game.World.Countries.ToList()) // scripts may add countries (start_civil_war)
        {
            if (!c.Exists) continue;
            var p = Of(c);
            if (p.NextElection is { } next && HoldsElections(c, p))
            {
                if (next <= today) HoldElection(c);
                else if (next.TotalDays - today.TotalDays == (int)_game.Data.Defines.Get("election_campaign_days", 60))
                    _game.Events.FireOnAction("on_election_campaign", c); // campaign events (politics feature)
            }
        }
        if (CivilWarsEnabled) CheckCivilWars();
    }

    public void MonthlyTick()
    {
        if (!Enabled) return;
        if (!_aiCalled && _monthsTicked > 0 && _game.Features.Has(FeatureSet.Ai) && _game.Ai.Enabled)
            foreach (var ac in _game.World.Countries.Where(x => x.Exists && !x.IsPlayer).ToList()) AiMonthly(ac);
        _aiCalled = false;
        _monthsTicked++;
        foreach (var c in _game.World.Countries.ToList()) // scripts may add countries (start_civil_war)
        {
            if (!c.Exists) continue;
            var p = Of(c);
            var drift = Rules.MonthlyDrift(_game, c);
            foreach (var kv in drift)
                if (p.Popularity.ContainsKey(kv.Key)) p.Popularity[kv.Key] = Math.Max(0, p.Popularity[kv.Key] + kv.Value / 100.0);
            Normalize(p);
            // ministers who died or left the country leave the cabinet
            foreach (var kv in p.Ministers.ToList())
                if (!kv.Value.Alive || kv.Value.Country != c) { p.Ministers.Remove(kv.Key); _modsDirty.Add(c); }
        }
        if (CivilWarsEnabled) MonthlyUnrest();
    }

    // =============================================================================================
    // Save
    // =============================================================================================

    private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var (c, p) in _state)
        {
            if (!c.Exists && !c.Dynamic) continue;
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            n.Add("ruling_party", p.Ruling);
            var gov = n.AddBlock("government");
            foreach (var g in p.Government) gov.AddItem(g);
            var pop = n.AddBlock("popularity");
            foreach (var kv in p.Popularity) pop.Add(kv.Key, F(kv.Value));
            var seats = n.AddBlock("seats");
            foreach (var kv in p.Seats) seats.Add(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            n.Add("total_seats", p.TotalSeats.ToString(CultureInfo.InvariantCulture));
            if (p.LastElection is { } le) n.Add("last_election", le.ToScript());
            if (p.NextElection is { } ne) n.Add("next_election", ne.ToScript());
            if (p.CalmUntilDay != int.MinValue) n.Add("calm_until", p.CalmUntilDay.ToString(CultureInfo.InvariantCulture));
            var laws = n.AddBlock("laws");
            foreach (var kv in p.Laws) laws.Add(kv.Key, kv.Value);
            var changed = n.AddBlock("law_changed");
            foreach (var kv in p.LawChangedDay) changed.Add(kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
            var min = n.AddBlock("ministers");
            foreach (var kv in p.Ministers) min.Add(kv.Key, kv.Value.Key);
            var parties = n.AddBlock("parties");
            foreach (var kv in p.PartyNames) parties.Add(kv.Key, kv.Value, true);
        }
        foreach (var m in Movements)
        {
            var n = root.AddBlock("movement");
            n.Add("country", m.Country.Tag);
            n.Add("ideology", m.Ideology);
            n.Add("demand", m.Demand.ToString().ToLowerInvariant());
            if (m.TargetLaw != null) n.Add("law", m.TargetLaw);
            n.Add("progress", F(m.Progress));
            n.Add("started", m.Started.ToScript());
        }
        var rv = root.AddBlock("revolts");
        foreach (var kv in _revolts) rv.Add(kv.Key.Tag, kv.Value.ToString(CultureInfo.InvariantCulture));
        foreach (var cw in CivilWars)
        {
            var n = root.AddBlock("civil_war");
            n.Add("war", cw.War.Id.ToString(CultureInfo.InvariantCulture));
            n.Add("origin", cw.Origin.Tag);
            n.Add("rebel", cw.Rebel.Tag);
            n.Add("ideology", cw.Ideology);
        }
    }

    private void LoadSave(Node root)
    {
        var w = _game.World;
        foreach (var n in root.GetAll("country"))
        {
            var c = w.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            var p = new CountryPolitics { Ruling = n.GetString("ruling_party", "")!, TotalSeats = Math.Max(1, n.GetInt("total_seats", 100)) };
            p.Government.AddRange(n.GetList("government").Where(IdeologyByKey.ContainsKey));
            if (!IdeologyByKey.ContainsKey(p.Ruling)) p.Ruling = Ideologies.FirstOrDefault()?.Key ?? "";
            if (p.Government.Count == 0 || p.Government[0] != p.Ruling) { p.Government.Remove(p.Ruling); p.Government.Insert(0, p.Ruling); }
            ReadNumbers(n.Get("popularity"), p.Popularity);
            foreach (var i in Ideologies) if (!p.Popularity.ContainsKey(i.Key)) p.Popularity[i.Key] = 0;
            foreach (var k in p.Popularity.Keys.Where(k => !IdeologyByKey.ContainsKey(k)).ToList()) p.Popularity.Remove(k);
            Normalize(p);
            foreach (var s in n.Get("seats")?.All ?? Enumerable.Empty<Node>()) if (IdeologyByKey.ContainsKey(s.Key) || IsMinorityParty(s.Key)) p.Seats[s.Key] = (int)s.AsDouble();
            if (GameDate.TryParse(n.GetString("last_election"), out var le)) p.LastElection = le;
            if (GameDate.TryParse(n.GetString("next_election"), out var ne)) p.NextElection = ne;
            if (n.Get("calm_until") != null) p.CalmUntilDay = n.GetInt("calm_until", int.MinValue);
            foreach (var grp in LawGroups) p.Laws[grp.Key] = grp.Default;
            foreach (var l in n.Get("laws")?.All ?? Enumerable.Empty<Node>())
                if (l.Value != null && Laws.TryGetValue(l.Value, out var law)) p.Laws[law.Group.Key] = law.Key;
            foreach (var l in n.Get("law_changed")?.All ?? Enumerable.Empty<Node>()) p.LawChangedDay[l.Key] = (int)l.AsDouble();
            foreach (var m in n.Get("ministers")?.All ?? Enumerable.Empty<Node>())
                if (w.GetCharacter(m.Value) is { } ch) p.Ministers[m.Key] = ch;
            foreach (var pn in n.Get("parties")?.All ?? Enumerable.Empty<Node>()) if (pn.Value != null) p.PartyNames[pn.Key] = pn.Value;
            _state[c] = p;
            _modsDirty.Add(c);
        }
        foreach (var n in root.GetAll("movement"))
        {
            var c = w.GetCountry(n.GetString("country"));
            string ideology = n.GetString("ideology", "")!;
            if (c == null || !IdeologyByKey.ContainsKey(ideology)) continue;
            Movements.Add(new Movement
            {
                Country = c,
                Ideology = ideology,
                Demand = string.Equals(n.GetString("demand"), "legalize", StringComparison.OrdinalIgnoreCase) ? MovementDemand.Legalize : MovementDemand.Power,
                TargetLaw = n.GetString("law"),
                Progress = n.GetDouble("progress"),
                Started = GameDate.Parse(n.GetString("started") ?? "", w.Date),
            });
        }
        foreach (var rv in root.Get("revolts")?.All ?? Enumerable.Empty<Node>())
            if (w.GetCountry(rv.Key) is { } rc) _revolts[rc] = (int)rv.AsDouble();
        foreach (var n in root.GetAll("civil_war"))
        {
            int id = n.GetInt("war", -1);
            var war = w.Wars.FirstOrDefault(x => x.Id == id);
            var o = w.GetCountry(n.GetString("origin"));
            var r = w.GetCountry(n.GetString("rebel"));
            if (war == null || o == null || r == null) continue;
            CivilWars.Add(new CivilWar { War = war, Origin = o, Rebel = r, Ideology = n.GetString("ideology", "")! });
        }
    }
}
