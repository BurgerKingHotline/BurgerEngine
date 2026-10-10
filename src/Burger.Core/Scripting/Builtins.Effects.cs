using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Scripting;

public static partial class Builtins
{
    private static Country? NeedCountry(ScriptContext ctx, Node n)
    {
        var c = C(ctx.This);
        if (c == null) Log.ErrorOnce("needc:" + n.Location, $"'{n.Key}' needs a country scope (current scope: {ctx.This?.ScopeType.ToString() ?? "none"})", n.Location);
        return c;
    }

    private static Character? NeedCharacter(ScriptContext ctx, Node n)
    {
        if (ctx.This is Character ch) return ch;
        Log.ErrorOnce("needch:" + n.Location, $"'{n.Key}' needs a character scope (current scope: {ctx.This?.ScopeType.ToString() ?? "none"})", n.Location);
        return null;
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void RegisterEffects(Game game)
    {
        var s = game.Script;
        var w = game.World;
        var R = w.Relations;

        // ------------------------------------------------------------------ generic
        s.RegisterEffect("set_flag", (ctx, n) => ctx.This?.Vars.SetFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("set_country_flag", (ctx, n) => C(ctx.This)?.Vars.SetFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("set_character_flag", (ctx, n) => ctx.This?.Vars.SetFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("clear_flag", (ctx, n) => ctx.This?.Vars.ClearFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("remove_flag", (ctx, n) => ctx.This?.Vars.ClearFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("clr_country_flag", (ctx, n) => C(ctx.This)?.Vars.ClearFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("set_global_flag", (ctx, n) => game.World.Global.SetFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("clear_global_flag", (ctx, n) => game.World.Global.ClearFlag(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("set_variable", (ctx, n) =>
        {
            var (name, value) = VarArgs(s, ctx, n);
            if (name != null) ctx.This?.Vars.Set(name, value);
        }, (ctx, n) => null);
        s.RegisterEffect("set_global_variable", (ctx, n) =>
        {
            var (name, value) = VarArgs(s, ctx, n);
            if (name != null) game.World.Global.Set(name, value);
        }, (ctx, n) => null);
        s.RegisterEffect("change_variable", (ctx, n) =>
        {
            string? name = n.GetString("name") ?? n.GetString("which");
            if (name == null || ctx.This == null) return;
            double v = ctx.This.Vars.Get(name);
            if (n.Has("add")) v += s.ResolveNumber(n.GetString("add"), ctx);
            if (n.Has("value")) v += s.ResolveNumber(n.GetString("value"), ctx);
            if (n.Has("subtract")) v -= s.ResolveNumber(n.GetString("subtract"), ctx);
            if (n.Has("multiply")) v *= s.ResolveNumber(n.GetString("multiply"), ctx);
            if (n.Has("divide")) { double dv = s.ResolveNumber(n.GetString("divide"), ctx); if (dv != 0) v /= dv; }
            if (n.Has("min")) v = Math.Max(v, s.ResolveNumber(n.GetString("min"), ctx));
            if (n.Has("max")) v = Math.Min(v, s.ResolveNumber(n.GetString("max"), ctx));
            ctx.This.Vars.Set(name, v);
        }, (ctx, n) => null);
        s.RegisterEffect("change_global_variable", (ctx, n) =>
        {
            string? name = n.GetString("name");
            if (name == null) return;
            game.World.Global.Set(name, game.World.Global.Get(name) + s.ResolveNumber(n.GetString("add") ?? n.GetString("value"), ctx));
        }, (ctx, n) => null);
        s.RegisterEffect("remove_variable", (ctx, n) => ctx.This?.Vars.Vars.Remove(n.Value ?? ""), (ctx, n) => null);
        s.RegisterEffect("save_scope_as", (ctx, n) => ctx.Scopes[n.Value ?? "saved"] = ctx.This, (ctx, n) => null);
        s.RegisterEffect("save_event_target_as", (ctx, n) => ctx.Scopes[n.Value ?? "saved"] = ctx.This, (ctx, n) => null);
        s.RegisterEffect("log", (ctx, n) => Log.Info($"[script] {game.Events.FormatText(n.Value ?? "", ctx)}", n.Location), (ctx, n) => null);
        s.RegisterEffect("notify", (ctx, n) =>
        {
            var c = C(ctx.This);
            if (c == null || c.IsPlayer) game.Notify(game.Events.FormatText(n.Value ?? n.GetString("text") ?? "", ctx), NotificationKind.Info);
        }, (ctx, n) => null);

        // ------------------------------------------------------------------ events
        EffectFn fireEvent = (ctx, n) =>
        {
            string? id = n.Value ?? n.GetString("id");
            if (id == null) { Log.ErrorOnce("ev:" + n.Location, $"{n.Key} needs an id", n.Location); return; }
            int days = n.Value == null ? (int)s.ResolveNumber(n.GetString("days") ?? "0", ctx) + 30 * (int)s.ResolveNumber(n.GetString("months") ?? "0", ctx) : 0;
            IScope? root = ctx.This;
            var def = game.Events.Get(id);
            if (def != null && def.Kind == EventKind.Country) root = C(ctx.This);
            if (def != null && def.Kind == EventKind.Character && root is Country cc) root = cc.Ruler;
            game.Events.FireEvent(id, root, ctx.Scopes, days, n.Location);
        };
        s.RegisterEffect("country_event", fireEvent, (ctx, n) => null);
        s.RegisterEffect("character_event", fireEvent, (ctx, n) => null);
        s.RegisterEffect("trigger_event", fireEvent, (ctx, n) => null);

        // ------------------------------------------------------------------ country: economy & identity
        s.RegisterEffect("add_gold", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.Gold += s.ResolveNumber(n.Value, ctx); },
            (ctx, n) => $"Gold: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("add_treasury", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.Gold += s.ResolveNumber(n.Value, ctx); },
            (ctx, n) => $"Gold: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("add_manpower", (ctx, n) =>
        {
            if (ctx.This is State st) st.Manpower += s.ResolveNumber(n.Value, ctx);
            else if (NeedCountry(ctx, n) is { } c) c.Manpower += s.ResolveNumber(n.Value, ctx);
        }, (ctx, n) => $"Manpower: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("set_country_name", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) { c.Name = n.Value != null ? game.Events.FormatText(n.Value, ctx) : c.DefaultName; game.RaiseMapChanged(); } },
            (ctx, n) => $"Country is renamed to {Localisation.Get(n.Value ?? "")}");
        s.RegisterEffect("reset_country_name", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.Name = c.DefaultName; }, (ctx, n) => "Country name is reset");
        s.RegisterEffect("set_country_color", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is not { } c) return;
            if (Rgb.TryParse(n, out var col)) { c.Color = col; game.RaiseMapChanged(); }
            else Log.ErrorOnce("color:" + n.Location, "set_country_color needs a color like { 255 0 0 } or \"FF0000\"", n.Location);
        }, (ctx, n) => "Country color changes");
        s.RegisterEffect("reset_country_color", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) { c.Color = c.DefaultColor; game.RaiseMapChanged(); } }, (ctx, n) => "Country color is reset");
        EffectFn setFlagImage = (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.FlagPath = n.Value ?? c.DefaultFlagPath; };
        s.RegisterEffect("set_flag_image", setFlagImage, (ctx, n) => "Country flag changes");
        s.RegisterEffect("set_country_banner", setFlagImage, (ctx, n) => "Country flag changes");
        s.RegisterEffect("reset_flag_image", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.FlagPath = c.DefaultFlagPath; }, (ctx, n) => "Country flag is reset");
        s.RegisterEffect("set_government", (ctx, n) => { if (NeedCountry(ctx, n) is { } c) c.Government = n.Value ?? ""; },
            (ctx, n) => $"Government becomes {Localisation.GetOrPretty(n.Value ?? "")}");
        s.RegisterEffect("set_capital", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is not { } c) return;
            var l = LocationArg(game, ctx, n.Value);
            if (l == null) { Log.ErrorOnce("cap:" + n.Location, $"Unknown location '{n.Value}'", n.Location); return; }
            c.Capital = l;
        }, (ctx, n) => $"Capital moves to {LocationArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");

        // ------------------------------------------------------------------ diplomacy
        s.RegisterEffect("add_opinion", (ctx, n) =>
        {
            var from = NeedCountry(ctx, n);
            var to = s.ArgCountry(n, ctx);
            if (from == null || to == null) return;
            double value = s.ResolveNumber(n.GetString("value") ?? n.GetString("modifier"), ctx);
            double decay = n.Has("decay") ? s.ResolveNumber(n.GetString("decay"), ctx) : -1;
            int? days = n.Has("days") ? (int)s.ResolveNumber(n.GetString("days"), ctx) : n.Has("years") ? (int)(365 * s.ResolveNumber(n.GetString("years"), ctx)) : null;
            game.Diplomacy.AddOpinion(from, to, n.GetString("name") ?? "Opinion", value, days.HasValue && !n.Has("decay") ? 0 : decay, days);
        }, (ctx, n) => $"Opinion of {s.ArgScope(n, ctx)?.DisplayName}: {ScriptEngine.FormatSigned(s.ResolveNumber(n.GetString("value") ?? n.GetString("modifier"), ctx))}");
        s.RegisterEffect("reverse_add_opinion", (ctx, n) =>
        {
            var to = NeedCountry(ctx, n);
            var from = s.ArgCountry(n, ctx);
            if (from == null || to == null) return;
            double value = s.ResolveNumber(n.GetString("value") ?? n.GetString("modifier"), ctx);
            game.Diplomacy.AddOpinion(from, to, n.GetString("name") ?? "Opinion", value);
        }, (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName}'s opinion of us: {ScriptEngine.FormatSigned(s.ResolveNumber(n.GetString("value") ?? n.GetString("modifier"), ctx))}");
        s.RegisterEffect("declare_war", (ctx, n) =>
        {
            var a = NeedCountry(ctx, n);
            var d = s.ArgCountry(n, ctx);
            if (a == null || d == null) return;
            CasusBelli? cb = null;
            var cbKey = n.GetString("cb") ?? n.GetString("casus_belli");
            if (cbKey != null && !game.Data.CasusBelli.TryGetValue(cbKey, out cb)) Log.ErrorOnce("cb:" + n.Location, $"Unknown casus belli '{cbKey}'", n.Location);
            var goal = StateArg(game, ctx, n.GetString("state") ?? n.GetString("war_goal"));
            game.Diplomacy.DeclareWar(a, d, cb, goal, force: true);
        }, (ctx, n) => $"Declare war on {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("white_peace", (ctx, n) =>
        {
            var a = NeedCountry(ctx, n);
            var b = s.ArgCountry(n, ctx);
            if (a == null || b == null) return;
            var war = game.World.WarBetween(a, b);
            if (war == null) return;
            if (war.LeaderOf(a) == a && war.LeaderOf(b) == b) game.Diplomacy.EndWar(war);
            else game.Diplomacy.LeaveWar(war.LeaderOf(a) == a ? b : a, war);
        }, (ctx, n) => $"White peace with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("create_alliance", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) { R.SetAlliance(a, b, true); game.RaiseMapChanged(); } },
            (ctx, n) => $"Form an alliance with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("break_alliance", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) R.SetAlliance(a, b, false); },
            (ctx, n) => $"Break the alliance with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("grant_military_access", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) R.SetAccess(a, b, true); },
            (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName} gets military access");
        s.RegisterEffect("revoke_military_access", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) { R.SetAccess(a, b, false); game.Warfare.EvacuateTrespassers(); }
        }, (ctx, n) => $"Military access of {s.ArgScope(n, ctx)?.DisplayName} is revoked");
        s.RegisterEffect("guarantee", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) R.SetGuarantee(a, b, true); },
            (ctx, n) => $"Guarantee the independence of {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("cancel_guarantee", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b) R.SetGuarantee(a, b, false); },
            (ctx, n) => $"Stop guaranteeing {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("make_subject", (ctx, n) =>
        {
            var overlord = NeedCountry(ctx, n);
            var subject = s.ArgCountry(n, ctx);
            if (overlord == null || subject == null || overlord == subject) return;
            string type = n.GetString("type") ?? game.Data.SubjectTypes.Keys.First();
            if (!game.Data.SubjectTypes.TryGetValue(type, out var st)) { Log.ErrorOnce("st:" + n.Location, $"Unknown subject type '{type}'", n.Location); return; }
            game.Diplomacy.MakeSubject(subject, overlord, st);
        }, (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName} becomes our subject");
        s.RegisterEffect("release_subject", (ctx, n) => { if (s.ArgCountry(n, ctx) is { } b) { R.ReleaseSubject(b); game.RaiseMapChanged(); } },
            (ctx, n) => $"Release {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("add_truce", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b)
                R.AddTruce(a, b, game.World.Date.AddDays((int)s.ResolveNumber(n.GetString("days") ?? "1825", ctx)));
        }, (ctx, n) => $"Truce with {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("annex_country", (ctx, n) => { if (NeedCountry(ctx, n) is { } a && s.ArgCountry(n, ctx) is { } b && a != b) game.Diplomacy.Annex(a, b); },
            (ctx, n) => $"Annex {s.ArgScope(n, ctx)?.DisplayName}");

        // ------------------------------------------------------------------ states, cores, claims
        s.RegisterEffect("add_claim", (ctx, n) => { if (NeedCountry(ctx, n) is { } c && StateArg(game, ctx, n.Value) is { } st) st.Claims.Add(c); },
            (ctx, n) => $"Gain a claim on {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterEffect("remove_claim", (ctx, n) => { if (NeedCountry(ctx, n) is { } c && StateArg(game, ctx, n.Value) is { } st) st.Claims.Remove(c); },
            (ctx, n) => $"Lose the claim on {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterEffect("add_core", (ctx, n) => { if (NeedCountry(ctx, n) is { } c && StateArg(game, ctx, n.Value) is { } st) st.Cores.Add(c); },
            (ctx, n) => $"Gain a core on {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterEffect("remove_core", (ctx, n) => { if (NeedCountry(ctx, n) is { } c && StateArg(game, ctx, n.Value) is { } st) st.Cores.Remove(c); },
            (ctx, n) => $"Lose the core on {StateArg(game, ctx, n.Value)?.DisplayName ?? n.Value}");
        s.RegisterEffect("add_core_of", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            if (st != null && s.ArgCountry(n, ctx) is { } c) st.Cores.Add(c);
        }, (ctx, n) => $"Becomes a core of {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("fabricate_claim", (ctx, n) =>
        {
            var c = NeedCountry(ctx, n);
            var target = s.ArgCountry(n, ctx);
            if (c == null || target == null) return;
            var neighbors = target.OwnedStates.Where(st => !st.Claims.Contains(c) && !st.Cores.Contains(c))
                .OrderByDescending(st => st.Locations.Any(l => l.Neighbors.Any(a => a.To.Owner == c))).ThenByDescending(st => st.Tax).ToList();
            var pick = neighbors.FirstOrDefault();
            if (pick != null)
            {
                pick.Claims.Add(c);
                game.NotifyPlayer($"{c.Name} now claims {pick.DisplayName}.", NotificationKind.Diplomacy, c, target);
            }
        }, (ctx, n) => $"Gain a claim on a state of {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("transfer_state", (ctx, n) =>
        {
            State? st;
            Country? to;
            if (n.Children != null)
            {
                st = StateArg(game, ctx, n.GetString("state"));
                to = s.ArgCountry(n, ctx);
            }
            else
            {
                st = StateArg(game, ctx, n.Value);
                to = NeedCountry(ctx, n);
            }
            if (st == null || to == null) { Log.ErrorOnce("ts:" + n.Location, "transfer_state needs a valid state and country", n.Location); return; }
            game.Diplomacy.TransferState(st, to);
        }, (ctx, n) => $"Gain {StateArg(game, ctx, n.Value ?? n.GetString("state"))?.DisplayName}");
        s.RegisterEffect("set_owner", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            var to = s.ArgCountry(n, ctx);
            if (st != null && to != null) game.Diplomacy.TransferState(st, to);
        }, (ctx, n) => $"Owner becomes {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("set_controller", (ctx, n) =>
        {
            var to = s.ArgCountry(n, ctx);
            if (to == null) return;
            if (ctx.This is Location l) game.Warfare.SetController(l, to);
            else if (ctx.This is State st) foreach (var x in st.Locations) game.Warfare.SetController(x, to);
        }, (ctx, n) => $"Controller becomes {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("set_state_name", (ctx, n) => { if (ctx.This is State st) st.NameOverride = n.Value; }, (ctx, n) => $"State is renamed to {Localisation.Get(n.Value ?? "")}");
        s.RegisterEffect("set_location_name", (ctx, n) => { if (ctx.This is Location l) l.NameOverride = n.Value; }, (ctx, n) => $"Renamed to {Localisation.Get(n.Value ?? "")}");
        s.RegisterEffect("add_tax", (ctx, n) => { if (ctx.This is State st) st.Tax += s.ResolveNumber(n.Value, ctx); },
            (ctx, n) => $"Tax: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("set_fort", (ctx, n) => { if (ctx.This is Location l) l.Fort = (int)s.ResolveNumber(n.Value, ctx); }, (ctx, n) => $"Fort level {n.Value}");
        s.RegisterEffect("add_building", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            if (st == null) return;
            string type = n.Value ?? n.GetString("type") ?? "";
            int lv = n.Value != null ? 1 : (int)s.ResolveNumber(n.GetString("level") ?? "1", ctx);
            st.Buildings[type] = st.Buildings.GetValueOrDefault(type) + lv;
        }, (ctx, n) => $"Build {Localisation.GetOrPretty(n.Value ?? n.GetString("type") ?? "")}");
        s.RegisterEffect("remove_building", (ctx, n) =>
        {
            var st = ctx.This as State ?? (ctx.This as Location)?.State;
            if (st == null) return;
            string type = n.Value ?? n.GetString("type") ?? "";
            int lv = n.Value != null ? 1 : (int)s.ResolveNumber(n.GetString("level") ?? "1", ctx);
            int left = st.Buildings.GetValueOrDefault(type) - lv;
            if (left > 0) st.Buildings[type] = left; else st.Buildings.Remove(type);
        }, (ctx, n) => $"Remove {Localisation.GetOrPretty(n.Value ?? n.GetString("type") ?? "")}");

        // ------------------------------------------------------------------ military
        s.RegisterEffect("create_division", (ctx, n) =>
        {
            var c = n.Has("owner") ? s.ArgCountry(n, ctx, "owner") : NeedCountry(ctx, n);
            if (c == null) return;
            string key = n.GetString("template") ?? n.Value ?? "";
            if (!game.Data.Templates.TryGetValue(key, out var t)) { Log.ErrorOnce("tmpl:" + n.Location, $"Unknown division template '{key}'", n.Location); return; }
            var loc = LocationArg(game, ctx, n.GetString("location")) ?? c.Capital;
            if (loc == null) return;
            int count = Math.Max(1, (int)s.ResolveNumber(n.GetString("count") ?? "1", ctx));
            for (int i = 0; i < count; i++) game.Warfare.CreateDivision(c, t, loc, count == 1 ? n.GetString("name") : null);
        }, (ctx, n) => $"Gain {n.GetString("count") ?? "1"} x {(game.Data.Templates.TryGetValue(n.GetString("template") ?? n.Value ?? "", out var t) ? t.Name : n.GetString("template"))}");

        // ------------------------------------------------------------------ characters
        s.RegisterEffect("create_character", (ctx, n) =>
        {
            var country = n.Has("country") ? null : C(ctx.This);
            var oldRulers = game.World.Countries.ToDictionary(c => c, c => c.Ruler);
            var ch = game.Characters.CreateFromNode(n, country, ctx);
            var save = n.GetString("save_scope_as");
            if (save != null) ctx.Scopes[save] = ch;
            if (ch.Country != null && n.GetBool("ruler"))
            {
                ch.Country.Ruler = oldRulers.GetValueOrDefault(ch.Country);
                game.Characters.SetRuler(ch.Country, ch);
            }
        }, (ctx, n) => "A new character appears");
        s.RegisterEffect("set_ruler", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is not { } c) return;
            if (s.ArgScope(n, ctx) is Character ch && ch.Alive) game.Characters.SetRuler(c, ch);
        }, (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName} becomes ruler");
        s.RegisterEffect("set_heir", (ctx, n) =>
        {
            if (NeedCountry(ctx, n) is not { } c) return;
            if (s.ArgScope(n, ctx) is Character ch && ch.Alive) c.Heir = ch;
        }, (ctx, n) => $"{s.ArgScope(n, ctx)?.DisplayName} becomes heir");
        s.RegisterEffect("add_trait", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            if (game.Data.Traits.TryGetValue(n.Value ?? "", out var t)) game.Characters.AddTrait(ch, t);
            else Log.ErrorOnce("trait:" + n.Location, $"Unknown trait '{n.Value}'", n.Location);
        }, (ctx, n) => $"Gains trait {(game.Data.Traits.TryGetValue(n.Value ?? "", out var t) ? Localisation.Get(t.Name) : n.Value)}");
        s.RegisterEffect("remove_trait", (ctx, n) => { if (NeedCharacter(ctx, n) is { } ch) ch.Traits.RemoveAll(t => string.Equals(t.Key, n.Value, StringComparison.OrdinalIgnoreCase)); },
            (ctx, n) => $"Loses trait {(game.Data.Traits.TryGetValue(n.Value ?? "", out var t) ? Localisation.Get(t.Name) : n.Value)}");
        s.RegisterEffect("add_stat", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            string stat = n.GetString("stat") ?? "";
            ch.Stats[stat] = ch.Stats.GetValueOrDefault(stat) + s.ResolveNumber(n.GetString("value"), ctx);
        }, (ctx, n) => $"{Localisation.GetOrPretty(n.GetString("stat") ?? "")}: {ScriptEngine.FormatSigned(s.ResolveNumber(n.GetString("value"), ctx))}");
        s.RegisterEffect("set_stat", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            ch.Stats[n.GetString("stat") ?? ""] = s.ResolveNumber(n.GetString("value"), ctx);
        }, (ctx, n) => $"{Localisation.GetOrPretty(n.GetString("stat") ?? "")} becomes {n.GetString("value")}");
        s.RegisterEffect("add_loyalty", (ctx, n) => { if (NeedCharacter(ctx, n) is { } ch) ch.Loyalty = Math.Clamp(ch.Loyalty + s.ResolveNumber(n.Value, ctx), 0, 100); },
            (ctx, n) => $"Loyalty: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        s.RegisterEffect("add_health", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            ch.Health = Math.Clamp(ch.Health + s.ResolveNumber(n.Value, ctx), 0, 100);
            if (ch.Health <= 0) game.Characters.Kill(ch, "ill health");
        }, (ctx, n) => $"Health: {ScriptEngine.FormatSigned(s.ResolveNumber(n.Value, ctx))}");
        EffectFn kill = (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            if (n.Value != null && !n.AsBool(true)) return;
            game.Characters.Kill(ch, n.GetString("reason") ?? n.GetString("death_reason") ?? "unknown causes");
        };
        s.RegisterEffect("kill_character", kill, (ctx, n) => $"{ctx.This?.DisplayName} dies");
        s.RegisterEffect("death", kill, (ctx, n) => $"{ctx.This?.DisplayName} dies");
        s.RegisterEffect("marry", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is { } a && s.ArgScope(n, ctx) is Character b) game.Characters.Marry(a, b);
        }, (ctx, n) => $"Marries {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("divorce", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is { } a && a.Spouse != null) { a.Spouse.Spouse = null; a.Spouse = null; }
        }, (ctx, n) => "Divorce");
        s.RegisterEffect("set_employer", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is not { } ch) return;
            var c = s.ArgCountry(n, ctx);
            if (ch.Country != null && ch.Country.Ruler == ch) return; // rulers stay
            if (ch.Country != null && ch.Country.Heir == ch) ch.Country.Heir = null;
            foreach (var army in game.World.Armies.Where(a => a.General == ch)) army.General = null;
            ch.Country = c;
        }, (ctx, n) => $"Moves to {s.ArgScope(n, ctx)?.DisplayName}");
        s.RegisterEffect("make_general", (ctx, n) => { if (NeedCharacter(ctx, n) is { } ch) ch.IsGeneral = n.AsBool(true); },
            (ctx, n) => $"{ctx.This?.DisplayName} becomes a general");
        s.RegisterEffect("set_first_name", (ctx, n) => { if (NeedCharacter(ctx, n) is { } ch) ch.FirstName = n.Value ?? ch.FirstName; }, (ctx, n) => null);
        s.RegisterEffect("set_family", (ctx, n) => { if (NeedCharacter(ctx, n) is { } ch) ch.Family = n.Value ?? ch.Family; }, (ctx, n) => null);
        s.RegisterEffect("give_birth", (ctx, n) =>
        {
            if (NeedCharacter(ctx, n) is { Female: true } ch) game.Characters.Birth(ch);
        }, (ctx, n) => "A child is born");
        _ = w;
    }

    private static (string? Name, double Value) VarArgs(ScriptEngine s, ScriptContext ctx, Node n)
    {
        // set_variable = { name = x value = 5 }   or   set_variable = { x = 5 }
        string? name = n.GetString("name") ?? n.GetString("which");
        if (name != null) return (name, s.ResolveNumber(n.GetString("value") ?? "0", ctx));
        var first = n.All.FirstOrDefault(c => c.Value != null);
        if (first == null) { Log.ErrorOnce("var:" + n.Location, $"{n.Key} needs  name = x value = n", n.Location); return (null, 0); }
        return (first.Key, s.ResolveNumber(first.Value, ctx));
    }
}
