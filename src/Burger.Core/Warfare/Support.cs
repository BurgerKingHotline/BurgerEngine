using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Warfare;

/// <summary>Equipment sent every month from one country's stockpile to another (lend-lease, production feature).</summary>
public sealed class LendLeaseDeal
{
    public Country From = null!;
    public Country To = null!;
    public EquipmentType Equipment = null!;
    /// <summary>Share of the sender's stockpile of that equipment sent each month (0-1).</summary>
    public double Share;
}

/// <summary>Overridable rules for lend-lease and volunteers. Subclass and assign to <c>Game.Rules.Support</c> (the lend_lease feature puts a copy in backend/Support.cs).</summary>
public class SupportRules
{
    /// <summary>Most divisions a country may have abroad as volunteers.</summary>
    public virtual int MaxVolunteers(Game game, Country from)
    {
        int own = game.World.Divisions.Count(d => d.Owner == from) + game.World.Divisions.Count(d => d.VolunteerFrom == from);
        return Math.Max(1, (int)Math.Floor(own * game.Data.Defines.Get("volunteer_share", 0.15)));
    }

    /// <summary>Why <paramref name="from"/> cannot send volunteers to <paramref name="to"/> (null = it can).</summary>
    public virtual string? CannotSendVolunteers(Game game, Country from, Country to)
    {
        if (from == to) return "Not to yourself";
        var w = game.World;
        if (!w.WarsOf(to).Any()) return $"{to.Name} is not at war";
        if (w.AtWar(from, to)) return "You are at war with them";
        var enemies = w.WarsOf(to).SelectMany(x => x.EnemiesOf(to)).ToHashSet();
        if (enemies.Contains(from)) return "You are their enemy";
        if (enemies.Any(e => w.Relations.AreAllied(from, e))) return "You are allied with their enemy";
        if (w.WarsOf(from).Any(x => x.SideOf(from).Contains(to))) return "You fight in the same war already";
        if (w.Divisions.Count(d => d.VolunteerFrom == from) >= MaxVolunteers(game, from)) return $"At most {MaxVolunteers(game, from)} volunteer divisions";
        if (to.Capital == null || to.Capital.Controller != to) return $"{to.Name} has no capital to land in";
        return null;
    }

    /// <summary>Why <paramref name="from"/> cannot lend-lease to <paramref name="to"/> (null = it can).</summary>
    public virtual string? CannotLendLease(Game game, Country from, Country to)
    {
        if (from == to) return "Not to yourself";
        if (!game.Features.Has(FeatureSet.Production)) return "Needs the production feature";
        if (game.World.AtWar(from, to)) return "You are at war with them";
        if (!to.Exists) return "They do not exist";
        return null;
    }

    /// <summary>Opinion the receiver gains (once) for volunteers / for lend-lease.</summary>
    public virtual double VolunteerOpinion(Game game) => game.Data.Defines.Get("volunteer_opinion", 20);
    public virtual double LendLeaseOpinion(Game game) => game.Data.Defines.Get("lend_lease_opinion", 15);
}

/// <summary>
/// Lend-lease (monthly equipment deliveries, production feature) and volunteers (divisions that fight for another
/// country's war without joining it; they come home when that country is at peace). Part of the lend_lease feature.
/// </summary>
public sealed class SupportSystem
{
    private readonly Game _game;
    public readonly List<LendLeaseDeal> Deals = new();
    /// <summary>Statistics since the game was loaded (not saved).</summary>
    public double EquipmentSent;
    public int VolunteersSent;

    public SupportSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "support", Write = Save, Read = Load, Reset = () => Deals.Clear() });
    }

    private SupportRules Rules => _game.Rules.Support;
    public bool Enabled => _game.Features.Has(FeatureSet.LendLease);

    // ------------------------------------------------------------------------------------- volunteers
    public int MaxVolunteers(Country from) => Rules.MaxVolunteers(_game, from);
    public string? CannotSendVolunteers(Country from, Country to) => Rules.CannotSendVolunteers(_game, from, to);
    public IEnumerable<Division> VolunteersOf(Country from) => _game.World.Divisions.Where(d => d.VolunteerFrom == from);

    /// <summary>Sends a division as a volunteer: it fights for <paramref name="to"/> (lands at its capital) until it is at peace.</summary>
    public string? SendVolunteer(Country from, Country to, Division d)
    {
        var reason = CannotSendVolunteers(from, to);
        if (reason != null) return reason;
        if (d.Owner != from || d.VolunteerFrom != null) return "Not one of your divisions";
        if (d.InBattle) return "The division is in battle";
        var wf = _game.Warfare;
        wf.StopDivision(d);
        wf.AssignToArmy(d, null);
        d.FrontPost = null;
        d.Retreating = false;
        d.Owner = to;
        d.VolunteerFrom = from;
        wf.Teleport(d, to.Capital!);
        VolunteersSent++;
        if (VolunteersOf(from).Count() == 1 && _game.Features.Has(FeatureSet.Diplomacy))
            _game.Diplomacy.AddOpinion(to, from, "Sent volunteers", Rules.VolunteerOpinion(_game), 1);
        _game.NotifyPlayer($"{from.Name} sent {d.Name} to fight for {to.Name}.", NotificationKind.Good, to, from);
        return null;
    }

    /// <summary>Sends up to <paramref name="count"/> of the sender's idle divisions (not in battle, not in an army first).</summary>
    public int SendVolunteers(Country from, Country to, int count)
    {
        int sent = 0;
        var candidates = _game.World.Divisions.Where(d => d.Owner == from && d.VolunteerFrom == null && !d.InBattle)
            .OrderBy(d => d.Army != null).ThenBy(d => d.Army?.FrontEnemy != null).ToList();
        foreach (var d in candidates)
        {
            if (sent >= count || SendVolunteer(from, to, d) != null) break;
            sent++;
        }
        return sent;
    }

    /// <summary>Brings a volunteer division home.</summary>
    public void Recall(Division d)
    {
        var home = d.VolunteerFrom;
        if (home == null) return;
        var wf = _game.Warfare;
        if (d.Battle != null)
        {
            d.Battle.Attackers.Remove(d);
            d.Battle.Defenders.Remove(d);
            d.Battle = null;
            d.AttackTarget = null;
        }
        wf.StopDivision(d);
        wf.AssignToArmy(d, null);
        d.FrontPost = null;
        d.VolunteerFrom = null;
        var at = home.Capital != null && home.Capital.Controller == home ? home.Capital : wf.NearestOwnLocation(home, d.Location);
        if (!home.Exists || at == null) { wf.DestroyDivision(d); return; }
        d.Owner = home;
        wf.Teleport(d, at);
        _game.NotifyPlayer($"{d.Name} has come home.", NotificationKind.Good, home);
    }

    // ------------------------------------------------------------------------------------- lend-lease
    public string? CannotLendLease(Country from, Country to) => Rules.CannotLendLease(_game, from, to);
    public LendLeaseDeal? DealOf(Country from, Country to, EquipmentType e) => Deals.FirstOrDefault(x => x.From == from && x.To == to && x.Equipment == e);

    /// <summary>Sets the monthly share of a stockpile sent to another country (0 ends the deal).</summary>
    public string? SetLendLease(Country from, Country to, EquipmentType e, double share)
    {
        var deal = DealOf(from, to, e);
        if (share <= 0) { if (deal != null) Deals.Remove(deal); return null; }
        var reason = CannotLendLease(from, to);
        if (reason != null) return reason;
        if (deal == null)
        {
            if (!Deals.Any(x => x.From == from && x.To == to) && _game.Features.Has(FeatureSet.Diplomacy))
                _game.Diplomacy.AddOpinion(to, from, "Lend-lease", Rules.LendLeaseOpinion(_game), 1);
            Deals.Add(deal = new LendLeaseDeal { From = from, To = to, Equipment = e });
        }
        deal.Share = Math.Clamp(share, 0, 1);
        return null;
    }

    /// <summary>One-time shipment of an amount of any equipment in the stockpile (arrives at once).</summary>
    public string? SendOnce(Country from, Country to, string equipment, double amount)
    {
        var reason = CannotLendLease(from, to);
        if (reason != null) return reason;
        double have = Math.Floor(from.Stockpile.GetValueOrDefault(equipment));
        amount = Math.Floor(Math.Min(amount, have));
        if (amount < 1) return "Nothing to send";
        from.Stockpile[equipment] = from.Stockpile.GetValueOrDefault(equipment) - amount;
        to.Stockpile[equipment] = to.Stockpile.GetValueOrDefault(equipment) + amount;
        EquipmentSent += amount;
        if (!_shipped.Contains((from, to)) && _game.Features.Has(FeatureSet.Diplomacy))
        {
            _shipped.Add((from, to));
            _game.Diplomacy.AddOpinion(to, from, "Sent equipment", Rules.LendLeaseOpinion(_game), 1);
        }
        string name = _game.Data.Equipment.TryGetValue(equipment, out var e) ? e.Name : equipment;
        if (to.IsPlayer) _game.Notify($"{from.Name} sent us {amount:0} {name}.", NotificationKind.Good, null);
        return null;
    }

    private readonly HashSet<(Country, Country)> _shipped = new();

    // ------------------------------------------------------------------------------------- ticks
    public void DailyTick()
    {
        // volunteers come home when the country they help is at peace (or turns on their homeland)
        foreach (var d in _game.World.Divisions.Where(d => d.VolunteerFrom != null).ToList())
        {
            var from = d.VolunteerFrom!;
            if (!from.Exists || !_game.World.WarsOf(d.Owner).Any() || _game.World.AtWar(from, d.Owner)) Recall(d);
        }
    }

    public void MonthlyTick()
    {
        if (!_game.Features.Has(FeatureSet.Production)) return;
        foreach (var deal in Deals.ToList())
        {
            if (CannotLendLease(deal.From, deal.To) != null || !deal.From.Exists) { Deals.Remove(deal); continue; }
            double stock = deal.From.Stockpile.GetValueOrDefault(deal.Equipment.Key);
            double amount = Math.Floor(stock * deal.Share);
            if (amount < 1) continue;
            deal.From.Stockpile[deal.Equipment.Key] = stock - amount;
            deal.To.Stockpile[deal.Equipment.Key] = deal.To.Stockpile.GetValueOrDefault(deal.Equipment.Key) + amount;
            EquipmentSent += amount;
            if (deal.To.IsPlayer) _game.Notify($"Lend-lease: {amount:0} {deal.Equipment.Name} arrived from {deal.From.Name}.", NotificationKind.Good, null);
        }
    }

    /// <summary>AI: helps friends at war with equipment and volunteers.</summary>
    public void AiMonthly(Country c)
    {
        var w = _game.World;
        if (w.WarsOf(c).Any()) { foreach (var d in Deals.Where(x => x.From == c).ToList()) Deals.Remove(d); return; } // needs everything itself
        var friends = w.Countries.Where(o => o != c && o.Exists && w.WarsOf(o).Any() &&
                                             (!_game.Features.Has(FeatureSet.Diplomacy) || _game.Diplomacy.Opinion(c, o) >= 20 || w.Relations.IsGuaranteeing(c, o) || w.Relations.OverlordOf(o) == c)).ToList();
        foreach (var f in friends)
        {
            if (_game.Features.Has(FeatureSet.Production) && CannotLendLease(c, f) == null)
                foreach (var e in _game.Data.Equipment.Values.Where(e => e.Category.Equals("army", StringComparison.OrdinalIgnoreCase)))
                    if (c.Stockpile.GetValueOrDefault(e.Key) > 200 && DealOf(c, f, e) == null) SetLendLease(c, f, e, 0.1);
            if (CannotSendVolunteers(c, f) == null && w.Divisions.Count(d => d.Owner == c) >= 4 && _game.Rng.NextDouble() < 0.4)
                SendVolunteers(c, f, 1);
        }
        // deals with countries that are no longer friends end
        foreach (var d in Deals.Where(x => x.From == c && !friends.Contains(x.To)).ToList()) Deals.Remove(d);
    }

    // ------------------------------------------------------------------------------------- save
    private void Save(Node root)
    {
        foreach (var d in Deals)
        {
            var n = root.AddBlock("lend_lease");
            n.Add("from", d.From.Tag);
            n.Add("to", d.To.Tag);
            n.Add("equipment", d.Equipment.Key);
            n.Add("share", d.Share.ToString("0.####", CultureInfo.InvariantCulture));
        }
    }

    private void Load(Node root)
    {
        foreach (var n in root.GetAll("lend_lease"))
        {
            var from = _game.World.GetCountry(n.GetString("from"));
            var to = _game.World.GetCountry(n.GetString("to"));
            if (from == null || to == null || !_game.Data.Equipment.TryGetValue(n.GetString("equipment") ?? "", out var e)) continue;
            Deals.Add(new LendLeaseDeal { From = from, To = to, Equipment = e, Share = n.GetDouble("share", 0.1) });
        }
    }
}
