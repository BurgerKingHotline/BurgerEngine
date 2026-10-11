using System.Globalization;
using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Production;

/// <summary>A strategic commodity from <c>common/commodities.txt</c> (steel, oil, rubber, aluminium, chromium, tungsten ...).</summary>
public sealed class CommodityDef
{
    public string Key = "";
    public string Name = "";
    public Rgb Color = new(160, 160, 160);
    /// <summary>Gold one unit costs when it is traded (commodity_trade_price define scales it).</summary>
    public double Price = 1;
    /// <summary>Monthly use of each civilian factory (industry and construction need it too).</summary>
    public double CivilianUse;
    public Node Definition = Node.Root("");
}

/// <summary>
/// Overridable commodity rules (commodities feature, backend/Commodities.cs): how much a country digs, what it needs and who sells to whom.
/// </summary>
public class CommodityRules
{
    /// <summary>Monthly output of a state: its deposits (units a year / 12) scaled by what the owner controls and by the modifier extraction_factor.</summary>
    public virtual double Output(Game game, Country c, State s, CommodityDef k, double deposit)
        => deposit / 12.0 * s.ControlledShare(c) * Math.Max(0.1, 1 + game.Modifiers.Sum(c, "extraction") + game.Modifiers.Sum(c, k.Key + "_output"));

    /// <summary>Monthly use of a country besides its military lines: its civilian factories (industry, consumer goods, construction).</summary>
    public virtual double BaseDemand(Game game, Country c, CommodityDef k)
        => game.Production.Enabled ? game.Production.CivilianFactories(c) * k.CivilianUse : 0;

    /// <summary>May the seller sell to the buyer? Not at war, not hated, and not a country that has put an embargo on it (flag embargo_TAG).</summary>
    public virtual bool WillTrade(Game game, Country seller, Country buyer)
    {
        if (game.World.AtWar(seller, buyer)) return false;
        if (seller.Vars.HasFlag("embargo_" + buyer.Tag)) return false;
        // allies of the seller's enemies do not get its goods
        foreach (var war in game.World.WarsOf(seller)) if (war.Involves(buyer) && war.EnemiesOf(seller).Contains(buyer)) return false;
        return game.Diplomacy.Opinion(seller, buyer) > game.Data.Defines.Get("trade_min_opinion", -60);
    }

    /// <summary>Who the seller prefers to sell to (higher first): allies and subjects, then friends.</summary>
    public virtual double TradePriority(Game game, Country seller, Country buyer)
    {
        var r = game.World.Relations;
        double p = game.Diplomacy.Opinion(seller, buyer);
        if (r.AreAllied(seller, buyer)) p += 100;
        if (r.OverlordOf(buyer) == seller || r.OverlordOf(seller) == buyer) p += 150;
        return p;
    }

    /// <summary>Share (0-1) of its demand a country gets, as a speed for what it makes with it: a shortage hurts, but never stops everything (commodity_floor, 0.15).</summary>
    public virtual double Speed(Game game, double satisfied) => Math.Clamp(Math.Max(game.Data.Defines.Get("commodity_floor", 0.15), satisfied), 0, 1);
}

/// <summary>Deposits, trade and shortages of strategic commodities (commodities feature).</summary>
public sealed class CommoditySystem
{
    private readonly Game _game;
    public readonly Dictionary<string, CommodityDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>State key -> commodity -> yearly output at full extraction (history/commodities.txt).</summary>
    public readonly Dictionary<State, Dictionary<string, double>> Deposits = new();
    private readonly Dictionary<string, Dictionary<string, double>> _equipUse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per country and commodity: what it digs, imports, needs, and the share of its need it got last month.</summary>
    public sealed class Balance { public double Output, Import, Export, Demand, MilitaryUse, Satisfied = 1; }
    public readonly Dictionary<Country, Dictionary<string, Balance>> Books = new();
    private readonly Dictionary<Country, Dictionary<string, double>> _usedThisMonth = new();

    public CommoditySystem(Game game) { _game = game; game.SaveSections.Add(new SaveSection { Name = "commodities", Write = Save, Read = Load, Reset = () => { Books.Clear(); _usedThisMonth.Clear(); } }); }

    public bool Enabled => _game.Features.Has(FeatureSet.Commodities) && Defs.Count > 0;
    private CommodityRules Rules => _game.Rules.Commodities;

    // ------------------------------------------------------------------------------------- data
    public void LoadDefinitions(GameData data)
    {
        Defs.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/commodities"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var k = new CommodityDef { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Price = n.GetDouble("price", 1), CivilianUse = n.GetDouble("civilian_use"), Definition = n };
                if (Rgb.TryParse(n.Get("color"), out var col)) k.Color = col;
                Defs[k.Key] = k;
            }
    }

    /// <summary>Reads deposits (history/commodities) and what equipment uses; called when the world is loaded.</summary>
    public void LoadWorld()
    {
        Deposits.Clear();
        _equipUse.Clear();
        if (Defs.Count == 0) return;
        foreach (var root in DataLoader.ParseFileOrFolder(_game.Data, "history/commodities"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var s = _game.World.GetState(n.Key);
                if (s == null) { Log.ErrorOnce("deposit:" + n.Key, $"history/commodities: unknown state '{n.Key}'", n.Location); continue; }
                var d = Deposits.TryGetValue(s, out var existing) ? existing : Deposits[s] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in n.All)
                {
                    if (!Defs.ContainsKey(kv.Key)) { Log.ErrorOnce("deposit-k:" + kv.Key, $"history/commodities: unknown commodity '{kv.Key}'", kv.Location); continue; }
                    d[kv.Key] = d.GetValueOrDefault(kv.Key) + kv.AsDouble(0);
                }
            }
        foreach (var e in _game.Data.Equipment.Values)
            if (e.Definition.Get("commodities") is { Children: not null } cn)
            {
                var use = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cn.All)
                {
                    if (!Defs.ContainsKey(kv.Key)) { Log.ErrorOnce("equip-k:" + kv.Key, $"Equipment '{e.Key}' uses unknown commodity '{kv.Key}'", kv.Location); continue; }
                    use[kv.Key] = kv.AsDouble(0);
                }
                _equipUse[e.Key] = use;
            }
    }

    // ------------------------------------------------------------------------------------- questions
    public Balance BalanceOf(Country c, string commodity)
    {
        if (!Books.TryGetValue(c, out var b)) Books[c] = b = new Dictionary<string, Balance>(StringComparer.OrdinalIgnoreCase);
        if (!b.TryGetValue(commodity, out var x)) b[commodity] = x = new Balance();
        return x;
    }

    public IReadOnlyDictionary<string, double> UseOf(EquipmentType e) => _equipUse.TryGetValue(e.Key, out var u) ? u : EmptyUse;
    private static readonly Dictionary<string, double> EmptyUse = new();

    /// <summary>Speed (0-1) at which this country can make an equipment: its worst shortage among the commodities it needs.</summary>
    public double Factor(Country c, EquipmentType e)
    {
        if (!Enabled || !_equipUse.TryGetValue(e.Key, out var use)) return 1;
        double f = 1;
        foreach (var k in use.Keys) f = Math.Min(f, BalanceOf(c, k).Satisfied);
        return Rules.Speed(_game, f);
    }

    /// <summary>Records what a production line used (units made x the equipment's use).</summary>
    public void Use(Country c, EquipmentType e, double units)
    {
        if (!Enabled || units <= 0 || !_equipUse.TryGetValue(e.Key, out var use)) return;
        if (!_usedThisMonth.TryGetValue(c, out var d)) _usedThisMonth[c] = d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, per) in use) d[k] = d.GetValueOrDefault(k) + per * units;
    }

    /// <summary>The total yearly deposits of a commodity in the states of a country (for the AI and the interface).</summary>
    public double DepositsOf(Country c, string commodity)
    {
        double v = 0;
        foreach (var s in c.OwnedStates) if (Deposits.TryGetValue(s, out var d) && d.TryGetValue(commodity, out var x)) v += x;
        return v;
    }

    /// <summary>Commodities the country is short of (satisfied under 85%), worst first.</summary>
    public List<(string Commodity, double Satisfied)> Shortages(Country c)
        => Defs.Keys.Select(k => (k, BalanceOf(c, k).Satisfied)).Where(x => x.Item2 < 0.85).OrderBy(x => x.Item2).ToList();

    /// <summary>
    /// How much the state's deposits would help a country that is short of commodities (0 when it is not): the summed share of its
    /// shortages the deposits would cover. The AI adds this to its wish for land and for good relations with its owner.
    /// </summary>
    public double NeedScore(Country c, State s)
    {
        if (!Enabled || !Deposits.TryGetValue(s, out var d)) return 0;
        double score = 0;
        foreach (var (k, shortfall) in Shortages(c))
            if (d.TryGetValue(k, out var yearly) && yearly > 0)
            {
                var b = BalanceOf(c, k);
                double missing = Math.Max(0.1, b.Demand - b.Output - b.Import);
                score += Math.Min(2, yearly / 12.0 / missing) * (1 - shortfall);
            }
        return score;
    }

    /// <summary>Need score of everything a country owns (the AI's wish to take, or make friends with, its owner).</summary>
    public double NeedScore(Country c, Country owner) => owner.OwnedStates.Sum(s => NeedScore(c, s));

    // ------------------------------------------------------------------------------------- monthly market
    public void MonthlyTick()
    {
        if (!Enabled) return;
        var alive = _game.World.Countries.Where(c => c.Exists).ToList();
        foreach (var k in Defs.Values)
        {
            var sellers = new List<(Country C, double Surplus)>();
            var buyers = new List<(Country C, double Deficit)>();
            foreach (var c in alive)
            {
                var b = BalanceOf(c, k.Key);
                double output = 0;
                foreach (var s in c.OwnedStates) if (Deposits.TryGetValue(s, out var d) && d.TryGetValue(k.Key, out var yearly)) output += Rules.Output(_game, c, s, k, yearly);
                double military = _usedThisMonth.TryGetValue(c, out var u) ? u.GetValueOrDefault(k.Key) : 0;
                b.Output = output;
                b.MilitaryUse = military;
                b.Demand = Rules.BaseDemand(_game, c, k) + military;
                b.Import = 0;
                b.Export = 0;
                if (output > b.Demand) sellers.Add((c, output - b.Demand));
                else if (b.Demand > output) buyers.Add((c, b.Demand - output));
            }
            // buyers in order of how much they lack, each takes from the sellers that like them most
            var left = sellers.ToDictionary(x => x.C, x => x.Surplus);
            foreach (var (buyer, deficit) in buyers.OrderByDescending(x => x.Deficit))
            {
                double need = deficit;
                foreach (var seller in left.Keys.Where(s => left[s] > 0.001 && Rules.WillTrade(_game, s, buyer)).OrderByDescending(s => Rules.TradePriority(_game, s, buyer)).ToList())
                {
                    double take = Math.Min(need, left[seller]);
                    left[seller] -= take;
                    need -= take;
                    BalanceOf(buyer, k.Key).Import += take;
                    BalanceOf(seller, k.Key).Export += take;
                    if (_game.Features.Has(FeatureSet.Economy) && take > 0)
                    {
                        double gold = take * k.Price * _game.Data.Defines.Get("commodity_trade_price", 0.5);
                        buyer.Gold -= gold;
                        seller.Gold += gold;
                    }
                    if (need <= 0.001) break;
                }
            }
            foreach (var c in alive)
            {
                var b = BalanceOf(c, k.Key);
                b.Satisfied = b.Demand <= 0.0001 ? 1 : Math.Clamp((b.Output + b.Import) / b.Demand, 0, 1);
            }
        }
        _usedThisMonth.Clear();
    }

    // ------------------------------------------------------------------------------------- save
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private void Save(Node root)
    {
        foreach (var (c, books) in Books)
        {
            var n = root.AddBlock("country");
            n.Add("tag", c.Tag);
            foreach (var (k, b) in books) n.Add(k, F(b.Satisfied));
        }
    }

    private void Load(Node root)
    {
        Books.Clear();
        foreach (var n in root.GetAll("country"))
        {
            var c = _game.World.GetCountry(n.GetString("tag"));
            if (c == null) continue;
            foreach (var kv in n.All) if (Defs.ContainsKey(kv.Key)) BalanceOf(c, kv.Key).Satisfied = kv.AsDouble(1);
        }
    }
}
