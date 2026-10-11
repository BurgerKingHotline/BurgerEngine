using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.AI;

/// <summary>
/// An AI personality from <c>common/ai_profiles/*.txt</c>: an archetype (aggressive, defensive, isolationist ...) or the profile
/// of a country. Numbers are multipliers around 1; tag maps say who a country fears, courts or wants.
/// </summary>
public sealed class AiProfileDef
{
    public string Key = "";
    public string Name = "";
    public string Parent = "";
    public bool Archetype;
    /// <summary>aggression, expansion, defense, isolation, diplomacy, risk, tolerance, colonial, land, navy, air, production ...</summary>
    public Dictionary<string, double> Traits = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hostile to these countries (tag = weight): war, claims, alliances against them.</summary>
    public Dictionary<string, double> Rivals = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Courts these countries (alliances, guarantees, pacts).</summary>
    public Dictionary<string, double> Friends = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Wants their land or submission (the historical ambitions).</summary>
    public Dictionary<string, double> Want = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The year an ambition awakens (tag = year): before it the want for that country counts for nothing, over the next year it grows to full (history, not a script: the AI still has to be ready).</summary>
    public Dictionary<string, double> Timing = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What awakens an ambition (tag = trigger, evaluated for the country): it grows to full over awaken_months (3) once the trigger holds, and fades when it stops. Replaces `timing` for that tag.</summary>
    public Dictionary<string, Node> Awaken = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hostility to ideologies (ideology key = weight).</summary>
    public Dictionary<string, double> IdeologyHostility = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Research priorities by technology category.</summary>
    public Dictionary<string, double> Research = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Countries = new();
    public string Desc = "";
}

/// <summary>What a country's AI is like right now: its generic archetype blended with its historical profile by the historical bias.</summary>
public sealed class AiPersonality
{
    public string Archetype = "";
    public AiProfileDef? Profile;
    public readonly Dictionary<string, double> Traits = new(StringComparer.OrdinalIgnoreCase);
    public double Trait(string key, double def = 1) => Traits.TryGetValue(key, out var v) ? v : def;
}

/// <summary>
/// Overridable rules of the AI profiles (the ai_profiles feature puts an editable copy in backend/AiProfiles.cs): which archetype a
/// country without a profile has, how strongly its history counts, and what it wants of every other country.
/// </summary>
public class AiProfileRules
{
    /// <summary>The archetype a country has when history says nothing: by its ideology and standing in the world.</summary>
    public virtual string GenericArchetype(Game game, Country c)
    {
        var d = game.Data.Defines;
        double strength = game.Diplomacy.Strength(c);
        var alive = game.World.Countries.Where(x => x.Exists).ToList();
        int rank = alive.Count(x => game.Diplomacy.Strength(x) > strength);
        bool great = rank < d.Get("ai_great_powers", 9);
        string ide = game.Politics.Enabled ? game.Politics.Of(c).Ruling : "";
        bool subject = game.World.Relations.OverlordOf(c) != null;
        if (subject) return "passive";
        return ide switch
        {
            "fascist" => great ? "expansionist" : "aggressive",
            "communist" => great ? "expansionist" : "defensive",
            "democratic" => great ? "status_quo" : "defensive",
            _ => great ? "opportunist" : (c.OwnedStates.Count <= 3 ? "passive" : "defensive"),
        };
    }

    /// <summary>
    /// How much the historical profile counts for this country: the historical bias (0-1) of the game. At 0 every country acts as its generic
    /// archetype and none of its rivals, friends or ambitions matter.
    /// </summary>
    public virtual double HistoryWeight(Game game, Country c) => Math.Clamp(game.HistoricalBias, 0, 1);

    /// <summary>
    /// Multiplier on the scripted desire for an action against a target, from the personality: war-like countries like war, the
    /// isolationist stay out of alliances, and nobody picks a fight it cannot win (see <see cref="Readiness"/>).
    /// </summary>
    public virtual double ActionFactor(Game game, Country c, string action, Country target, AiPersonality p)
    {
        switch (action)
        {
            case "declare_war":
                return p.Trait("aggression") * Math.Clamp(0.4 + p.Trait("expansion") * 0.6, 0.2, 2.5) * Readiness(game, c, target, p) / Math.Max(0.3, p.Trait("isolation")) * Restraint(game, c, target, p);
            case "offer_alliance":
                // (a country with a history allies with its friends, not with whoever the scripted weights fancy: Germany and the Netherlands did not ally)
                return p.Trait("diplomacy") / Math.Max(0.3, p.Trait("isolation")) * (p.Profile != null && p.Profile.Friends.GetValueOrDefault(target.Tag) <= 0 ? 1 - 0.95 * HistoryWeight(game, c) : 1);
            case "guarantee_independence":
            case "offer_non_aggression_pact":
                return p.Trait("diplomacy") / Math.Max(0.3, p.Trait("isolation"));
            case "break_alliance":
                // (a historical friend is not dropped over a falling-out of opinion: the Axis held together)
                return 1 / Math.Max(0.4, p.Trait("diplomacy")) * (p.Profile != null && p.Profile.Friends.GetValueOrDefault(target.Tag) >= 1 ? 1 - 0.95 * HistoryWeight(game, c) : 1);
            case "demand_state":
            case "demand_subjugation":
            case "fabricate_claim":
                return p.Trait("expansion") * p.Trait("aggression") * Restraint(game, c, target, p);
            default:
                return 1;
        }
    }

    /// <summary>
    /// Can the country win this war: its strength and its allies' against the target's and its allies' (and what else it is fighting).
    /// 0 when it would lose badly, 1 when it is ahead; countries with a taste for risk (trait risk) accept worse odds.
    /// </summary>
    public virtual double Readiness(Game game, Country c, Country target, AiPersonality p)
    {
        var D = game.Diplomacy;
        var R = game.World.Relations;
        double mine = D.Strength(c) + R.AlliesOf(c).Where(a => !game.World.AtWar(a, c)).Sum(a => D.Strength(a) * 0.6)
                      + R.SubjectsOf(c).Sum(a => D.Strength(a) * 0.5);
        // (the target's friends that are already fighting me are not new enemies: they are counted in what I am already facing)
        double theirs = D.Strength(target) + R.AlliesOf(target).Where(a => !game.World.AtWar(a, c)).Sum(a => D.Strength(a) * 0.6) + R.GuarantorsOf(target).Where(a => !game.World.AtWar(a, c)).Sum(a => D.Strength(a) * 0.5)
                        + R.SubjectsOf(target).Sum(a => D.Strength(a) * 0.5);
        if (game.World.WarsOf(c).Any()) mine *= 0.6; // a war already on
        double ratio = mine / Math.Max(1, theirs);
        double need = 1.25 / Math.Max(0.4, p.Trait("risk"));
        // history's fanatics gamble: a target they have wanted for long (an awakened ambition) lowers the odds they ask for
        if (p.Profile != null) need /= 1 + game.Data.Defines.Get("fanatic_readiness", 1.0) * Ambition(game, c, target, p) * HistoryWeight(game, c);
        // a country that is being starved of oil, steel or rubber gambles: the shortage lowers the odds it asks for (Japan in 1941)
        if (game.Commodities.Enabled && game.Commodities.NeedScore(c, target) > 0.05)
            need *= 1 - Math.Min(0.65, game.Commodities.Shortages(c).Sum(s => 1 - s.Satisfied) * 0.35);
        return Math.Clamp((ratio - need * 0.5) / need, 0, 1);
    }

    /// <summary>
    /// What a shortage of commodities adds to the desire for an action against a country that has deposits of what is missing: a reason to
    /// claim and to fight for its land (only where the war can be won, see <see cref="Readiness"/>), or to court it and get the goods
    /// by trade. Always counts: need is not history, so the historical bias does not scale it.
    /// </summary>
    public virtual double ResourceMotive(Game game, Country c, string action, Country target, AiPersonality p)
    {
        if (action != "declare_war" && action != "fabricate_claim" && action != "improve_relations" && action != "offer_alliance") return 0;
        double need = game.Commodities.NeedScore(c, target);
        if (need <= 0.02) return 0;
        double w = Math.Min(2.5, need);
        return action switch
        {
            "declare_war" => w * 0.5 * p.Trait("aggression") * p.Trait("expansion") * Readiness(game, c, target, p) * Restraint(game, c, target, p),
            "fabricate_claim" => w * 0.25 * p.Trait("expansion") * Readiness(game, c, target, p) * Restraint(game, c, target, p),
            _ => w * 0.3 * p.Trait("diplomacy"),
        };
    }

    /// <summary>
    /// How much two countries incline to ally, guarantee or make pacts, on top of the scripted acceptance: a profile that names the other as
    /// a friend (scaled by the historical bias), a shared rival, and kinship or enmity of their ideologies.
    /// </summary>
    public virtual double Affinity(Game game, Country actor, Country recipient)
    {
        double h = HistoryWeight(game, actor);
        var pa = game.AiProfiles.ProfileOf(actor);
        var pr = game.AiProfiles.ProfileOf(recipient);
        double v = 0;
        if (pr != null) { v += pr.Friends.GetValueOrDefault(actor.Tag) * 22 * h; v -= pr.Rivals.GetValueOrDefault(actor.Tag) * 20 * h; }
        if (pa != null)
        {
            v += pa.Friends.GetValueOrDefault(recipient.Tag) * 12 * h;
            if (pa != null && pa.Friends.GetValueOrDefault(recipient.Tag) > 0 || pr != null && pr.Friends.GetValueOrDefault(actor.Tag) > 0) v += CommonCause(game, actor, recipient) * 14 * h; // common enemies of friends (rivals and awakened ambitions)
        }
        if (game.Politics.Enabled)
        {
            string a = game.Politics.Of(actor).Ruling, b = game.Politics.Of(recipient).Ruling;
            if (a == b) v += 6;
            else if ((a == "fascist" && b == "communist") || (a == "communist" && b == "fascist")) v -= 20;
            else if ((a == "democratic" && (b == "fascist" || b == "communist")) || (b == "democratic" && (a == "fascist" || a == "communist"))) v -= 8;
        }
        return v;
    }

    /// <summary>
    /// Restraint of a country with a historical profile toward a target it has no reason for: scripted war, demand and claim weights count only
    /// (1 - restraint_strength, 0.85) times as much at full Historical Bias, unless history wants the target (an awakened ambition). A rivalry alone
    /// is not a reason to attack. Needs (resources) and the profile's own motives are not held back.
    /// </summary>
    public virtual double Restraint(Game game, Country c, Country target, AiPersonality p)
    {
        if (p.Profile == null) return 1;
        double h = HistoryWeight(game, c);
        if (h <= 0 || Ambition(game, c, target, p) >= 0.3) return 1;
        return 1 - game.Data.Defines.Get("restraint_strength", 1.0) * h;
    }

    /// <summary>
    /// A country with an awakened ambition (profile want, see Ambition) that is ready to fight for it (Readiness) goes for it: the best such target,
    /// by ambition times readiness, with a monthly chance of pursuit_chance (0.2) times that, scaled by the Historical Bias. The ambition is history's
    /// data; the readiness, the casus belli and every refusal of the world are the game's. Returns the action to use (declare war, else fabricate a claim).
    /// </summary>
    public virtual (ActionDef Action, Country Target)? Pursuit(Game game, Country c, AiPersonality p)
    {
        if (p.Profile == null) return null;
        double h = HistoryWeight(game, c);
        if (h <= 0) return null;
        if (game.World.WarsOf(c).Count() >= (int)Math.Min(game.Data.Defines.Get("pursuit_max_wars", 5), p.Trait("max_wars", 99))) return null;
        var war = game.Data.DiplomaticActions.GetValueOrDefault("declare_war");
        var claim = game.Data.DiplomaticActions.GetValueOrDefault("fabricate_claim");
        Country? best = null; double bestScore = 0;
        foreach (var kv in p.Profile.Want)
        {
            var t = game.World.GetCountry(kv.Key);
            if (t == null || !t.Exists || t == c || game.World.AtWar(c, t) || game.World.Relations.AreAllied(c, t) || game.World.Relations.OverlordOf(t) == c) continue;
            if (Environment.GetEnvironmentVariable("BURGER_PURSUIT_LOG") is { } pl2 && pl2.Equals(c.Tag, StringComparison.OrdinalIgnoreCase) && game.World.Date.Day == 1 && Ambition(game, c, t, p) >= 1)
                Console.WriteLine($"  PURSUIT-TARGET {game.World.Date.ToDisplay()} {c.Tag}->{t.Tag}: ambition {Ambition(game, c, t, p):0.0} readiness {Readiness(game, c, t, p):0.00} war {(war == null ? "-" : game.Diplomacy.CanUse(war, c, t).ToString())} reason {game.Rules.Diplomacy.CannotDeclareWarReason(game, c, t, null)}");
            bool pact = game.World.Relations.HasPact(c, t, game.World.Date);
            if (!pact && (war == null || !game.Diplomacy.CanUse(war, c, t)) && (claim == null || !game.Diplomacy.CanUse(claim, c, t))) continue; // (a target it cannot act on now - a truce, a subject - does not hold up the next)
            double amb = Ambition(game, c, t, p);
            if (amb < 1) continue;
            double score = amb * Readiness(game, c, t, p);
            if (score > bestScore) { bestScore = score; best = t; }
        }
        if (Environment.GetEnvironmentVariable("BURGER_PURSUIT_LOG") is { } pl && pl.Equals(c.Tag, StringComparison.OrdinalIgnoreCase) && game.World.Date.Day == 1)
            Console.WriteLine($"PURSUIT {game.World.Date.ToDisplay()} {c.Tag}: best {best?.Tag} score {bestScore:0.00} wars {game.World.WarsOf(c).Count()}");
        if (best == null || bestScore < 0.3) return null;
        if (game.Rng.NextDouble() > Math.Min(0.95, game.Data.Defines.Get("pursuit_chance", 0.9) * bestScore * h)) return null;
        // (a non-aggression pact is torn up first: Germany's with the Low Countries, Italy's with Greece)
        if (game.World.Relations.HasPact(c, best, game.World.Date) && game.Data.DiplomaticActions.GetValueOrDefault("break_non_aggression_pact") is { } breakPact && game.Diplomacy.CanUse(breakPact, c, best)) return (breakPact, best);
        if (war != null && game.Diplomacy.CanUse(war, c, best)) return (war, best);
        if (claim != null && game.Diplomacy.CanUse(claim, c, best)) return (claim, best);
        return null;
    }

    /// <summary>
    /// Chance (0-1) that a country with a historical profile answers a call to arms against <paramref name="enemy"/>: full if its history wants that
    /// enemy now (an awakened ambition), if the enemy is a direct neighbour with its own claim on it, or if it has no profile; otherwise only
    /// join_unprompted (0.1) at full Historical Bias - the alliances of the thirties were not honoured in wars nobody wanted.
    /// </summary>
    public virtual double JoinFactor(Game game, Country ally, Country enemy, Country caller, bool asAttacker = true)
    {
        var p = game.AiProfiles.PersonalityOf(ally);
        double h = HistoryWeight(game, ally);
        if (p.Profile == null || h <= 0) return 1;
        if (IsAwake(game, ally, enemy, p)) return 1;
        // a close friend that is attacked is defended
        // (a friend that is attacked is defended only by those whose history has taken up the quarrel - France did not fight for Czechoslovakia)
        // fighting for its own land: the enemy attacked its overlord or a country it holds
        if (game.World.AtWar(ally, enemy)) return 1;
        double floor = game.Data.Defines.Get("join_unprompted", 0.1);
        return 1 - h * (1 - floor);
    }

    /// <summary>The countries a profile is set against now: its rivals and the targets of its awakened ambitions (weight = the larger of the two).</summary>
    public virtual Dictionary<string, double> Foes(Game game, AiProfileDef prof, Country? owner = null)
    {
        var d = new Dictionary<string, double>(prof.Rivals, StringComparer.OrdinalIgnoreCase);
        double now = game.World.Date.Year + (game.World.Date.Month - 1) / 12.0;
        foreach (var (tag, w) in prof.Want)
        {
            double ww = prof.Awaken.TryGetValue(tag, out var trig) ? (owner == null ? 0 : w * game.AiProfiles.AwakenedShare(owner, tag, trig))
                      : prof.Timing.TryGetValue(tag, out var from) ? w * Math.Clamp(now - from, 0, 1) : w;
            if (ww > d.GetValueOrDefault(tag)) d[tag] = ww;
        }
        return d;
    }

    /// <summary>What two profiles have against the same countries (sum of the smaller weights): the common cause of an alliance.</summary>
    public virtual double CommonCause(Game game, Country a, Country b)
    {
        var pa = game.AiProfiles.ProfileOf(a);
        var pb = game.AiProfiles.ProfileOf(b);
        if (pa == null || pb == null) return 0;
        var fa = Foes(game, pa, a);
        double sum = 0;
        foreach (var (tag, w) in Foes(game, pb, b))
            if (fa.TryGetValue(tag, out var w2) && tag != a.Tag && tag != b.Tag) sum += Math.Min(w, w2);
        return sum;
    }

    /// <summary>True once an ambition on the target has awakened at all (its trigger holds, or its year has come) - whatever its growth since.</summary>
    public virtual bool IsAwake(Game game, Country c, Country target, AiPersonality p)
    {
        if (p.Profile == null || p.Profile.Want.GetValueOrDefault(target.Tag) <= 0) return false;
        if (p.Profile.Awaken.TryGetValue(target.Tag, out var trigger)) return game.AiProfiles.IsAwake(c, target.Tag, trigger);
        if (p.Profile.Timing.TryGetValue(target.Tag, out var from)) return game.World.Date.Year + (game.World.Date.Month - 1) / 12.0 >= from;
        return true;
    }

    /// <summary>
    /// A country with a historical profile courts the friends it has a common cause with: each month a chance (courtship_chance, 0.3) to propose an
    /// alliance to the best candidate (profile friend weight plus common cause), who accepts or not by the usual Affinity rules.
    /// </summary>
    public virtual (ActionDef Action, Country Target)? Courtship(Game game, Country c, AiPersonality p)
    {
        if (p.Profile == null) return null;
        double h = HistoryWeight(game, c);
        if (h <= 0) return null;
        var action = game.Data.DiplomaticActions.GetValueOrDefault("offer_alliance");
        if (action == null) return null;
        Country? best = null; double bestScore = 0;
        foreach (var t in game.World.Countries)
        {
            if (!t.Exists || t == c || game.World.Relations.AreAllied(c, t) || game.World.AtWar(c, t)) continue;
            double friend = p.Profile.Friends.GetValueOrDefault(t.Tag);
            if (friend <= 0) continue; // (a common cause only strengthens a friendship - Germany and the USSR shared enemies, not an alliance)
            double score = friend + CommonCause(game, c, t) * 0.5;
            if (score >= 1.5 && score > bestScore && game.Diplomacy.CanUse(action, c, t) && game.Diplomacy.Acceptance(action, c, t) > 0) { bestScore = score; best = t; }
        }
        if (best == null || game.Rng.NextDouble() > game.Data.Defines.Get("courtship_chance", 0.3) * h) return null;
        return (action, best);
    }

    /// <summary>How much the country's history wants this country's land: the profile's want, once its ambition has awakened (see AiProfileDef.Timing; not scaled by the bias).</summary>
    public virtual double Ambition(Game game, Country c, Country target, AiPersonality p)
    {
        if (p.Profile == null) return 0;
        double want = p.Profile.Want.GetValueOrDefault(target.Tag);
        if (want <= 0) return 0;
        if (p.Profile.Awaken.TryGetValue(target.Tag, out var trigger)) return want * game.AiProfiles.AwakenedShare(c, target.Tag, trigger);
        if (p.Profile.Timing.TryGetValue(target.Tag, out var from))
        {
            double now = game.World.Date.Year + (game.World.Date.Month - 1) / 12.0;
            want *= Math.Clamp(now - from, 0, 1);
        }
        return want;
    }

    /// <summary>What the profile adds to the desire for an action on top of the scripted weights: the historical motives of the country, scaled by the historical bias.</summary>
    public virtual double Motive(Game game, Country c, string action, Country target, AiPersonality p)
    {
        if (p.Profile == null) return 0;
        double h = HistoryWeight(game, c);
        if (h <= 0) return 0;
        var prof = p.Profile;
        double want = prof.Want.GetValueOrDefault(target.Tag), rival = prof.Rivals.GetValueOrDefault(target.Tag), friend = prof.Friends.GetValueOrDefault(target.Tag);
        want = Ambition(game, c, target, p);
        if (prof.IdeologyHostility.Count > 0 && game.Politics.Enabled) rival += prof.IdeologyHostility.GetValueOrDefault(game.Politics.Of(target).Ruling) * 0.5;
        if (want <= 0 && rival <= 0 && friend <= 0) return 0;
        switch (action)
        {
            case "declare_war": return h * want * Readiness(game, c, target, p) * p.Trait("aggression"); // (a rivalry alone is no reason for war)
            case "fabricate_claim": return h * want * 0.8;
            case "demand_state": return h * want * 0.5 * Readiness(game, c, target, p);
            case "demand_subjugation": return h * want * 0.3 * Readiness(game, c, target, p);
            case "offer_alliance": return h * (friend * 6 + (friend > 0 ? CommonCause(game, c, target) * 2 : 0));
            case "guarantee_independence": return h * friend * 2.5;
            case "offer_non_aggression_pact": return h * (friend * 2 + (rival > 0 && want <= 0 ? 0.8 : 0));
            case "break_alliance": return h * rival * 0.8;
            case "improve_relations": return h * friend * 0.4;
            case "insult": return h * rival * 0.15;
            default: return 0;
        }
    }
}

/// <summary>Loads the AI profiles and answers what a country's AI is like.</summary>
public sealed class AiProfileSystem
{
    private readonly Game _game;
    public readonly Dictionary<string, AiProfileDef> Defs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AiProfileDef> _byCountry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Country, (int Day, AiPersonality P)> _cache = new();

    public AiProfileSystem(Game game) { _game = game; }

    private readonly Dictionary<(Country, string), (int Since, int Checked, bool On, int LastTrue)> _awake = new();

    /// <summary>
    /// How far an ambition has awakened (0-1): the profile's awaken trigger for that target, evaluated for the country at most once a day, and the days
    /// it has held, over awaken_months (3).
    /// </summary>
    public bool IsAwake(Country c, string tag, Node trigger)
    {
        AwakenedShare(c, tag, trigger);
        return _awake.TryGetValue((c, tag), out var st) && st.On;
    }

    public double AwakenedShare(Country c, string tag, Node trigger)
    {
        int day = _game.World.Date.TotalDays;
        var key = (c, tag);
        _awake.TryGetValue(key, out var st);
        if (st.Checked != day || !_awake.ContainsKey(key))
        {
            bool now = _game.Script.EvalTrigger(trigger, _game.Context(c));
            int last = now ? day : st.LastTrue;
            // an ambition that has awakened stays awake for awaken_hold_months (24) after its cause lapses: a war that ends does not end the appetite
            bool on = now || (st.On && day - last < 30 * _game.Data.Defines.Get("awaken_hold_months", 24));
            if (Environment.GetEnvironmentVariable("BURGER_AMB_LOG") != null && on != st.On) Console.WriteLine($"AWAKEN {_game.World.Date.ToDisplay()} {c.Tag} -> {tag}: {(on ? "awake" : "asleep")}");
            if (on && !st.On) st.Since = day;
            st = (on ? st.Since : 0, day, on, last);
            _awake[key] = st;
        }
        if (!st.On) return 0;
        return Math.Clamp((day - st.Since) / (30.0 * _game.Data.Defines.Get("awaken_months", 3)), 0, 1);
    }

    public bool Enabled => _game.Features.Has(FeatureSet.AiProfiles) && Defs.Count > 0;
    private AiProfileRules Rules => _game.Rules.AiProfile;

    public void LoadDefinitions(GameData data)
    {
        Defs.Clear();
        _byCountry.Clear();
        foreach (var root in DataLoader.ParseFileOrFolder(data, "common/ai_profiles"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                var p = new AiProfileDef { Key = n.Key, Name = n.GetString("name") ?? Localisation.GetOrPretty(n.Key), Parent = n.GetString("parent", "")!, Archetype = n.GetBool("archetype"), Desc = n.GetString("desc", "")! };
                foreach (var kv in n.All)
                    if (kv.Children == null && kv.Value != null && double.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) p.Traits[kv.Key] = v;
                Fill(n.Get("rivals"), p.Rivals);
                Fill(n.Get("friends"), p.Friends);
                Fill(n.Get("want"), p.Want);
                Fill(n.Get("timing"), p.Timing);
                if (n.Get("awaken") is { } aw) foreach (var an in aw.All) if (an.Children != null) p.Awaken[an.Key] = an;
                Fill(n.Get("ideology_hostility"), p.IdeologyHostility);
                Fill(n.Get("research"), p.Research);
                foreach (var c in n.GetList("countries")) { p.Countries.Add(c); _byCountry[c] = p; }
                Defs[p.Key] = p;
            }
        foreach (var p in Defs.Values)
            if (p.Parent.Length > 0 && !Defs.ContainsKey(p.Parent)) Log.Error($"AI profile '{p.Key}': unknown parent '{p.Parent}'");
    }

    private static void Fill(Node? n, Dictionary<string, double> into)
    {
        if (n?.Children == null) return;
        foreach (var kv in n.All) if (kv.Value != null && double.TryParse(kv.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) into[kv.Key] = v;
    }

    /// <summary>The profile a country has by history: its own, else none.</summary>
    public AiProfileDef? ProfileOf(Country c) => _byCountry.GetValueOrDefault(c.Tag);

    private void Merge(AiProfileDef p, Dictionary<string, double> into)
    {
        if (p.Parent.Length > 0 && Defs.TryGetValue(p.Parent, out var par) && par != p) Merge(par, into);
        foreach (var kv in p.Traits) into[kv.Key] = kv.Value;
    }

    /// <summary>The personality of a country now (recomputed monthly): the generic archetype blended with the historical profile by the historical bias.</summary>
    public AiPersonality PersonalityOf(Country c)
    {
        int day = _game.World.Date.TotalDays;
        if (_cache.TryGetValue(c, out var e) && day - e.Day < 30) return e.P;
        var p = new AiPersonality();
        if (Enabled)
        {
            p.Archetype = Rules.GenericArchetype(_game, c);
            var generic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (Defs.TryGetValue(p.Archetype, out var ga)) Merge(ga, generic);
            var own = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            p.Profile = ProfileOf(c);
            if (p.Profile != null) Merge(p.Profile, own);
            double h = Rules.HistoryWeight(_game, c);
            foreach (var key in generic.Keys.Concat(own.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                double g = generic.GetValueOrDefault(key, 1);
                p.Traits[key] = own.TryGetValue(key, out var o) ? g + (o - g) * h : g;
            }
        }
        _cache[c] = (day, p);
        return p;
    }

    /// <summary>How much a country's AI cares about a research category (1 = average).</summary>
    public double CategoryWeight(Country c, string category)
    {
        if (!Enabled) return 1;
        var p = PersonalityOf(c);
        double generic = 1;
        double own = p.Profile != null && p.Profile.Research.TryGetValue(category, out var w) ? w : 1;
        return generic + (own - generic) * Rules.HistoryWeight(_game, c);
    }

    public void ForgetCache() => _cache.Clear();
}
