using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Population;

/// <summary>People of a culture the country does not accept, working towards breaking away (independence feature).</summary>
public sealed class IndependenceMovement
{
    public Country Country = null!;
    public string Culture = "";
    /// <summary>0-100. At 100 the movement declares independence.</summary>
    public double Progress;
    public GameDate Started;
    /// <summary>How often it was suppressed (each time costs more).</summary>
    public int Suppressed;
}

/// <summary>A running war of independence.</summary>
public sealed class IndependenceWar
{
    public War War = null!;
    public Country Origin = null!;
    public Country Rebel = null!;
    public string Culture = "";
    public int StartDay;
}

public sealed partial class PopulationSystem
{
    // =============================================================================================
    // Independence movements (independence feature)
    // =============================================================================================

    public readonly List<IndependenceMovement> Independence = new();
    public readonly List<IndependenceWar> IndependenceWars = new();
    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public int IndependenceDeclared, IndependenceWon, IndependenceCrushed;
    /// <summary>Countries freed from their rulers in this game (independence won or granted; saved). Each one raises decolonisation pressure.</summary>
    public int FreedTotal;

    /// <summary>
    /// Decolonisation pressure (1 = none yet): grows with every country that won or was granted independence (decolonisation_step,
    /// 0.05 each, up to max_decolonisation_pressure 2). Speeds up independence movements and sharpens the world's dislike of colonial empires.
    /// </summary>
    public double DecolonisationPressure => Math.Min(_game.Data.Defines.Get("max_decolonisation_pressure", 2), 1 + FreedTotal * _game.Data.Defines.Get("decolonisation_step", 0.05));

    private readonly Dictionary<Country, int> _colonyCount = new();
    private int _colonyCountDay = -1;

    /// <summary>Number of colonies (see <see cref="IsColony"/>) a country holds (recounted monthly).</summary>
    public int ColonyCount(Country c)
    {
        int today = _game.World.Date.TotalDays;
        if (_colonyCountDay < 0 || today - _colonyCountDay >= 30)
        {
            _colonyCount.Clear();
            foreach (var s in _game.World.States)
                if (s.Owner != null && IsColony(s)) _colonyCount[s.Owner] = _colonyCount.GetValueOrDefault(s.Owner) + 1;
            _colonyCountDay = today;
        }
        return _colonyCount.GetValueOrDefault(c);
    }

    /// <summary>Opinion <paramref name="from"/> has of <paramref name="to"/> because <paramref name="to"/> holds colonies (countries with many colonies themselves do not mind).</summary>
    public double ColonialOpinion(Country from, Country to)
    {
        if (!IndependenceEnabled || from == to) return 0;
        int colonies = ColonyCount(to);
        if (colonies == 0 || ColonyCount(from) >= _game.Data.Defines.Get("colonial_power_colonies", 5)) return 0;
        var d = _game.Data.Defines;
        return -Math.Min(d.Get("max_colonial_opinion", 25), colonies * d.Get("colonial_opinion_per_colony", 0.5)) * DecolonisationPressure;
    }

    public IEnumerable<IndependenceMovement> IndependenceOf(Country c) => Independence.Where(m => m.Country == c);

    public bool InIndependenceWar(Country c) => IndependenceWars.Any(w => w.Origin == c || w.Rebel == c);

    /// <summary>States of <paramref name="c"/> where <paramref name="culture"/> is the biggest culture and is not accepted (never the capital state).</summary>
    public List<State> SeparatistStates(Country c, string culture)
    {
        var result = new List<State>();
        if (IsAccepted(c, culture)) return result;
        foreach (var s in c.OwnedStates)
            if (s != c.Capital?.State && MajorityCulture(s) == culture) result.Add(s);
        return result;
    }

    private void MonthlyIndependence()
    {
        CheckIndependenceWars();
        var w = _game.World;
        double minShare = _game.Data.Defines.Get("independence_min_share", 0.02);
        foreach (var c in w.Countries.ToList())
        {
            if (!c.Exists || Of(c).Culture == null || InIndependenceWar(c) || _game.Politics.InCivilWar(c)) continue;
            double total = TotalOf(c);
            if (total <= 0) continue;
            // cultures that are the majority somewhere in the country without being accepted
            var cultures = new Dictionary<string, List<State>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in c.OwnedStates)
            {
                if (s == c.Capital?.State) continue;
                string? major = MajorityCulture(s);
                if (major == null || IsAccepted(c, major)) continue;
                if (!cultures.TryGetValue(major, out var list)) cultures[major] = list = new List<State>();
                list.Add(s);
            }
            // movements whose people are gone or were accepted dissolve
            Independence.RemoveAll(m => m.Country == c && !cultures.ContainsKey(m.Culture));
            bool declared = false;
            foreach (var (culture, states) in cultures)
            {
                double people = states.Sum(s => Of(s).Pops.Where(p => p.Culture == culture).Sum(p => p.Size));
                double share = people / total;
                var m = Independence.FirstOrDefault(x => x.Country == c && x.Culture == culture);
                if (m == null)
                {
                    if (share < minShare) continue;
                    m = new IndependenceMovement { Country = c, Culture = culture, Started = w.Date };
                    Independence.Add(m);
                    if (c.IsPlayer) _game.Notify($"The {Culture(culture)?.Name ?? culture} people want independence.", NotificationKind.Bad);
                    _game.Events.FireOnAction("on_independence_movement", c);
                }
                bool colonial = states.All(IsColony);
                double progress = share < minShare / 2 ? -2 : Rules.IndependenceProgress(_game, c, culture, share, colonial);
                m.Progress = Math.Clamp(m.Progress + progress, 0, 100);
                if (m.Progress <= 0 && share < minShare) { Independence.Remove(m); continue; }
                if (m.Progress >= 100 && !declared)
                {
                    Independence.Remove(m);
                    DeclareIndependence(c, culture, states);
                    declared = true; // one a month per country
                }
            }
        }
    }

    /// <summary>Cost of suppressing: suppress_independence_cost (50), half again more for every earlier suppression.</summary>
    public double SuppressIndependenceCost(IndependenceMovement m) => _game.Data.Defines.Get("suppress_independence_cost", 50) * (1 + 0.5 * m.Suppressed);

    /// <summary>Political power with politics, gold with the economy.</summary>
    private string IndependenceCurrency => _game.Politics.PowerResource?.Name ?? (_game.Features.Has(FeatureSet.Economy) ? "gold" : "");

    public string? CannotSuppressIndependence(IndependenceMovement m)
    {
        double cost = SuppressIndependenceCost(m);
        if (IndependenceCurrency.Length == 0 || cost <= 0) return null;
        double have = IndependenceCurrency == "gold" ? m.Country.Gold : _game.Politics.PowerOf(m.Country);
        return have < cost ? $"Needs {cost:0} {IndependenceCurrency}" : null;
    }

    /// <summary>Sets an independence movement back (costs political power or gold, and some stability with politics).</summary>
    public string? SuppressIndependence(IndependenceMovement m)
    {
        var reason = CannotSuppressIndependence(m);
        if (reason != null) return reason;
        double cost = SuppressIndependenceCost(m);
        m.Suppressed++;
        if (IndependenceCurrency == "gold") m.Country.Gold -= cost;
        else if (_game.Politics.PowerResource is { } pr) _game.Resources.Set(m.Country, pr, _game.Resources.Get(m.Country, pr) - cost);
        m.Progress = Math.Max(0, m.Progress - _game.Data.Defines.Get("suppress_independence_progress", 20));
        var stab = _game.Resources.Defs.FirstOrDefault(r => string.Equals(r.Key, "stability", StringComparison.OrdinalIgnoreCase));
        if (stab != null) _game.Resources.Set(m.Country, stab, _game.Resources.Get(m.Country, stab) - _game.Data.Defines.Get("suppress_independence_stability", 3));
        return null;
    }

    /// <summary>Frees the states of <paramref name="owner"/> where <paramref name="culture"/> is the majority as a country of that people (peace term "release a people").</summary>
    public Country? ReleaseCulture(Country owner, string culture)
    {
        if (!Enabled) return null;
        var states = owner.OwnedStates.Where(s => s != owner.Capital?.State && MajorityCulture(s) == culture).ToList();
        if (states.Count == 0) return null;
        Independence.RemoveAll(m => m.Country == owner && m.Culture == culture);
        var free = CreateIndependentCountry(owner, culture, states);
        FreedTotal++;
        _game.Notify($"{free.Name} was freed from {owner.Name}.", owner.IsPlayer ? NotificationKind.Bad : NotificationKind.Info);
        _game.RaiseMapChanged();
        return free;
    }

    /// <summary>Lets the movement's states go in peace: they become an independent country.</summary>
    public Country? GrantIndependence(IndependenceMovement m)
    {
        Independence.Remove(m);
        var states = SeparatistStates(m.Country, m.Culture);
        if (states.Count == 0) return null;
        var free = CreateIndependentCountry(m.Country, m.Culture, states);
        FreedTotal++;
        // the world approves of letting a people go
        var d = _game.Data.Defines;
        foreach (var other in _game.World.Countries.Where(x => x.Exists && x != m.Country && x != free))
            _game.Diplomacy.AddOpinion(other, m.Country, "Granted independence", d.Get("grant_independence_opinion", 15), 0.5);
        _game.Diplomacy.AddOpinion(free, m.Country, "Granted us independence", d.Get("granted_independence_opinion", 50), 0.5);
        _game.Notify($"{m.Country.Name} granted independence to {free.Name}.", m.Country.IsPlayer ? NotificationKind.Diplomacy : NotificationKind.Info);
        _game.Events.FireOnAction("on_independence", free, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["origin"] = m.Country });
        _game.RaiseMapChanged();
        return free;
    }

    /// <summary>The movement breaks away: a new country (or a dead one of that culture, revived) takes its states; with armies and wars it must fight for it.</summary>
    public Country? DeclareIndependence(Country origin, string culture, List<State>? states = null)
    {
        states ??= SeparatistStates(origin, culture);
        if (states.Count == 0) return null;
        var rebel = CreateIndependentCountry(origin, culture, states);
        IndependenceDeclared++;
        bool war = _game.Features.Has(FeatureSet.Military) && _game.Features.Has(FeatureSet.Diplomacy);
        if (!war)
        {
            IndependenceWon++;
            FreedTotal++;
            _game.Notify($"{rebel.Name} broke away from {origin.Name}.", origin.IsPlayer ? NotificationKind.Bad : NotificationKind.Info);
            _game.Events.FireOnAction("on_independence", rebel, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["origin"] = origin });
            _game.RaiseMapChanged();
            return rebel;
        }
        // garrisons in the land partly go over; a militia rises from the people
        var land = states.SelectMany(s => s.Locations).Where(l => l.IsLand && l.IsPassable).ToList();
        var landSet = land.ToHashSet();
        double defect = _game.Data.Defines.Get("independence_garrison_defect", 0.5);
        foreach (var d in _game.World.Divisions.Where(d => d.Owner == origin && d.VolunteerFrom == null && landSet.Contains(d.Location)).ToList())
            if (_game.Rng.NextDouble() < defect)
            {
                _game.Warfare.StopDivision(d);
                _game.Warfare.AssignToArmy(d, null);
                d.FrontPost = null;
                d.Owner = rebel;
            }
        var template = _game.Data.Templates.Values.FirstOrDefault(t => t.Key.Contains("militia", StringComparison.OrdinalIgnoreCase))
                       ?? _game.Data.Templates.Values.FirstOrDefault(t => t.Key.Contains("infantry", StringComparison.OrdinalIgnoreCase))
                       ?? _game.Data.Templates.Values.FirstOrDefault();
        if (template != null && land.Count > 0)
        {
            double people = states.Sum(TotalOf);
            int count = Math.Clamp((int)(people / _game.Data.Defines.Get("independence_people_per_division", 1500000)), Math.Min(states.Count, 12), 24);
            for (int i = 0; i < count; i++) _game.Warfare.CreateDivision(rebel, template, land[i * 7919 % land.Count]);
        }
        var w = _game.Diplomacy.DeclareWar(rebel, origin, null, null, force: true);
        if (w == null)
        {
            Log.Warn($"Independence of {rebel.Tag} from {origin.Tag}: the war could not start");
            return rebel;
        }
        w.Name = $"{rebel.Name} War of Independence";
        w.Vars.SetFlag("independence_war");
        IndependenceWars.Add(new IndependenceWar { War = w, Origin = origin, Rebel = rebel, Culture = culture, StartDay = _game.World.Date.TotalDays });
        _game.Notify($"{rebel.Name} declared independence from {origin.Name}!", origin.IsPlayer ? NotificationKind.War : NotificationKind.Info);
        _game.Events.FireOnAction("on_independence_declared", rebel, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["origin"] = origin });
        _game.RaiseMapChanged();
        return rebel;
    }

    private Country CreateIndependentCountry(Country origin, string culture, List<State> states)
    {
        var w = _game.World;
        var def = Culture(culture);
        // a country of that culture that no longer exists comes back, otherwise a new one forms
        var country = w.Countries.FirstOrDefault(x => !x.Exists && _countries.TryGetValue(x, out var xp) && string.Equals(xp.Culture, culture, StringComparison.OrdinalIgnoreCase));
        if (country == null)
        {
            string tag = FreeIndependenceTag();
            string name = def?.CountryName ?? $"{def?.Name ?? Localisation.GetOrPretty(culture)} Republic";
            // a second country of the same people is named after its land
            if (w.Countries.Any(x => x.Exists && x.Name == name)) name = $"{name} of {states.MaxBy(TotalOf)!.DisplayName}";
            country = w.CreateCountry(tag, name, def?.Color ?? Rgb.Gray, origin.FlagPath, origin.Government);
            country.Playable = false;
            var cp = new CountryPopulation { Culture = culture, Religion = MajorityReligionOf(states) ?? def?.Religion };
            _countries[country] = cp;
        }
        foreach (var s in states)
        {
            _game.Diplomacy.TransferState(s, country);
            s.Cores.Add(country);
        }
        var capState = states.MaxBy(TotalOf)!;
        country.Capital = capState.Locations.Where(l => l.IsLand && l.IsPassable).OrderBy(l => l.Key).FirstOrDefault() ?? capState.Locations[0];
        // politics: the most popular ideology of the old country leads the new one
        if (_game.Politics.Enabled)
        {
            var op = _game.Politics.Of(origin);
            var np = _game.Politics.Of(country);
            foreach (var kv in op.Popularity) np.Popularity[kv.Key] = kv.Value;
            string lead = np.Popularity.Count > 0 ? np.Popularity.MaxBy(kv => kv.Value).Key : np.Ruling;
            _game.Politics.SetRulingParty(country, lead, "independence");
        }
        _home.Remove(origin);
        _countryModsDirty = true;
        return country;
    }

    private string? MajorityReligionOf(List<State> states)
    {
        if (!ReligionEnabled) return null;
        return Breakdown(states, p => p.Religion).FirstOrDefault().Key;
    }

    private string FreeIndependenceTag()
    {
        for (char a = 'I'; a <= 'Q'; a++)
            for (int i = 0; i < 100; i++)
            {
                string t = $"{a}{i:00}";
                if (_game.World.GetCountry(t) == null) return t;
            }
        for (int i = 0; i < 46656; i++) { string t = $"Y{i % 36:X1}{i / 36 % 36:X1}"; if (_game.World.GetCountry(t) == null) return t; }
        return "YYY";
    }

    /// <summary>Ends wars of independence: crushed rebels are annexed; rebels who still hold most of their land after independence_war_months (24) are free.</summary>
    private void CheckIndependenceWars()
    {
        int today = _game.World.Date.TotalDays;
        foreach (var iw in IndependenceWars.ToList())
        {
            if (!_game.World.Wars.Contains(iw.War) || !iw.Origin.Exists || !iw.Rebel.Exists)
            {
                IndependenceWars.Remove(iw);
                continue;
            }
            double held = HeldShare(iw.Rebel);
            bool capitalLost = iw.Rebel.Capital == null || iw.Rebel.Capital.Controller != iw.Rebel;
            if (held <= 0.05 || (capitalLost && held <= 0.3))
            {
                IndependenceWars.Remove(iw);
                _game.Diplomacy.EndWar(iw.War);
                _game.Diplomacy.Annex(iw.Origin, iw.Rebel);
                IndependenceCrushed++;
                _game.Notify($"{iw.Origin.Name} crushed the {iw.Rebel.Name} independence movement.", iw.Origin.IsPlayer ? NotificationKind.Good : NotificationKind.Info);
                _game.RaiseMapChanged();
                continue;
            }
            double months = (today - iw.StartDay) / 30.4;
            if (months >= _game.Data.Defines.Get("independence_war_months", 24) && held >= 0.5 || HeldShare(iw.Origin) <= 0.3)
            {
                IndependenceWars.Remove(iw);
                _game.Diplomacy.EndWar(iw.War);
                IndependenceWon++;
                FreedTotal++;
                _game.Notify($"{iw.Rebel.Name} won its independence from {iw.Origin.Name}.", iw.Origin.IsPlayer ? NotificationKind.Bad : NotificationKind.Info);
                _game.Events.FireOnAction("on_independence", iw.Rebel, new Dictionary<string, IScope?>(StringComparer.OrdinalIgnoreCase) { ["origin"] = iw.Origin });
                _game.RaiseMapChanged();
            }
        }
    }

    private static double HeldShare(Country c)
    {
        int n = 0, held = 0;
        foreach (var s in c.OwnedStates)
            foreach (var l in s.Locations)
                if (l.IsLand) { n++; if (l.Controller == c) held++; }
        return n == 0 ? 0 : held / (double)n;
    }

    /// <summary>AI: suppress movements that get close, let small colonies go when it cannot.</summary>
    private void AiIndependence(Country c)
    {
        if (!IndependenceEnabled) return;
        foreach (var m in IndependenceOf(c).ToList())
        {
            if (m.Progress < 70) continue;
            if (CannotSuppressIndependence(m) == null && _game.Rng.NextDouble() < 0.35) SuppressIndependence(m);
            else if (m.Progress > 90 && _game.Rng.NextDouble() < 0.1 && SeparatistStates(c, m.Culture).All(IsColony)) GrantIndependence(m);
        }
    }

    // ------------------------------------------------------------------------------------- save
    private void SaveIndependence(Node root)
    {
        if (FreedTotal > 0) root.Add("freed_total", FreedTotal.ToString(CultureInfo.InvariantCulture));
        foreach (var m in Independence)
        {
            var n = root.AddBlock("independence");
            n.Add("country", m.Country.Tag);
            n.Add("culture", m.Culture);
            n.Add("progress", F(m.Progress));
            n.Add("started", m.Started.ToScript());
            if (m.Suppressed > 0) n.Add("suppressed", m.Suppressed.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var iw in IndependenceWars)
        {
            var n = root.AddBlock("independence_war");
            n.Add("war", iw.War.Id.ToString(CultureInfo.InvariantCulture));
            n.Add("origin", iw.Origin.Tag);
            n.Add("rebel", iw.Rebel.Tag);
            n.Add("culture", iw.Culture);
            n.Add("start", iw.StartDay.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void LoadIndependence(Node root)
    {
        var w = _game.World;
        Independence.Clear();
        IndependenceWars.Clear();
        FreedTotal = root.GetInt("freed_total", 0);
        foreach (var n in root.GetAll("independence"))
        {
            var c = w.GetCountry(n.GetString("country"));
            string culture = n.GetString("culture") ?? "";
            if (c == null || !CultureByKey.ContainsKey(culture)) continue;
            Independence.Add(new IndependenceMovement { Country = c, Culture = culture, Progress = n.GetDouble("progress"), Suppressed = n.GetInt("suppressed", 0), Started = GameDate.Parse(n.GetString("started") ?? "", w.Date) });
        }
        foreach (var n in root.GetAll("independence_war"))
        {
            int id = n.GetInt("war", -1);
            var war = w.Wars.FirstOrDefault(x => x.Id == id);
            var o = w.GetCountry(n.GetString("origin"));
            var r = w.GetCountry(n.GetString("rebel"));
            if (war == null || o == null || r == null) continue;
            IndependenceWars.Add(new IndependenceWar { War = war, Origin = o, Rebel = r, Culture = n.GetString("culture") ?? "", StartDay = n.GetInt("start", w.Date.TotalDays) });
        }
    }
}
