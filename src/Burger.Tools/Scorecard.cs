using System.Globalization;
using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Tools;

/// <summary>
/// The historical scorecard: expectations about what a simulated world should contain, measured over several seeds. Nothing is forced:
/// each line is a question (did Germany fight Poland between 1937 and 1942? did Italy win in Ethiopia, if the war happened?) answered
/// once per world; the hit rate over the worlds is compared with the band the line expects. See projects/Earth1930/scorecard.txt.
/// </summary>
public sealed class ScorecardRecorder : Burger.Core.Modding.GameScript
{
    private sealed class WarRecord
    {
        public string A = "", B = "";
        public int StartMonth;
        public HashSet<string> SideA = new(), SideB = new();
        public List<string> BStates = new(), AStates = new();
        public double? AShareOfB, BShareOfA;
        public bool Measured;
    }

    private readonly Game _game;
    private readonly List<WarRecord> _wars = new();
    private readonly Dictionary<(string, string), int> _pairSeen = new();      // pair (either order, sorted) -> first month
    private readonly Dictionary<(string, string), int> _rulingSeen = new();    // (tag, ideology) -> first month
    private readonly Dictionary<(string, string), HashSet<int>> _allianceSeen = new(); // pair -> the months it was in force
    private readonly Dictionary<string, int> _civilWarSeen = new();
    private readonly Dictionary<int, Dictionary<string, string>> _owners = new();   // year -> state -> owner tag
    private readonly Dictionary<int, Dictionary<string, string>> _rulers = new();   // year -> country -> ruling ideology
    private readonly Dictionary<int, Dictionary<string, int>> _factories = new();
    private readonly Dictionary<int, HashSet<string>> _alive = new();
    private readonly Dictionary<int, HashSet<(string, string)>> _subjects = new();
    private readonly Dictionary<int, HashSet<string>> _demil = new();
    private readonly Dictionary<int, HashSet<string>> _techs = new();               // year -> "TAG:tech"
    private readonly HashSet<int> _worldWarMonths = new();
    private int _warsDeclared;
    private readonly List<int> _warMonths = new();
    private readonly HashSet<string> _great = new() { "GER", "ITA", "JAP", "FRA", "ENG", "SOV", "USA" };
    private int _lastMonth = -1;
    private readonly Dictionary<string, string> _firstOwner = new();

    public ScorecardRecorder(Game game) { _game = game; }

    private static int Month(GameDate d) => d.Year * 12 + d.Month - 1;
    private static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    public override void OnWarDeclared(War war)
    {
        _warsDeclared++;
        _warMonths.Add(Month(_game.World.Date));
        var r = new WarRecord { A = war.AttackerLeader.Tag, B = war.DefenderLeader.Tag, StartMonth = Month(_game.World.Date) };
        r.SideA.Add(r.A);
        r.SideB.Add(r.B);
        r.BStates = war.DefenderLeader.OwnedStates.Select(s => s.Key).ToList();
        r.AStates = war.AttackerLeader.OwnedStates.Select(s => s.Key).ToList();
        foreach (var x in war.Attackers) foreach (var y in war.Defenders) _pairSeen.TryAdd(Pair(x.Tag, y.Tag), r.StartMonth); // (a war that is over before the next monthly sample still counts)
        _wars.Add(r);
    }

    /// <summary>Called once a day by the runner; samples on the first of every month.</summary>
    public void Daily()
    {
        var d = _game.World.Date;
        int month = Month(d);
        if (month == _lastMonth) return;
        _lastMonth = month;
        Sample(month, d);
    }

    private void Sample(int month, GameDate d)
    {
        var w = _game.World;
        var live = w.Countries.Where(c => c.Exists).ToList();
        // wars: every pair on opposite sides, and which great powers are at war together
        if (Environment.GetEnvironmentVariable("BURGER_WARSCORE_LOG") is { } wl)
            foreach (var war in w.Wars.Where(x => x.AttackerLeader.Tag == wl || x.DefenderLeader.Tag == wl))
            {
                int da = _game.World.Divisions.Count(x => war.Attackers.Contains(x.Owner)), db = _game.World.Divisions.Count(x => war.Defenders.Contains(x.Owner));
                int held = war.DefenderLeader.OwnedStates.Count(st => st.Locations.Any(l => l.Controller != null && war.Attackers.Contains(l.Controller)));
                Console.WriteLine($"WS {d.ToDisplay()} {war.AttackerLeader.Tag} vs {war.DefenderLeader.Tag}: score {war.WarScore:0} divisions {da} vs {db}, states of the defender with attackers on them {held}/{war.DefenderLeader.OwnedStates.Count}");
                Console.WriteLine("   at: " + string.Join(", ", _game.World.Divisions.Where(x => x.Owner == war.AttackerLeader && x.Location != null).GroupBy(x => x.Location!.State?.Key ?? "?").OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} {g.Count()}")) + "; armies " + string.Join(",", _game.World.ArmiesOf(war.AttackerLeader).Take(6).Select(a => $"{a.Mode}/{a.FrontEnemy?.Tag}/{a.Divisions.Count}/pairs{a.FrontPairs.Count}")));
            }
        foreach (var war in w.Wars)
        {
            foreach (var a in war.Attackers) foreach (var b in war.Defenders) _pairSeen.TryAdd(Pair(a.Tag, b.Tag), month);
            foreach (var r in _wars.Where(r => r.Measured == false && (r.A == war.AttackerLeader.Tag && r.B == war.DefenderLeader.Tag)))
            {
                foreach (var x in war.Attackers) r.SideA.Add(x.Tag);
                foreach (var x in war.Defenders) r.SideB.Add(x.Tag);
            }
            int greats = war.Attackers.Concat(war.Defenders).Count(c => _great.Contains(c.Tag));
            if (greats >= 3 && war.Attackers.Any(c => _great.Contains(c.Tag)) && war.Defenders.Any(c => _great.Contains(c.Tag))) _worldWarMonths.Add(month);
        }
        foreach (var c in live)
        {
            if (_game.Politics.Enabled) _rulingSeen.TryAdd((c.Tag, _game.Politics.Of(c).Ruling), month);
            foreach (var a in w.Relations.AlliesOf(c)) { var key = Pair(c.Tag, a.Tag); if (!_allianceSeen.TryGetValue(key, out var set)) _allianceSeen[key] = set = new HashSet<int>(); set.Add(month); }
        }
        foreach (var cw in _game.Politics.CivilWars) _civilWarSeen.TryAdd(cw.Origin.Tag, month);
        // the outcome of wars two years on
        foreach (var r in _wars.Where(r => !r.Measured && month - r.StartMonth >= 24))
        {
            r.Measured = true;
            var owner = w.States.ToDictionary(s => s.Key, s => s.Owner?.Tag ?? "");
            double Share(List<string> states, HashSet<string> by) => states.Count == 0 ? 0 : states.Count(k => owner.TryGetValue(k, out var o) && by.Contains(o)) / (double)states.Count;
            r.AShareOfB = Share(r.BStates, r.SideA);
            r.BShareOfA = Share(r.AStates, r.SideB);
        }
        // yearly snapshots on 1 January
        if (d.Month == 1)
        {
            int y = d.Year;
            _owners[y] = w.States.ToDictionary(s => s.Key, s => s.Owner?.Tag ?? "");
            _rulers[y] = _game.Politics.Enabled ? live.ToDictionary(c => c.Tag, c => _game.Politics.Of(c).Ruling) : new();
            _factories[y] = live.ToDictionary(c => c.Tag, c => _game.Production.Enabled ? _game.Production.CivilianFactories(c) + _game.Production.MilitaryFactories(c) : 0);
            _alive[y] = live.Select(c => c.Tag).ToHashSet();
            _subjects[y] = w.Relations.Subjects.Select(kv => (kv.Key.Tag, kv.Value.Overlord.Tag)).ToHashSet();
            _demil[y] = w.States.Where(s => s.Demilitarized).Select(s => s.Key).ToHashSet();
            var t = new HashSet<string>();
            if (_game.Tech.Enabled)
                foreach (var c in live) foreach (var k in _game.Tech.Known.GetValueOrDefault(c) ?? new HashSet<string>()) t.Add(c.Tag + ":" + k);
            _techs[y] = t;
        }
    }

    // ============================================================================================ evaluation
    public sealed class Line
    {
        public string Name = "", Kind = "", Note = "";
        public double Min, Max;
        public Node Node = null!;
        public double Weight = 1;
        /// <summary>1-5: how much the event matters (5 = the most important). Lines of priority 2-5 must all be within their band.</summary>
        public int Priority = 3;
    }

    public static List<Line> Parse(string path)
    {
        var root = ScriptParser.ParseFile(path);
        var list = new List<Line>();
        foreach (var n in root.All)
        {
            if (n.Children == null) continue;
            var l = new Line { Name = n.Key, Kind = n.GetString("kind", "")!, Note = n.GetString("note", "")!, Node = n, Weight = n.GetDouble("weight", 1), Priority = n.GetInt("priority", 3) };
            string band = n.GetString("expect", "0.5")!;
            var parts = band.Split('-');
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lo) && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hi)) { l.Min = lo; l.Max = hi; }
            else if (double.TryParse(band, NumberStyles.Float, CultureInfo.InvariantCulture, out var mid)) { l.Min = Math.Max(0, mid - 0.2); l.Max = Math.Min(1, mid + 0.2); }
            list.Add(l);
        }
        return list;
    }

    private static (int From, int To) Window(Node n) => (n.GetInt("from", 1930) * 12, n.GetInt("to", 1950) * 12 + 11);

    /// <summary>1 = it happened, 0 = it did not, null = the question did not arise in this world.</summary>
    public int? Evaluate(Line l)
    {
        var n = l.Node;
        string a = n.GetString("a") ?? n.GetString("tag") ?? "", b = n.GetString("b") ?? "";
        var (from, to) = Window(n);
        int year = n.GetInt("year", 1940);
        bool Yes(bool v) => v;
        switch (l.Kind)
        {
            case "war":
                return _pairSeen.TryGetValue(Pair(a, b), out var m) && m >= from && m <= to ? 1 : 0;
            case "war_result":
            {
                // given a war that started in the window, did a's side win (hold at least a share of b's states two years on)?
                var rec = _wars.FirstOrDefault(r => r.StartMonth >= from && r.StartMonth <= to && ((r.A == a && r.B == b) || (r.SideA.Contains(a) && r.SideB.Contains(b) && (r.A == a || r.B == b))));
                if (rec == null || !rec.Measured) return null;
                double need = n.GetDouble("share", 0.3);
                return rec.AShareOfB >= need ? 1 : 0;
            }
            case "war_holds":
            {
                var rec = _wars.FirstOrDefault(r => r.StartMonth >= from && r.StartMonth <= to && ((r.A == a && r.B == b) || (r.SideA.Contains(a) && r.SideB.Contains(b))));
                if (rec == null || !rec.Measured) return null;
                return rec.AShareOfB < n.GetDouble("share", 0.1) ? 1 : 0; // the defender kept (nearly) all its land
            }
            case "ruling":
                return _rulers.TryGetValue(year, out var r1) && r1.GetValueOrDefault(a) == b ? 1 : 0;
            case "ruling_not":
                return _rulers.TryGetValue(year, out var r2) && r2.TryGetValue(a, out var ide) && ide != b ? 1 : 0;
            case "ruling_ever":
                return _rulingSeen.TryGetValue((a, b), out var m2) && m2 >= from && m2 <= to ? 1 : 0;
            case "exists":
                return _alive.TryGetValue(year, out var al) && al.Contains(a) ? 1 : 0;
            case "gone":
            {
                // the country no longer exists - or, with share, is left with less than (1 - share) of the land it had in 1930 (a partition leaves a remnant)
                if (_alive.TryGetValue(year, out var al2) && !al2.Contains(a)) return 1;
                if (n.GetDouble("share", 0) <= 0 || !_owners.TryGetValue(year, out var oy3) || !_owners.TryGetValue(1930, out var o03)) return 0;
                var own = o03.Where(kv => kv.Value == a).Select(kv => kv.Key).ToList();
                return own.Count > 0 && own.Count(k => oy3.GetValueOrDefault(k) == a) <= (1 - n.GetDouble("share")) * own.Count ? 1 : 0;
            }
            case "owns":
                return _owners.TryGetValue(year, out var o1) && o1.GetValueOrDefault(b) == a ? 1 : 0;
            case "ideology_land":
            {
                // countries ruled by ideology b hold at least `share` of the land country a held in 1930 (the communists win China: whatever the tag of the winner)
                if (!_owners.TryGetValue(year, out var oy2) || !_owners.TryGetValue(1930, out var o02) || !_rulers.TryGetValue(year, out var ry)) return 0;
                var land = o02.Where(kv => kv.Value == a).Select(kv => kv.Key).ToList();
                if (land.Count == 0) return 0;
                int held = land.Count(k => oy2.TryGetValue(k, out var owner) && ry.TryGetValue(owner, out var ru) && ru == b);
                return held >= n.GetDouble("share", 0.3) * land.Count ? 1 : 0;
            }
            case "owns_country":
            {
                // a owns at least half of the states b owned in 1930
                if (!_owners.TryGetValue(year, out var oy) || !_owners.TryGetValue(1930, out var o0)) return 0;
                var bs = o0.Where(kv => kv.Value == b).Select(kv => kv.Key).ToList();
                // a owns half of b's old land, or b is a's subject (a client counts)
                bool client = _subjects.TryGetValue(year, out var subs) && subs.Contains((b, a));
                return client || (bs.Count > 0 && bs.Count(k => oy.GetValueOrDefault(k) == a) * 2 >= bs.Count) ? 1 : 0;
            }
            case "alliance":
                return _allianceSeen.TryGetValue(Pair(a, b), out var m3) && m3.Any(m => m >= from && m <= to) ? 1 : 0;
            case "alliance_at":
                return _owners.ContainsKey(year) && _allianceSeen.TryGetValue(Pair(a, b), out var m4) && m4.Any(m => m >= year * 12 && m <= year * 12 + 11) ? 1 : 0;
            case "independent":
                return _subjects.TryGetValue(year, out var sb) && _alive.TryGetValue(year, out var al3) && al3.Contains(a) && !sb.Any(x => x.Item1 == a) ? 1 : 0;
            case "civil_war":
                return _civilWarSeen.TryGetValue(a, out var m5) && m5 >= from && m5 <= to ? 1 : 0;
            case "countries":
            {
                if (!_alive.TryGetValue(year, out var al4)) return 0;
                return al4.Count >= n.GetInt("min", 0) && al4.Count <= n.GetInt("max", 999) ? 1 : 0;
            }
            case "wars":
            {
                int count = _warMonths.Count(x => x >= from && x <= to);
                return count >= n.GetInt("min", 0) && count <= n.GetInt("max", 999) ? 1 : 0;
            }
            case "world_war":
                return _worldWarMonths.Any(x => x >= from && x <= to) ? 1 : 0;
            case "tech":
                return _techs.TryGetValue(year, out var tt) && tt.Contains(a + ":" + b) ? 1 : 0;
            case "industry_rank":
            {
                if (!_factories.TryGetValue(year, out var f)) return 0;
                int rank = f.Values.Count(v => v > f.GetValueOrDefault(a)) + 1;
                return rank <= n.GetInt("rank", 3) ? 1 : 0;
            }
            case "growth":
            {
                if (!_factories.TryGetValue(year, out var fy) || !_factories.TryGetValue(1930, out var f0)) return 0;
                return f0.GetValueOrDefault(a) > 0 && fy.GetValueOrDefault(a) >= f0[a] * n.GetDouble("factor", 1.5) ? 1 : 0;
            }
            case "remilitarized":
                return _demil.TryGetValue(year, out var dm) && _demil.TryGetValue(1930, out var d0) && d0.Contains(b) && !dm.Contains(b) ? 1 : 0;
            default:
                return Yes(false) ? 1 : null;
        }
    }

    /// <summary>The wars of the great powers: when, who led each side and who joined (BURGER_WAR_LOG).</summary>
    public string WarTable()
    {
        var big = new HashSet<string> { "GER", "ITA", "JAP", "SOV", "ENG", "FRA", "USA", "POL", "CZE", "CHI" };
        return string.Join("\n", _wars.Where(w => w.SideA.Concat(w.SideB).Any(big.Contains)).OrderBy(w => w.StartMonth)
            .Select(w => $"    {w.StartMonth / 12}.{w.StartMonth % 12 + 1:00} {w.A} [{string.Join(" ", w.SideA.Where(x => x != w.A))}] vs {w.B} [{string.Join(" ", w.SideB.Where(x => x != w.B))}]"));
    }

    /// <summary>Factories of the biggest industrial powers on 1 January of the years recorded (BURGER_FACT_LOG).</summary>
    public string FactoryTable(int top = 8)
    {
        var tags = _factories.TryGetValue(1930, out var f0) ? f0.OrderByDescending(kv => kv.Value).Take(top).Select(kv => kv.Key).ToList() : new List<string>();
        return string.Join(nl_, _factories.OrderBy(kv => kv.Key).Where(kv => kv.Key % 5 == 0 || kv.Key == 1939).Select(kv => $"    {kv.Key}: " + string.Join(" ", tags.Select(t => $"{t} {kv.Value.GetValueOrDefault(t)}"))));
    }
    private const string nl_ = "\n";

    public List<(string Name, int? Result)> EvaluateAll(List<Line> lines) => lines.Select(l => (l.Name, Evaluate(l))).ToList();
}
