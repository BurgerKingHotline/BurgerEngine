using System.Globalization;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Core.Save;

/// <summary>
/// Saves and loads games in the same readable text format as the data files (like Paradox saves).
/// A save stores only what changes during play; definitions are re-read from the game folder on load.
/// </summary>
public static class SaveGame
{
    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static void Save(Game game, string path)
    {
        var w = game.World;
        var root = Node.Root(path);

        var meta = root.AddBlock("meta");
        meta.Add("version", "1");
        meta.Add("game", game.Data.Info.Name, true);
        meta.Add("date", w.Date.ToScript());
        meta.Add("hour", I(w.Date.Hour));
        if (w.Player != null) meta.Add("player", w.Player.Tag);
        meta.Add("combat_model", game.Data.Defines.CombatModel);
        var ids = meta.AddBlock("next_ids");
        ids.Add("character", I(w.NextCharacterId));
        ids.Add("division", I(w.NextDivisionId));
        ids.Add("army", I(w.NextArmyId));
        ids.Add("war", I(w.NextWarId));
        ids.Add("battle", I(w.NextBattleId));
        WriteVars(meta.AddBlock("global"), w.Global);
        var fired = meta.AddBlock("fired_once");
        foreach (var f in w.FiredOnceEvents) fired.AddItem(f);
        var cds = meta.AddBlock("cooldowns");
        foreach (var kv in w.ActionCooldowns) cds.Add(kv.Key, I(kv.Value)).KeyQuoted = true;

        var countries = root.AddBlock("countries");
        foreach (var c in w.Countries)
        {
            var n = countries.AddBlock(c.Tag);
            if (c.Dynamic)
            {
                n.Add("dynamic", "yes");
                if (c.Origin != null) n.Add("origin", c.Origin);
                n.Add("playable", c.Playable ? "yes" : "no");
                var nl = n.AddBlock("names");
                var male = nl.AddBlock("male"); foreach (var x in c.Names.Male) male.AddItem(x);
                var female = nl.AddBlock("female"); foreach (var x in c.Names.Female) female.AddItem(x);
                var family = nl.AddBlock("family"); foreach (var x in c.Names.Family) family.AddItem(x);
            }
            n.Add("name", c.RawName, true);
            n.Add(c.Color.ToNode("color"));
            n.Add("flag", c.FlagPath, true);
            if (c.Capital != null) n.Add("capital", c.Capital.Key);
            n.Add("gold", F(c.Gold));
            n.Add("manpower", F(c.Manpower));
            n.Add("government", c.Government, true);
            if (c.Ruler != null) n.Add("ruler", c.Ruler.Key);
            if (c.Heir != null) n.Add("heir", c.Heir.Key);
            WriteVars(n.AddBlock("vars"), c.Vars);
            foreach (var o in c.Recruitment)
            {
                var r = n.AddBlock("recruit");
                r.Add("template", o.Template.Key);
                r.Add("location", o.Location.Key);
                r.Add("hours", I(o.HoursLeft));
                r.Add("total", I(o.TotalHours));
            }
        }

        var states = root.AddBlock("states");
        foreach (var s in w.States)
        {
            var n = states.AddBlock(s.Key);
            if (s.Owner != null) n.Add("owner", s.Owner.Tag);
            var cores = n.AddBlock("cores");
            foreach (var c in s.Cores) cores.AddItem(c.Tag);
            var claims = n.AddBlock("claims");
            foreach (var c in s.Claims) claims.AddItem(c.Tag);
            n.Add("manpower", F(s.Manpower));
            n.Add("tax", F(s.Tax));
            if (s.NameOverride != null) n.Add("name", s.NameOverride, true);
            var b = n.AddBlock("buildings");
            foreach (var kv in s.Buildings) b.Add(kv.Key, I(kv.Value));
            WriteVars(n.AddBlock("vars"), s.Vars);
        }

        var locations = root.AddBlock("locations");
        foreach (var l in w.Locations)
        {
            bool interesting = l.Controller != l.Owner || l.Fort != 0 || l.SiegeProgress > 0 || l.NameOverride != null || l.Vars.Flags.Count > 0 || l.Vars.Vars.Count > 0;
            if (!interesting) continue;
            var n = locations.AddBlock(l.Key);
            n.Add("controller", l.Controller?.Tag ?? "none");
            n.Add("fort", I(l.Fort));
            if (l.SiegeProgress > 0 && l.Sieger != null) { n.Add("siege", F(l.SiegeProgress)); n.Add("sieger", l.Sieger.Tag); }
            if (l.NameOverride != null) n.Add("name", l.NameOverride, true);
            WriteVars(n.AddBlock("vars"), l.Vars);
        }

        var chars = root.AddBlock("characters");
        foreach (var ch in w.Characters)
        {
            var n = chars.AddBlock("character");
            n.Add("num", I(ch.Id));
            n.Add("id", ch.Key);
            n.Add("first_name", ch.FirstName, true);
            n.Add("family", ch.Family, true);
            n.Add("female", ch.Female ? "yes" : "no");
            n.Add("birth", ch.BirthDate.ToScript());
            if (ch.DeathDate != null) { n.Add("death", ch.DeathDate.Value.ToScript()); n.Add("death_reason", ch.DeathReason, true); }
            if (ch.Country != null) n.Add("country", ch.Country.Tag);
            var traits = n.AddBlock("traits");
            foreach (var t in ch.Traits) traits.AddItem(t.Key);
            var stats = n.AddBlock("stats");
            foreach (var kv in ch.Stats) stats.Add(kv.Key, F(kv.Value));
            n.Add("loyalty", F(ch.Loyalty));
            n.Add("health", F(ch.Health));
            if (ch.Father != null) n.Add("father", ch.Father.Key);
            if (ch.Mother != null) n.Add("mother", ch.Mother.Key);
            if (ch.Spouse != null) n.Add("spouse", ch.Spouse.Key);
            if (ch.Portrait != null) n.Add("portrait", ch.Portrait, true);
            n.Add("general", ch.IsGeneral ? "yes" : "no");
            WriteVars(n.AddBlock("vars"), ch.Vars);
        }

        var armies = root.AddBlock("armies");
        foreach (var a in w.Armies)
        {
            var n = armies.AddBlock("army");
            n.Add("num", I(a.Id));
            n.Add("name", a.Name, true);
            n.Add("owner", a.Owner.Tag);
            if (a.General != null) n.Add("general", a.General.Key);
            if (a.FrontEnemy != null) n.Add("front", a.FrontEnemy.Tag);
            n.Add("mode", a.Mode.ToString());
            if (a.MoveTarget != null) n.Add("move_target", a.MoveTarget.Key);
            if (a.Objective != null) n.Add("objective", a.Objective.Key);
            if (a.FrontLimit.Count > 0) { var fl = n.AddBlock("front_line"); foreach (var l in a.FrontLimit) fl.AddItem(l.Key); }
            if (a.Fallback.Count > 0) { var fb = n.AddBlock("fallback"); foreach (var l in a.Fallback) fb.AddItem(l.Key); }
        }

        var divs = root.AddBlock("divisions");
        foreach (var d in w.Divisions)
        {
            var n = divs.AddBlock("division");
            n.Add("num", I(d.Id));
            n.Add("name", d.Name, true);
            n.Add("owner", d.Owner.Tag);
            n.Add("template", d.Template.Key);
            n.Add("location", d.Location.Key);
            n.Add("strength", F(d.Strength));
            n.Add("org", F(d.Org));
            n.Add("experience", F(d.Experience));
            if (d.Army != null) n.Add("army", I(d.Army.Id));
            if (d.VolunteerFrom != null) n.Add("volunteer_from", d.VolunteerFrom.Tag);
            if (d.Path.Count > 0)
            {
                var p = n.AddBlock("path");
                foreach (var l in d.Path) p.AddItem(l.Key);
                n.Add("progress", F(d.MoveProgress));
            }
            if (d.Retreating) n.Add("retreating", "yes");
            if (d.ManualOrder) n.Add("manual", "yes");
            if (d.FrontPost != null) n.Add("front_post", d.FrontPost.Key);
        }

        var wars = root.AddBlock("wars");
        foreach (var war in w.Wars)
        {
            var n = wars.AddBlock("war");
            n.Add("num", I(war.Id));
            n.Add("name", war.Name, true);
            n.Add("attacker_leader", war.AttackerLeader.Tag);
            n.Add("defender_leader", war.DefenderLeader.Tag);
            var at = n.AddBlock("attackers");
            foreach (var c in war.Attackers) at.AddItem(c.Tag);
            var df = n.AddBlock("defenders");
            foreach (var c in war.Defenders) df.AddItem(c.Tag);
            if (war.CasusBelli != null) n.Add("cb", war.CasusBelli.Key);
            if (war.Goal != null) n.Add("goal", war.Goal.Key);
            n.Add("start", war.StartDate.ToScript());
            n.Add("battle_score", F(war.BattleScore));
            WriteVars(n.AddBlock("vars"), war.Vars);
        }

        var rel = root.AddBlock("relations");
        var R = w.Relations;
        foreach (var (a, b) in R.Alliances) { var n = rel.AddBlock("alliance"); n.Add("first", a.Tag); n.Add("second", b.Tag); }
        foreach (var (g, r) in R.MilitaryAccess) { var n = rel.AddBlock("military_access"); n.Add("granter", g.Tag); n.Add("receiver", r.Tag); }
        foreach (var (g, d) in R.Guarantees) { var n = rel.AddBlock("guarantee"); n.Add("guarantor", g.Tag); n.Add("guaranteed", d.Tag); }
        foreach (var kv in R.Subjects) { var n = rel.AddBlock("subject"); n.Add("subject", kv.Key.Tag); n.Add("overlord", kv.Value.Overlord.Tag); n.Add("type", kv.Value.Type.Key); }
        foreach (var kv in R.Truces) { var n = rel.AddBlock("truce"); n.Add("first", kv.Key.Item1.Tag); n.Add("second", kv.Key.Item2.Tag); n.Add("until", kv.Value.ToScript()); }
        foreach (var kv in R.Pacts) { var n = rel.AddBlock("pact"); n.Add("first", kv.Key.Item1.Tag); n.Add("second", kv.Key.Item2.Tag); n.Add("until", kv.Value.ToScript()); }
        foreach (var kv in R.Opinions)
            foreach (var m in kv.Value)
            {
                var n = rel.AddBlock("opinion");
                n.Add("from", kv.Key.From.Tag);
                n.Add("to", kv.Key.To.Tag);
                n.Add("name", m.Name, true);
                n.Add("value", F(m.Value));
                n.Add("decay", F(m.DecayPerMonth));
                if (m.Expires != null) n.Add("expires", m.Expires.Value.ToScript());
            }

        var mods = root.AddBlock("modifiers");
        foreach (var (scope, list) in game.Modifiers.Active)
            foreach (var m in list)
            {
                string sref = ScopeRef(scope);
                if (sref.Length == 0) continue;
                var n = mods.AddBlock("modifier");
                n.Add("scope", sref, true);
                n.Add("name", m.Def.Key);
                if (m.Expires != null) n.Add("expires", m.Expires.Value.ToScript());
                if (m.Auto) n.Add("auto", "yes");
            }

        var sched = root.AddBlock("scheduled_events");
        foreach (var e in game.Events.Scheduled)
        {
            if (e.Root == null) continue;
            var n = sched.AddBlock("event");
            n.Add("id", e.Def.Id);
            n.Add("when", e.When.ToScript());
            n.Add("scope", ScopeRef(e.Root), true);
        }

        foreach (var sec in game.SaveSections.All)
        {
            try { sec.Write(root.AddBlock("x_" + sec.Name)); }
            catch (Exception ex) { Log.Error($"Save section '{sec.Name}' failed: {ex.Message}"); }
        }

        ScriptWriter.WriteFile(path, root, $"BurgerEngine save - {game.Data.Info.Name} - {w.Date.ToDisplay()}");
    }

    private static string ScopeRef(IScope s) => s switch
    {
        Country c => "c:" + c.Tag,
        Character ch => "character:" + ch.Key,
        State st => "state:" + st.Key,
        Location l => "location:" + l.Key,
        _ => "",
    };

    private static void WriteVars(Node n, VarStore v)
    {
        var flags = n.AddBlock("flags");
        foreach (var f in v.Flags) flags.AddItem(f);
        foreach (var kv in v.Vars) n.Add(kv.Key, F(kv.Value));
    }

    private static void ReadVars(Node? n, VarStore v)
    {
        v.Flags.Clear();
        v.Vars.Clear();
        if (n == null) return;
        foreach (var f in n.GetList("flags")) v.Flags.Add(f);
        foreach (var c in n.All)
            if (c.Value != null && Node.TryParseDouble(c.Value, out var d)) v.Vars[c.Key] = d;
    }

    /// <summary>Reads only the header of a save (for the load menu).</summary>
    public static (string Game, string Date, string Player) Peek(string path)
    {
        try
        {
            var root = ScriptParser.ParseFile(path);
            var meta = root.Get("meta");
            return (meta?.GetString("game") ?? "?", meta?.GetString("date") ?? "?", meta?.GetString("player") ?? "observer");
        }
        catch { return ("?", "?", "?"); }
    }

    /// <summary>Loads a save into a freshly loaded <see cref="Game"/> (call <see cref="Game.Load"/> first, not StartNewGame).</summary>
    public static void Load(Game game, string path)
    {
        var root = ScriptParser.ParseFile(path, Path.GetFileName(path));
        var w = game.World;
        var data = game.Data;
        var meta = root.Get("meta") ?? throw new InvalidDataException("Not a BurgerEngine save (no meta block)");

        // ---- reset runtime collections that are fully stored in the save
        w.Characters.Clear();
        w.CharacterByKey.Clear();
        w.Divisions.Clear();
        game.Warfare.RebuildIndex();
        w.Armies.Clear();
        w.Battles.Clear();
        w.Wars.Clear();
        w.Relations.Alliances.Clear();
        w.Relations.MilitaryAccess.Clear();
        w.Relations.Guarantees.Clear();
        w.Relations.Subjects.Clear();
        w.Relations.Truces.Clear();
        w.Relations.Pacts.Clear();
        w.Relations.Opinions.Clear();
        w.ActionCooldowns.Clear();
        w.FiredOnceEvents.Clear();
        game.Events.Scheduled.Clear();
        game.Events.PlayerQueue.Clear();
        game.PendingProposals.Clear();
        game.Notifications.Clear();
        foreach (var sec in game.SaveSections.All) sec.Reset?.Invoke();
        void ReadSections(bool early)
        {
            foreach (var sec in game.SaveSections.All.Where(s => s.Early == early))
            {
                var block = root.Get("x_" + sec.Name);
                if (block == null) continue;
                try { sec.Read(block); }
                catch (Exception ex) { Log.Error($"Loading save section '{sec.Name}' failed: {ex.Message}"); }
            }
        }

        var date = GameDate.Parse(meta.GetString("date") ?? "", w.Date);
        w.Date = new GameDate(date.TotalDays, meta.GetInt("hour", 0));
        var cm = meta.GetString("combat_model");
        if (cm != null) data.Defines.CombatModel = cm;
        var ids = meta.Get("next_ids");
        ReadVars(meta.Get("global"), w.Global);
        foreach (var f in meta.GetList("fired_once")) w.FiredOnceEvents.Add(f);
        var cds = meta.Get("cooldowns");
        if (cds != null) foreach (var c in cds.All) if (c.Value != null) w.ActionCooldowns[c.Key] = (int)c.AsDouble();

        // ---- countries made during the game (civil war rebels...) must exist before anything refers to them
        foreach (var n in root.Get("countries")?.All ?? Enumerable.Empty<Node>())
        {
            if (!n.GetBool("dynamic") || w.GetCountry(n.Key) != null || !Country.IsValidTag(n.Key)) continue;
            Rgb.TryParse(n.Get("color"), out var dcol);
            var dc = w.CreateCountry(n.Key.ToUpperInvariant(), n.GetString("name", n.Key)!, dcol, n.GetString("flag", "")!, n.GetString("government", "")!);
            dc.Origin = n.GetString("origin");
            dc.Playable = n.GetBool("playable", false);
            var names = n.Get("names");
            if (names != null)
            {
                dc.Names.Male.AddRange(names.GetList("male"));
                dc.Names.Female.AddRange(names.GetList("female"));
                dc.Names.Family.AddRange(names.GetList("family"));
            }
        }

        ReadSections(early: true);

        // ---- characters (two passes: create, then link)
        var charNodes = root.Get("characters")?.GetAll("character").ToList() ?? new List<Node>();
        foreach (var n in charNodes)
        {
            var ch = new Character
            {
                Id = n.GetInt("num"),
                Key = n.GetString("id") ?? "char_" + n.GetInt("num"),
                FirstName = n.GetString("first_name", "")!,
                Family = n.GetString("family", "")!,
                Female = n.GetBool("female"),
                BirthDate = GameDate.Parse(n.GetString("birth") ?? "", w.Date),
                Country = w.GetCountry(n.GetString("country")),
                Loyalty = n.GetDouble("loyalty", 50),
                Health = n.GetDouble("health", 100),
                Portrait = n.GetString("portrait"),
                IsGeneral = n.GetBool("general"),
            };
            if (n.Has("death")) { ch.DeathDate = GameDate.Parse(n.GetString("death")!, w.Date); ch.DeathReason = n.GetString("death_reason", "")!; }
            foreach (var t in n.GetList("traits")) if (data.Traits.TryGetValue(t, out var tr)) ch.Traits.Add(tr);
            var stats = n.Get("stats");
            if (stats != null) foreach (var s in stats.All) if (s.Value != null) ch.Stats[s.Key] = s.AsDouble();
            ReadVars(n.Get("vars"), ch.Vars);
            w.Characters.Add(ch);
            w.CharacterByKey[ch.Key] = ch;
        }
        foreach (var n in charNodes)
        {
            var ch = w.GetCharacter(n.GetString("id"))!;
            ch.Father = w.GetCharacter(n.GetString("father"));
            ch.Mother = w.GetCharacter(n.GetString("mother"));
            ch.Spouse = w.GetCharacter(n.GetString("spouse"));
            ch.Father?.Children.Add(ch);
            ch.Mother?.Children.Add(ch);
        }

        // ---- countries
        foreach (var n in root.Get("countries")?.All ?? Enumerable.Empty<Node>())
        {
            var c = w.GetCountry(n.Key);
            if (c == null) { Log.Warn($"Save contains unknown country {n.Key} (removed from the game files?)"); continue; }
            c.Name = n.GetString("name", c.DefaultName)!;
            if (Rgb.TryParse(n.Get("color"), out var col)) c.Color = col;
            c.FlagPath = n.GetString("flag", c.DefaultFlagPath)!;
            c.Capital = w.GetLocation(n.GetString("capital"));
            c.Gold = n.GetDouble("gold");
            c.Manpower = n.GetDouble("manpower");
            c.Government = n.GetString("government", "")!;
            c.Ruler = w.GetCharacter(n.GetString("ruler"));
            c.Heir = w.GetCharacter(n.GetString("heir"));
            ReadVars(n.Get("vars"), c.Vars);
            c.Recruitment.Clear();
            foreach (var r in n.GetAll("recruit"))
            {
                if (!data.Templates.TryGetValue(r.GetString("template") ?? "", out var t)) continue;
                var loc = w.GetLocation(r.GetString("location"));
                if (loc == null) continue;
                c.Recruitment.Add(new RecruitOrder { Template = t, Location = loc, HoursLeft = r.GetInt("hours"), TotalHours = r.GetInt("total") });
            }
        }

        // ---- states & locations
        foreach (var n in root.Get("states")?.All ?? Enumerable.Empty<Node>())
        {
            var s = w.GetState(n.Key);
            if (s == null) continue;
            s.Owner = w.GetCountry(n.GetString("owner"));
            s.Cores.Clear();
            foreach (var t in n.GetList("cores")) if (w.GetCountry(t) is { } c) s.Cores.Add(c);
            s.Claims.Clear();
            foreach (var t in n.GetList("claims")) if (w.GetCountry(t) is { } c) s.Claims.Add(c);
            s.Manpower = n.GetDouble("manpower", s.Manpower);
            s.Tax = n.GetDouble("tax", s.Tax);
            s.NameOverride = n.GetString("name", s.NameOverride);
            s.Buildings.Clear();
            var b = n.Get("buildings");
            if (b != null) foreach (var x in b.All) if (x.Value != null) s.Buildings[x.Key] = (int)x.AsDouble();
            ReadVars(n.Get("vars"), s.Vars);
            foreach (var l in s.Locations) { l.Controller = s.Owner; l.Fort = 0; l.SiegeProgress = 0; l.Sieger = null; }
        }
        foreach (var n in root.Get("locations")?.All ?? Enumerable.Empty<Node>())
        {
            var l = w.GetLocation(n.Key);
            if (l == null) continue;
            var ctrl = n.GetString("controller");
            l.Controller = ctrl == "none" ? null : w.GetCountry(ctrl);
            l.Fort = n.GetInt("fort");
            l.SiegeProgress = n.GetDouble("siege");
            l.Sieger = w.GetCountry(n.GetString("sieger"));
            l.NameOverride = n.GetString("name");
            ReadVars(n.Get("vars"), l.Vars);
        }
        w.RefreshOwnership();

        // ---- armies & divisions
        var armyById = new Dictionary<int, Army>();
        foreach (var n in root.Get("armies")?.GetAll("army") ?? Enumerable.Empty<Node>())
        {
            var owner = w.GetCountry(n.GetString("owner"));
            if (owner == null) continue;
            var a = new Army
            {
                Id = n.GetInt("num"),
                Name = n.GetString("name", "Army")!,
                Owner = owner,
                General = w.GetCharacter(n.GetString("general")),
                FrontEnemy = w.GetCountry(n.GetString("front")),
                Mode = Enum.TryParse<ArmyMode>(n.GetString("mode"), true, out var m) ? m : ArmyMode.Hold,
                MoveTarget = w.GetLocation(n.GetString("move_target")),
                Objective = w.GetLocation(n.GetString("objective")),
            };
            foreach (var k in n.GetList("front_line")) if (w.GetLocation(k) is { } fl) a.FrontLimit.Add(fl);
            foreach (var k in n.GetList("fallback")) if (w.GetLocation(k) is { } fb) a.Fallback.Add(fb);
            w.Armies.Add(a);
            armyById[a.Id] = a;
        }
        foreach (var n in root.Get("divisions")?.GetAll("division") ?? Enumerable.Empty<Node>())
        {
            var owner = w.GetCountry(n.GetString("owner"));
            var loc = w.GetLocation(n.GetString("location"));
            if (owner == null || loc == null || !data.Templates.TryGetValue(n.GetString("template") ?? "", out var t)) continue;
            var d = new Division
            {
                Id = n.GetInt("num"),
                Name = n.GetString("name", t.Name)!,
                Owner = owner,
                Template = t,
                Location = loc,
                Strength = n.GetDouble("strength", 1),
                Org = n.GetDouble("org", 1),
                Experience = n.GetDouble("experience"),
                MoveProgress = n.GetDouble("progress"),
                Retreating = n.GetBool("retreating"),
                ManualOrder = n.GetBool("manual"),
                FrontPost = w.GetLocation(n.GetString("front_post")),
            };
            foreach (var lk in n.GetList("path")) if (w.GetLocation(lk) is { } pl) d.Path.Add(pl);
            if (n.Has("army") && armyById.TryGetValue(n.GetInt("army"), out var army)) { d.Army = army; army.Divisions.Add(d); }
            d.VolunteerFrom = w.GetCountry(n.GetString("volunteer_from"));
            game.Warfare.AddLoadedDivision(d);
        }

        // ---- wars
        foreach (var n in root.Get("wars")?.GetAll("war") ?? Enumerable.Empty<Node>())
        {
            var al = w.GetCountry(n.GetString("attacker_leader"));
            var dl = w.GetCountry(n.GetString("defender_leader"));
            if (al == null || dl == null) continue;
            var war = new War
            {
                Id = n.GetInt("num"),
                Name = n.GetString("name", "War")!,
                AttackerLeader = al,
                DefenderLeader = dl,
                StartDate = GameDate.Parse(n.GetString("start") ?? "", w.Date),
                BattleScore = n.GetDouble("battle_score"),
                Goal = w.GetState(n.GetString("goal")),
            };
            data.CasusBelli.TryGetValue(n.GetString("cb") ?? "", out var cb);
            war.CasusBelli = cb;
            foreach (var t in n.GetList("attackers")) if (w.GetCountry(t) is { } c) war.Attackers.Add(c);
            foreach (var t in n.GetList("defenders")) if (w.GetCountry(t) is { } c) war.Defenders.Add(c);
            ReadVars(n.Get("vars"), war.Vars);
            w.Wars.Add(war);
        }

        // ---- relations
        var R = w.Relations;
        foreach (var n in root.Get("relations")?.All ?? Enumerable.Empty<Node>())
        {
            Country? G(string k) => w.GetCountry(n.GetString(k));
            switch (n.Key)
            {
                case "alliance": if (G("first") is { } a1 && G("second") is { } b1) R.SetAlliance(a1, b1, true); break;
                case "military_access": if (G("granter") is { } a2 && G("receiver") is { } b2) R.SetAccess(a2, b2, true); break;
                case "guarantee": if (G("guarantor") is { } a3 && G("guaranteed") is { } b3) R.SetGuarantee(a3, b3, true); break;
                case "subject":
                    if (G("subject") is { } s4 && G("overlord") is { } o4 && data.SubjectTypes.TryGetValue(n.GetString("type") ?? "", out var st4)) R.SetSubject(s4, o4, st4);
                    break;
                case "truce": if (G("first") is { } a5 && G("second") is { } b5) R.AddTruce(a5, b5, GameDate.Parse(n.GetString("until") ?? "", w.Date)); break;
                case "pact": if (G("first") is { } a7 && G("second") is { } b7) R.AddPact(a7, b7, GameDate.Parse(n.GetString("until") ?? "", w.Date)); break;
                case "opinion":
                    if (G("from") is { } a6 && G("to") is { } b6)
                    {
                        GameDate? exp = n.Has("expires") ? GameDate.Parse(n.GetString("expires")!, w.Date) : null;
                        R.AddOpinion(a6, b6, n.GetString("name", "")!, n.GetDouble("value"), n.GetDouble("decay"), exp);
                    }
                    break;
            }
        }

        // ---- modifiers
        game.Modifiers.Active.Clear();
        foreach (var n in root.Get("modifiers")?.GetAll("modifier") ?? Enumerable.Empty<Node>())
        {
            game.Script.TryResolveScope(n.GetString("scope") ?? "", game.Context(null), out var scope);
            var key = n.GetString("name");
            if (scope == null || key == null || !game.Modifiers.Defs.ContainsKey(key)) continue;
            int? days = n.Has("expires") ? GameDate.Parse(n.GetString("expires")!, w.Date).TotalDays - w.Date.TotalDays : null;
            game.Modifiers.Add(scope, key, days, n.GetBool("auto"));
        }

        // ---- scheduled events
        foreach (var n in root.Get("scheduled_events")?.GetAll("event") ?? Enumerable.Empty<Node>())
        {
            var def = game.Events.Get(n.GetString("id") ?? "");
            if (def == null) continue;
            game.Script.TryResolveScope(n.GetString("scope") ?? "", game.Context(null), out var scope);
            if (scope == null) continue;
            game.Events.Scheduled.Add(new Events.ScheduledEvent { Def = def, Root = scope, When = GameDate.Parse(n.GetString("when") ?? "", w.Date) });
        }

        if (ids != null)
        {
            w.NextCharacterId = Math.Max(ids.GetInt("character", 1), w.Characters.Select(c => c.Id + 1).DefaultIfEmpty(1).Max());
            w.NextDivisionId = Math.Max(ids.GetInt("division", 1), w.Divisions.Select(d => d.Id + 1).DefaultIfEmpty(1).Max());
            w.NextArmyId = Math.Max(ids.GetInt("army", 1), w.Armies.Select(a => a.Id + 1).DefaultIfEmpty(1).Max());
            w.NextWarId = Math.Max(ids.GetInt("war", 1), w.Wars.Select(x => x.Id + 1).DefaultIfEmpty(1).Max());
            w.NextBattleId = ids.GetInt("battle", 1);
        }

        ReadSections(early: false);
        game.Warfare.RebuildIndex();
        game.SetPlayer(meta.GetString("player"));
        game.MarkStartedFromSave();
        game.Mods.Invoke(m => m.OnSaveLoaded());
        game.RaiseMapChanged();
    }
}
