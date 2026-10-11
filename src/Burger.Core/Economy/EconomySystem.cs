using Burger.Core.Model;

namespace Burger.Core.Economy;

/// <summary>Overridable economy formulas. Assign a subclass to <c>Game.Rules.Economy</c> from a C# mod.</summary>
public class EconomyRules
{
    /// <summary>Monthly gold income of a country (before upkeep).</summary>
    public virtual double MonthlyIncome(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_income", c, out var scripted)) return scripted;
        double income = 0;
        foreach (var s in c.OwnedStates) income += s.Tax * game.Modifiers.Factor(s, "tax") * s.ControlledShare(c);
        // occupied enemy land gives a little income too
        foreach (var l in game.World.Locations)
            if (l.Controller == c && l.Owner != null && l.Owner != c && l.State != null && l.State.Locations.Count > 0)
                income += l.State.Tax / l.State.Locations.Count * 0.25;
        income *= game.Data.Defines.IncomeMultiplier * (1 + c.Vars.Get("income_modifier") + game.Modifiers.Sum(c, "income"));
        return income;
    }

    /// <summary>Monthly upkeep of armies.</summary>
    public virtual double MonthlyExpenses(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_expenses", c, out var scripted)) return scripted;
        double cost = 0;
        foreach (var d in game.World.Divisions)
            if (d.Owner == c) cost += d.Template.Cost * game.Data.Defines.DivisionUpkeepFactor;
        return cost * (1 + c.Vars.Get("upkeep_modifier") + game.Modifiers.Sum(c, "upkeep"));
    }

    public virtual double MonthlyManpower(Game game, Country c)
    {
        if (game.ScriptValues.TryEval("country_monthly_manpower", c, out var scripted)) return scripted;
        double mp = 0;
        foreach (var s in c.OwnedStates) mp += s.Manpower * s.ControlledShare(c);
        return mp * game.Data.Defines.ManpowerMultiplier * (1 + c.Vars.Get("manpower_modifier") + game.Modifiers.Sum(c, "manpower"));
    }

    /// <summary>Can the country start building this template at this location right now?</summary>
    public virtual string? CannotRecruitReason(Game game, Country c, DivisionTemplate t, Location loc)
    {
        if (!string.IsNullOrEmpty(t.OwnerTag) && !string.Equals(t.OwnerTag, c.Tag, StringComparison.OrdinalIgnoreCase)) return "This template belongs to another country";
        if (loc.Controller != c || loc.Owner != c) return "You must own and control the location";
        if (loc.State?.Demilitarized == true) return "The state is demilitarized";
        foreach (var (u, _) in t.Units)
            if (!game.Tech.CanUse(c, u.Definition)) return $"{u.Name} needs a technology you do not have";
        if (game.Economy.GoldCosts && c.Gold < t.Cost) return $"Needs {t.Cost:0} gold";
        if (c.Manpower < t.Manpower) return $"Needs {t.Manpower:0} manpower";
        return null;
    }
}

/// <summary>Income, manpower, recruitment and reinforcement.</summary>
public sealed class EconomySystem
{
    private readonly Game _game;
    public EconomySystem(Game game) { _game = game; }

    /// <summary>Training costs gold: the economy feature is installed and the define recruit_gold_cost is not 'no'.</summary>
    public bool GoldCosts => _game.Features.Has(Burger.Core.Modding.FeatureSet.Economy) && _game.Data.Defines.GetBool("recruit_gold_cost", true);

    public void UpdateEstimates(Country c)
    {
        var r = _game.Rules.Economy;
        c.LastIncome = r.MonthlyIncome(_game, c);
        c.LastExpenses = r.MonthlyExpenses(_game, c);
        c.LastManpowerGain = r.MonthlyManpower(_game, c);
    }

    public void MonthlyTick()
    {
        var rules = _game.Rules.Economy;
        var w = _game.World;
        var income = new Dictionary<Country, double>();
        foreach (var c in w.Countries)
        {
            if (!c.Exists) continue;
            UpdateEstimates(c);
            income[c] = c.LastIncome;
        }
        // subjects pay their overlord
        foreach (var (subject, (overlord, type)) in w.Relations.Subjects)
        {
            if (!income.ContainsKey(subject) || !income.ContainsKey(overlord)) continue;
            double tribute = Math.Max(0, income[subject]) * type.IncomeShare;
            income[subject] -= tribute;
            income[overlord] += tribute;
        }
        bool gold = _game.Features.Has(Burger.Core.Modding.FeatureSet.Economy);
        bool military = _game.Features.Has(Burger.Core.Modding.FeatureSet.Military);
        foreach (var c in w.Countries)
        {
            if (!c.Exists) continue;
            if (gold) c.Gold += income[c] - c.LastExpenses;
            if (military) c.Manpower += c.LastManpowerGain;
        }
    }

    public void DailyTick()
    {
        var w = _game.World;
        foreach (var c in w.Countries)
        {
            for (int i = 0; i < c.Recruitment.Count; i++)
            {
                var o = c.Recruitment[i];
                o.HoursLeft -= 24;
                if (o.HoursLeft > 0) continue;
                var loc = o.Location.Controller == c ? o.Location : c.Capital?.Controller == c ? c.Capital : c.OwnedStates.SelectMany(s => s.Locations).FirstOrDefault(l => l.Controller == c);
                if (loc == null) { c.Recruitment.RemoveAt(i--); continue; }
                // production: a trained division deploys once enough equipment is in the stockpile
                double equipped = 1;
                if (_game.Production.Enabled && o.Template.Equipment.Count > 0)
                {
                    double min = Math.Clamp(_game.Data.Defines.Get("min_equipment_to_deploy", 0.5), 0, 1);
                    double share = _game.Production.AvailableShare(c, o.Template.Equipment);
                    if (share < min) { o.WaitingForEquipment = true; o.HoursLeft = 0; continue; }
                    equipped = _game.Production.TakeEquipment(c, o.Template.Equipment, Math.Min(1, share), false);
                }
                c.Recruitment.RemoveAt(i--);
                var d = _game.Warfare.CreateDivision(c, o.Template, loc);
                d.Strength = d.MaxStrength * Math.Max(0.05, equipped);
                _game.NotifyPlayer($"{d.Name} is ready in {loc.DisplayName}.", NotificationKind.Good, c);
            }
        }
        Reinforce();
    }

    private void Reinforce()
    {
        double rate = _game.Data.Defines.ReinforceRate;
        bool costs = _game.Features.Has(Burger.Core.Modding.FeatureSet.Military); // manpower
        foreach (var d in _game.World.Divisions)
        {
            if (d.InBattle || d.Strength >= d.MaxStrength) continue;
            double need = Math.Min(d.MaxStrength - d.Strength, d.MaxStrength * rate);
            if (_game.Features.HasSupply) need *= d.Supply; // badly supplied divisions are barely reinforced
            if (_game.Production.Enabled && d.Template.Equipment.Count > 0)
                need = d.MaxStrength * _game.Production.TakeEquipment(d.Owner, d.Template.Equipment, need / d.MaxStrength, false);
            if (need <= 0) continue;
            if (!costs) { d.Strength += need; continue; }
            double mpCost = need / d.MaxStrength * d.Template.Manpower;
            if (d.Owner.Manpower < mpCost)
            {
                if (d.Owner.Manpower <= 0) continue;
                need *= d.Owner.Manpower / mpCost;
                mpCost = d.Owner.Manpower;
            }
            d.Owner.Manpower -= mpCost;
            d.Strength += need;
        }
    }

    /// <summary>Starts training a division. Returns an error message or null on success.</summary>
    public string? Recruit(Country c, DivisionTemplate t, Location loc)
    {
        var reason = _game.Rules.Economy.CannotRecruitReason(_game, c, t, loc);
        if (reason != null) return reason;
        if (GoldCosts) c.Gold -= t.Cost;
        c.Manpower -= t.Manpower;
        int hours = Math.Max(24, t.BuildDays * 24);
        c.Recruitment.Add(new RecruitOrder { Template = t, Location = loc, HoursLeft = hours, TotalHours = hours });
        return null;
    }

    public void CancelRecruitment(Country c, RecruitOrder o)
    {
        if (!c.Recruitment.Remove(o)) return;
        double refund = o.TotalHours <= 0 ? 0 : (double)o.HoursLeft / o.TotalHours;
        if (GoldCosts) c.Gold += o.Template.Cost * refund;
        c.Manpower += o.Template.Manpower;
    }
}
