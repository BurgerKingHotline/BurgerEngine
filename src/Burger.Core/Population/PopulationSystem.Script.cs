using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Population;

public sealed partial class PopulationSystem
{
    private void RegisterScript()
    {
        var s = _game.Script;
        static bool YesNo(Node n, bool v) => n.AsBool(true) ? v : !v;
        string CName(string? k) => Culture(k)?.Name ?? k ?? "?";
        string RName(string? k) => Religion(k)?.Name ?? k ?? "?";
        Country? Target(Node n, ScriptContext ctx) => s.TryResolveScope(n.Value ?? "", ctx, out var o) ? o as Country : null;

        // ------------------------------------------------------------------ triggers
        s.RegisterTrigger("culture", (ctx, n) => ctx.This switch
        {
            Country c => Enabled && string.Equals(Of(c).Culture, n.Value, StringComparison.OrdinalIgnoreCase),
            State st => Enabled && string.Equals(MajorityCulture(st), n.Value, StringComparison.OrdinalIgnoreCase),
            Location l when l.State != null => Enabled && string.Equals(MajorityCulture(l.State), n.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        }, (ctx, n) => $"Culture is {CName(n.Value)}");
        s.RegisterTrigger("culture_group", (ctx, n) =>
        {
            string? k = ctx.This switch { Country c => Of(c).Culture, State st => MajorityCulture(st), Location l when l.State != null => MajorityCulture(l.State), _ => null };
            return Enabled && Culture(k)?.Group.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase) == true;
        }, (ctx, n) => $"Culture group is {CultureGroups.FirstOrDefault(g => g.Key == n.Value)?.Name ?? n.Value}");
        s.RegisterTrigger("religion", (ctx, n) => ctx.This switch
        {
            Country c => ReligionEnabled && string.Equals(Of(c).Religion, n.Value, StringComparison.OrdinalIgnoreCase),
            State st => ReligionEnabled && string.Equals(MajorityReligion(st), n.Value, StringComparison.OrdinalIgnoreCase),
            Location l when l.State != null => ReligionEnabled && string.Equals(MajorityReligion(l.State), n.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        }, (ctx, n) => $"Religion is {RName(n.Value)}");
        s.RegisterTrigger("religion_group", (ctx, n) =>
        {
            string? k = ctx.This switch { Country c => Of(c).Religion, State st => MajorityReligion(st), Location l when l.State != null => MajorityReligion(l.State), _ => null };
            return ReligionEnabled && Religion(k)?.Group.Key.Equals(n.Value, StringComparison.OrdinalIgnoreCase) == true;
        }, (ctx, n) => $"Religion group is {ReligionGroups.FirstOrDefault(g => g.Key == n.Value)?.Name ?? n.Value}");
        s.RegisterTrigger("has_accepted_culture", (ctx, n) => ctx.This is Country c && Enabled && IsAccepted(c, n.Value ?? ""),
            (ctx, n) => $"{CName(n.Value)} culture is accepted");
        s.RegisterTrigger("same_religion", (ctx, n) => ReligionEnabled && ctx.This is Country a && Target(n, ctx) is { } b && Of(a).Religion == Of(b).Religion,
            (ctx, n) => $"Same religion as {Target(n, ctx)?.Name ?? n.Value}");
        s.RegisterTrigger("same_religion_group", (ctx, n) => ReligionEnabled && ctx.This is Country a && Target(n, ctx) is { } b && Religion(Of(a).Religion)?.Group is { } ga && ga == Religion(Of(b).Religion)?.Group,
            (ctx, n) => $"Same religious family as {Target(n, ctx)?.Name ?? n.Value}");
        s.RegisterTrigger("same_culture", (ctx, n) => Enabled && ctx.This is Country a && Target(n, ctx) is { } b && Of(a).Culture == Of(b).Culture,
            (ctx, n) => $"Same culture as {Target(n, ctx)?.Name ?? n.Value}");
        s.RegisterTrigger("has_fellow_believers_in", (ctx, n) => ctx.This is Country a && Target(n, ctx) is { } b && FellowBelieverStates(a, b).Count > 0,
            (ctx, n) => $"Our faithful live in {Target(n, ctx)?.Name ?? n.Value}'s neighbouring states");
        s.RegisterTrigger("is_colonizing", (ctx, n) => YesNo(n, ctx.This is Country c && ColoniesOf(c).Any()),
            (ctx, n) => n.AsBool(true) ? "Is settling a colony" : "Is not settling a colony");

        // ------------------------------------------------------------------ values
        s.RegisterValue("population", (ctx, x) => !Enabled ? null : x switch
        {
            Country c => TotalOf(c),
            State st => TotalOf(st),
            Location l when l.State != null => TotalOf(l.State),
            _ => null,
        });
        s.RegisterValue("accepted_share", (ctx, x) => !Enabled ? null : x switch
        {
            Country c => Shares(c).Accepted,
            State st when st.Owner != null && TotalOf(st) > 0 => Of(st).Pops.Where(p => IsAccepted(st.Owner, p.Culture)).Sum(p => p.Size) / TotalOf(st),
            _ => null,
        });
        s.RegisterValue("faithful_share", (ctx, x) => !ReligionEnabled ? null : x switch
        {
            Country c => Shares(c).Faithful,
            State st when st.Owner != null && TotalOf(st) > 0 => Of(st).Pops.Where(p => p.Religion == Of(st.Owner).Religion).Sum(p => p.Size) / TotalOf(st),
            _ => null,
        });
        s.RegisterValue("num_of_colonies", (ctx, x) => x is Country c ? ColoniesOf(c).Count() : null);

        // ------------------------------------------------------------------ effects
        s.RegisterEffect("add_pops", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            if (st == null || !Enabled) return;
            string culture = n.GetString("culture") ?? (st.Owner != null ? Of(st.Owner).Culture : null) ?? "";
            if (!CultureByKey.ContainsKey(culture)) { Log.ErrorOnce("addpops:" + culture, $"add_pops: unknown culture '{culture}'"); return; }
            string? religion = ReligionEnabled ? n.GetString("religion") ?? Culture(culture)?.Religion ?? (st.Owner != null ? Of(st.Owner).Religion : null) : null;
            string? type = ClassesEnabled ? n.GetString("type") ?? PopTypes.OrderByDescending(t => t.Share).First().Key : null;
            double size = s.ResolveNumber(n.GetString("size"), ctx);
            var sp = Of(st);
            InvalidateMajorities();
            if (size >= 0) sp.Pops.Add(new Pop { Culture = culture, Religion = religion, Type = type, Size = size });
            else // negative: remove people of that culture
            {
                double remove = -size;
                foreach (var p in sp.Pops.Where(p => p.Culture == culture).ToList()) { double r = Math.Min(p.Size, remove); p.Size -= r; remove -= r; }
            }
            Merge(sp);
        }, (ctx, n) => $"{ScriptEngine.FormatSigned(s.ResolveNumber(n.GetString("size"), ctx))} people of {CName(n.GetString("culture"))} culture");
        s.RegisterEffect("set_culture", (ctx, n) => { if (ctx.This is Country c && Enabled && CultureByKey.ContainsKey(n.Value ?? "")) { Of(c).Culture = n.Value; Invalidate(); } },
            (ctx, n) => $"Our culture becomes {CName(n.Value)}");
        s.RegisterEffect("add_accepted_culture", (ctx, n) => { if (ctx.This is Country c && Enabled && CultureByKey.ContainsKey(n.Value ?? "")) { Of(c).Accepted.Add(n.Value!); Invalidate(); } },
            (ctx, n) => $"Accept {CName(n.Value)} culture");
        s.RegisterEffect("remove_accepted_culture", (ctx, n) => { if (ctx.This is Country c && Enabled) { Of(c).Accepted.Remove(n.Value ?? ""); Invalidate(); } },
            (ctx, n) => $"No longer accept {CName(n.Value)} culture");
        s.RegisterEffect("set_state_religion", (ctx, n) => { if (ctx.This is Country c && ReligionEnabled && ReligionByKey.ContainsKey(n.Value ?? "")) { Of(c).Religion = n.Value; Invalidate(); } },
            (ctx, n) => $"Our state religion becomes {RName(n.Value)}");
        s.RegisterEffect("convert_pops", (ctx, n) =>
        {
            // convert_pops = { religion = catholic share = 0.5 }   (state scope)
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            string? rel = n.Value ?? n.GetString("religion");
            if (st == null || !ReligionEnabled || rel == null || !ReligionByKey.ContainsKey(rel)) return;
            double share = n.Children != null && n.Has("share") ? Math.Clamp(s.ResolveNumber(n.GetString("share"), ctx), 0, 1) : 1;
            var sp = Of(st);
            foreach (var p in sp.Pops.Where(p => p.Religion != rel).ToList())
            {
                double moved = p.Size * share;
                p.Size -= moved;
                sp.Pops.Add(new Pop { Culture = p.Culture, Religion = rel, Type = p.Type, Size = moved });
            }
            Merge(sp);
        }, (ctx, n) => $"People convert to {RName(n.Value ?? n.GetString("religion"))}");
        s.RegisterEffect("assimilate_pops", (ctx, n) =>
        {
            // assimilate_pops = { culture = german share = 0.5 }   (state scope)
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            string? cul = n.Value ?? n.GetString("culture");
            if (st == null || !Enabled || cul == null || !CultureByKey.ContainsKey(cul)) return;
            double share = n.Children != null && n.Has("share") ? Math.Clamp(s.ResolveNumber(n.GetString("share"), ctx), 0, 1) : 1;
            var sp = Of(st);
            foreach (var p in sp.Pops.Where(p => p.Culture != cul).ToList())
            {
                double moved = p.Size * share;
                p.Size -= moved;
                sp.Pops.Add(new Pop { Culture = cul, Religion = p.Religion, Type = p.Type, Size = moved });
            }
            Merge(sp);
        }, (ctx, n) => $"People take {CName(n.Value ?? n.GetString("culture"))} culture");
        s.RegisterEffect("add_religious_claims", (ctx, n) =>
        {
            if (ctx.This is not Country c || Target(n, ctx) is not { } t) return;
            foreach (var st in FellowBelieverStates(c, t)) { st.Claims.Add(c); ClaimsMade++; }
        }, (ctx, n) => $"Claim {Target(n, ctx)?.Name ?? n.Value}'s states where our faithful are the majority");
    }

    /// <summary>Checks script references of the population features (called with the other script checks).</summary>
    public void Validate()
    {
        foreach (var b in _game.Data.BuildingTypes.Values)
            foreach (var key in new[] { "modifier", "country_modifier" })
                foreach (var x in b.Definition.Get(key)?.All ?? Enumerable.Empty<Node>())
                    if (x.Value == null || !Node.TryParseDouble(x.Value, out _)) Log.Warn($"Building '{b.Key}': {key} '{x.Key}' should be a number", x.Location);
    }
}
