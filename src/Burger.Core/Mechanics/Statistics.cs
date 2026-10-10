using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Mechanics;

/// <summary>A number recorded for every country each month (for the Ledger charts).</summary>
public sealed class StatMetric
{
    public string Key = "";
    public string Name = "";
    public Func<Game, Country, double?> Read = (_, _) => null;
}

/// <summary>
/// Monthly statistics per country for the Ledger: population, income, manpower, divisions, factories, states...
/// Old samples are thinned out so a long game keeps a bounded history. C# mods can add metrics with <see cref="Add"/>.
/// </summary>
public sealed class StatisticsSystem
{
    private readonly Game _game;
    public readonly List<StatMetric> Metrics = new();
    /// <summary>Dates of the samples.</summary>
    public readonly List<GameDate> Dates = new();
    /// <summary>Values per metric key, per country tag, one per sample (NaN = no value).</summary>
    public readonly Dictionary<string, Dictionary<string, List<double>>> Values = new(StringComparer.OrdinalIgnoreCase);
    public int MaxSamples = 480;

    public StatisticsSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "statistics", Write = Save, Read = Load, Reset = Clear });
        Add("population", "Population", (g, c) => g.Features.Has(FeatureSet.Population) ? g.Population.TotalOf(c) : null);
        Add("states", "States", (g, c) => c.OwnedStates.Count);
        Add("income", "Income", (g, c) => g.Features.Has(FeatureSet.Economy) ? c.LastIncome : null);
        Add("treasury", "Treasury", (g, c) => g.Features.Has(FeatureSet.Economy) ? c.Gold : null);
        Add("manpower", "Manpower", (g, c) => g.Features.Has(FeatureSet.Military) || g.Features.Has(FeatureSet.Economy) ? c.Manpower : null);
        Add("divisions", "Divisions", (g, c) => g.Features.Has(FeatureSet.Military) ? g.World.Divisions.Count(d => d.Owner == c) : null);
        Add("factories", "Factories", (g, c) => g.Features.Has(FeatureSet.Production) ? g.Production.CivilianFactories(c) + g.Production.MilitaryFactories(c) : null);
        Add("ships", "Ships", (g, c) => g.Features.Has(FeatureSet.Navy) ? g.Navy.FleetsOf(c).Sum(f => f.Ships.Count) : null);
        Add("war_dead", "War dead", (g, c) => g.Features.Has(FeatureSet.Military) ? g.Diplomacy.MenLost.GetValueOrDefault(c) : null);
        Add("war_exhaustion", "Exhaustion", (g, c) => g.Features.Has(FeatureSet.Diplomacy) ? g.Diplomacy.WarExhaustion(c) : null);
        Add("stability", "Stability", (g, c) => g.Resources.Defs.FirstOrDefault(r => r.Key.Equals("stability", StringComparison.OrdinalIgnoreCase)) is { } d ? g.Resources.Get(c, d) : null);
        Add("colonies", "Colonies", (g, c) => g.Features.Has(FeatureSet.Population) && (g.Features.Has(FeatureSet.CoresClaims) || g.Features.Has(FeatureSet.Independence)) ? g.Population.ColonyCount(c) : null);
        Add("coups", "Coups & revolts", (g, c) => g.Features.Has(FeatureSet.CivilWars) ? g.Politics.RevoltsOf(c) : null);
    }

    /// <summary>Adds a metric recorded every month from now on.</summary>
    public void Add(string key, string name, Func<Game, Country, double?> read)
    {
        Metrics.RemoveAll(m => m.Key == key);
        Metrics.Add(new StatMetric { Key = key, Name = name, Read = read });
    }

    /// <summary>Metrics that have values in this game.</summary>
    public IEnumerable<StatMetric> Available => Metrics.Where(m => Values.TryGetValue(m.Key, out var v) && v.Values.Any(l => l.Any(x => !double.IsNaN(x))));

    public void Clear()
    {
        Dates.Clear();
        Values.Clear();
        Interval = 1;
        _skip = 0;
    }

    public void MonthlyTick()
    {
        if (_skip > 0) { _skip--; return; }
        _skip = Interval - 1;
        // civil war rebels count as part of the country they split from (its line does not dip while the war lasts)
        var rebelOf = new Dictionary<Country, Country>();
        foreach (var cw in _game.Politics.CivilWars) rebelOf[cw.Origin] = cw.Rebel;
        var rebels = rebelOf.Values.ToHashSet();
        var countries = _game.World.Countries.Where(c => c.Exists && !rebels.Contains(c)).ToList();
        int index = Dates.Count;
        Dates.Add(_game.World.Date);
        foreach (var m in Metrics)
        {
            if (!Values.TryGetValue(m.Key, out var perCountry)) Values[m.Key] = perCountry = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in countries)
            {
                double? v;
                try
                {
                    v = m.Read(_game, c);
                    if (v != null && rebelOf.TryGetValue(c, out var rebel) && rebel.Exists && m.Read(_game, rebel) is { } rv) v += rv;
                }
                catch { v = null; }
                if (v == null) continue;
                if (!perCountry.TryGetValue(c.Tag, out var list)) perCountry[c.Tag] = list = new List<double>();
                while (list.Count < index) list.Add(double.NaN);
                list.Add(v.Value);
            }
        }
        if (Dates.Count > MaxSamples) Thin();
    }

    /// <summary>Months between samples (doubles each time the history is thinned).</summary>
    public int Interval = 1;
    private int _skip;

    /// <summary>Keeps every other sample (the first one stays) and records half as often from now on, so the whole game stays evenly covered.</summary>
    private void Thin()
    {
        var keep = new List<int>();
        for (int i = 0; i < Dates.Count; i++) if (i % 2 == 0 || i == Dates.Count - 1) keep.Add(i);
        Interval *= 2;
        _skip = Interval - 1;
        var dates = keep.Select(i => Dates[i]).ToList();
        Dates.Clear(); Dates.AddRange(dates);
        foreach (var perCountry in Values.Values)
            foreach (var tag in perCountry.Keys.ToList())
            {
                var l = perCountry[tag];
                perCountry[tag] = keep.Select(i => i < l.Count ? l[i] : double.NaN).ToList();
            }
    }

    /// <summary>Metrics where the world total makes sense (counts and amounts, not levels like stability).</summary>
    public static readonly HashSet<string> Summable = new(StringComparer.OrdinalIgnoreCase)
        { "population", "states", "income", "treasury", "manpower", "divisions", "factories", "ships", "war_dead", "colonies", "coups" };

    /// <summary>Running totals (they never fall).</summary>
    public static readonly HashSet<string> Cumulative = new(StringComparer.OrdinalIgnoreCase) { "war_dead", "coups" };

    /// <summary>World total of a metric per sample (sum of all countries; NaN where nobody had a value).</summary>
    public List<double> WorldSeries(string metric)
    {
        var result = Enumerable.Repeat(double.NaN, Dates.Count).ToList();
        if (!Values.TryGetValue(metric, out var pc)) return result;
        bool cumulative = Cumulative.Contains(metric);
        foreach (var list in pc.Values)
        {
            double last = double.NaN;
            for (int i = 0; i < result.Count; i++)
            {
                double v = i < list.Count ? list[i] : double.NaN;
                if (!double.IsNaN(v)) last = v;
                else if (cumulative) v = last; // a country that is gone keeps its total (war dead do not come back)
                if (!double.IsNaN(v)) result[i] = (double.IsNaN(result[i]) ? 0 : result[i]) + v;
            }
        }
        return result;
    }

    /// <summary>Values of one metric for one country (NaN where it did not exist), aligned with <see cref="Dates"/>.</summary>
    public List<double> Series(string metric, string tag)
    {
        var result = new List<double>(Dates.Count);
        if (Values.TryGetValue(metric, out var pc) && pc.TryGetValue(tag, out var l)) result.AddRange(l);
        while (result.Count < Dates.Count) result.Add(double.NaN);
        return result;
    }

    private void Save(Node root)
    {
        root.Add("interval", Interval.ToString(CultureInfo.InvariantCulture));
        root.Add("skip", _skip.ToString(CultureInfo.InvariantCulture));
        var d = root.AddBlock("dates");
        foreach (var x in Dates) d.AddItem(x.ToScript());
        foreach (var (metric, pc) in Values)
        {
            var m = root.AddBlock("metric");
            m.Add("key", metric);
            foreach (var (tag, list) in pc)
                m.Add(tag, string.Join(" ", list.Select(v => double.IsNaN(v) ? "-" : v.ToString("0.###", CultureInfo.InvariantCulture))), true);
        }
    }

    private void Load(Node root)
    {
        Clear();
        Interval = Math.Max(1, root.GetInt("interval", 1));
        _skip = Math.Max(0, root.GetInt("skip", 0));
        foreach (var s in root.GetList("dates")) if (GameDate.TryParse(s, out var date)) Dates.Add(date);
        foreach (var m in root.GetAll("metric"))
        {
            string key = m.GetString("key") ?? "";
            var pc = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in m.All)
            {
                if (c.Key == "key" || c.Value == null) continue;
                pc[c.Key] = c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t == "-" ? double.NaN : double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToList();
            }
            Values[key] = pc;
        }
    }
}
