using Burger.Core.Modding;
using System.Diagnostics;
using System.Text;
using Burger.Core;
using Burger.Core.Map;
using Burger.Core.Model;
using Burger.Core.Save;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Tools;

/// <summary>
/// BurgerEngine command line tool.
///   burger new &lt;Name&gt; [--blank]          create a project (copy of the sample game, or an empty skeleton)
///   burger validate &lt;project&gt; [--days N]   check all files and run the simulation headless
///   burger run &lt;project&gt; [--tag TAG]        play the game
///   burger export &lt;project&gt; [--name N]      build a standalone EXE + editable game folder
///   burger generate-locations &lt;project&gt;    add every province color missing from locations.txt
///   burger docs [project]                  write the scripting reference (all triggers/effects)
/// </summary>
public static class Program
{
    private static string EngineRoot = "";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        EngineRoot = FindEngineRoot();
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }
        try
        {
            string cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToList();
            return cmd switch
            {
                "new" => New(rest),
                "validate" => Validate(rest),
                "selftest" => Validate(rest.Append("--selftest").ToList()),
                "run" => Run(rest),
                "export" => Export(rest),
                "generate-locations" => GenerateLocations(rest),
                "docs" => Docs(rest),
                "list" => ListProjects(),
                "features" => Features(rest),
                "presets" => ListPresets(),
                _ => Fail($"Unknown command '{args[0]}'. Run 'burger help'."),
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message + (ex is GameLoadException ? "" : "\n" + ex.StackTrace));
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"BurgerEngine tools

  burger new <Name>                         create projects/<Name> as a copy of the full sample game
  burger new <Name> --preset hoi4 [--base isle|tiny] [--features a,b]
                                            start from a base map with a preset's features and rules
                                            (--features = exact feature list instead of the preset's)
  burger new <Name> --blank                 blank slate: tiny map, no features
  burger presets                            list presets and base maps
  burger features <project>                 list installed and available features
  burger features <project> add <id...>     add features (and what they need) to a project
  burger features <project> remove <id...>  remove features (changed files go to removed_features/)
  burger list                               list projects
  burger validate <project> [--days 60] [--profile]  load everything, report errors, simulate N days headless (--profile: time per system)
  burger run <project> [--tag TAG]          play the project
  burger export <project> [--name Game] [--out folder] [--rid win-x64]
                                            build <Name>.exe + game/ folder that players can mod
  burger generate-locations <project>       name every provinces.png color missing from locations.txt
  burger docs [project]                     write docs/SCRIPT_REFERENCE.md (all triggers, effects, scopes)

<project> is a name in the projects folder (e.g. SampleGame) or a path.
");
    }

    private static int Fail(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ERROR: " + msg);
        Console.ResetColor();
        return 1;
    }

    private static void Ok(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    private static string? Opt(List<string> args, string name)
    {
        int i = args.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return null;
        if (i + 1 >= args.Count) throw new ArgumentException($"{name} needs a value");
        string v = args[i + 1];
        args.RemoveRange(i, 2);
        return v;
    }

    private static bool Flag(List<string> args, string name)
    {
        int i = args.FindIndex(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        args.RemoveAt(i);
        return true;
    }

    private static string FindEngineRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (File.Exists(Path.Combine(d.FullName, "src", "Burger.Game", "Burger.Game.csproj"))) return d.FullName;
                d = d.Parent;
            }
        }
        return Directory.GetCurrentDirectory();
    }

    private static string ProjectsDir => Path.Combine(EngineRoot, "projects");

    /// <summary>Resolves a project name or path to the project folder (the one containing game/).</summary>
    private static string ResolveProject(string nameOrPath)
    {
        foreach (var candidate in new[] { nameOrPath, Path.Combine(ProjectsDir, nameOrPath) })
        {
            if (Directory.Exists(Path.Combine(candidate, "game"))) return Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(candidate, "game.txt")) || Directory.Exists(Path.Combine(candidate, "common")))
                return Path.GetFullPath(Path.Combine(candidate, ".."));
        }
        throw new DirectoryNotFoundException($"Project '{nameOrPath}' not found (looked in {ProjectsDir}). Use 'burger list'.");
    }

    private static string GameDir(string project) => Path.Combine(project, "game");

    private static string RequireProjectArg(List<string> args, string usage)
    {
        var a = args.FirstOrDefault(x => !x.StartsWith("--"));
        if (a == null) throw new ArgumentException("Usage: " + usage);
        args.Remove(a);
        return a;
    }

    // ===============================================================================================
    private static int ListProjects()
    {
        if (!Directory.Exists(ProjectsDir)) { Console.WriteLine("No projects folder."); return 0; }
        foreach (var d in Directory.GetDirectories(ProjectsDir).OrderBy(d => d))
        {
            string gameTxt = Path.Combine(d, "game", "game.txt");
            string name = File.Exists(gameTxt) ? ScriptParser.ParseFile(gameTxt).GetString("name", "") ?? "" : "";
            Console.WriteLine($"  {Path.GetFileName(d),-24} {name}");
        }
        return 0;
    }

    // ===============================================================================================
    private static int New(List<string> args)
    {
        bool blank = Flag(args, "--blank");
        string? baseId = Opt(args, "--base");
        string? presetId = Opt(args, "--preset");
        string? extra = Opt(args, "--features");
        string name = RequireProjectArg(args, "burger new <Name> [--preset id] [--base isle|tiny] [--features a,b] [--blank]");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(' ')) return Fail("Use a project name without spaces or special characters, e.g. MyEmpire");
        string dest = Path.Combine(ProjectsDir, name);
        if (Directory.Exists(dest)) return Fail($"{dest} already exists");

        if (blank || baseId != null || presetId != null || extra != null)
        {
            // base map + features
            baseId ??= blank ? "tiny" : "isle";
            presetId ??= "blank";
            var b = FeaturePackages.Bases().FirstOrDefault(x => x.Id.Equals(baseId, StringComparison.OrdinalIgnoreCase));
            if (b.Dir == null) return Fail($"Unknown base '{baseId}'. Bases: {string.Join(", ", FeaturePackages.Bases().Select(x => x.Id))}");
            var preset = FeaturePackages.Presets().FirstOrDefault(p => p.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return Fail($"Unknown preset '{presetId}'. Presets: {string.Join(", ", FeaturePackages.Presets().Select(p => p.Id))}");
            // --features is the exact list (the preset then only gives its rules); without it the preset's features are used
            var features = extra != null ? extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : preset.Features.ToList();
            var known = FeaturePackages.All().Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = features.Where(f => !known.Contains(f)).ToList();
            if (unknown.Count > 0) Console.WriteLine($"Note: {string.Join(", ", unknown)} are not engine features - they are recorded as custom features.");
            CopyDir(Path.Combine(b.Dir, "game"), Path.Combine(dest, "game"), excludeBuildOutput: true);
            SetGameName(Path.Combine(dest, "game", "game.txt"), name);
            WriteBackendReadme(dest);
            foreach (var line in FeaturePackages.Install(dest, features, preset.Defines)) Console.WriteLine("  " + line);
            Console.WriteLine($"  Base: {b.Name}, preset: {preset.Name}");
        }
        else
        {
            string sample = Path.Combine(ProjectsDir, "SampleGame");
            if (!Directory.Exists(sample)) return Fail("projects/SampleGame is missing - use --blank or --preset");
            CopyDir(Path.Combine(sample, "game"), Path.Combine(dest, "game"), excludeBuildOutput: true);
            SetGameName(Path.Combine(dest, "game", "game.txt"), name);
            string sampleBackend = Path.Combine(sample, "backend");
            if (Directory.Exists(sampleBackend)) CopyDir(sampleBackend, Path.Combine(dest, "backend"), excludeBuildOutput: true);
        }
        Templates.WriteScriptsProject(Path.Combine(dest, "game", "scripts"), EngineRoot);
        Ok($"Created {dest}");
        Console.WriteLine($"  Edit the files in {Path.Combine(dest, "game")}, then:  burger run {name}");
        return 0;
    }

    /// <summary>One line per installed feature with numbers that show it is working.</summary>
    private static void PrintFeatureStats(Game game)
    {
        var live = game.World.Countries.Where(c => c.Exists).ToList();
        if (game.Features.Has(FeatureSet.Production))
        {
            int civ = live.Sum(c => game.Production.CivilianFactories(c)), mil = live.Sum(c => game.Production.MilitaryFactories(c));
            int lines = live.Sum(c => c.ProductionLines.Count), queued = live.Sum(c => c.Construction.Count);
            double stock = live.Sum(c => c.Stockpile.Values.Sum());
            int waiting = live.Sum(c => c.Recruitment.Count(o => o.WaitingForEquipment));
            Console.WriteLine($"  production: {civ} civilian / {mil} military factories, {lines} lines, {stock:0} equipment in stockpiles, {queued} buildings queued, {waiting} trained divisions waiting for equipment");
        }
        if (game.Features.HasSupply && game.World.Divisions.Count > 0)
        {
            var ds = game.World.Divisions;
            Console.WriteLine($"  supply ({(game.Supply.HubMode ? "hubs" : "range")}): average {ds.Average(d => d.Supply):P0}, {ds.Count(d => d.Supply < 0.5)} of {ds.Count} divisions below 50%");
        }
        if (game.Features.Has(FeatureSet.DivisionDesigner))
            Console.WriteLine($"  designer: {game.Data.Templates.Values.Count(t => t.Custom)} custom templates");
        if (game.Features.Has(FeatureSet.Navy))
        {
            var N = game.Navy;
            Console.WriteLine($"  navy: {N.Fleets.Count} fleets, {N.Fleets.Sum(f => f.Ships.Count)} ships ({N.Fleets.Count(f => f.Location.IsWater)} at sea), {N.BattlesFought} naval battles, {N.ShipsSunk} ships sunk, " +
                              $"{N.ShipOrders.Values.Sum(o => o.Count)} ships ordered, transport capacity {live.Sum(c => N.TransportCapacity(c)):0}, {game.World.Divisions.Count(d => d.Location.IsWater)} divisions at sea, {N.Embarked} embarkations");
        }
        if (game.Features.Has(FeatureSet.Air))
        {
            var A = game.Air;
            Console.WriteLine($"  air: {A.Regions.Count} regions, {A.Wings.Count} wings, {A.Wings.Sum(w => w.Planes):0} planes, {A.Wings.Count(w => w.Region != null)} on missions " +
                              $"({string.Join(", ", A.Wings.Where(w => w.Region != null).GroupBy(w => w.Mission).Select(g => $"{g.Count()} {g.Key}"))}), {A.PlanesLost:0} planes lost, {A.CasDamageDone:0} CAS damage, {A.LevelsBombed:0.#} building levels bombed");
        }
        if (game.Features.Has(FeatureSet.BattlePlans))
            Console.WriteLine($"  battle plans: {game.World.Armies.Count(x => x.Objective != null)} armies with an offensive arrow, {game.World.Armies.Count(x => x.Fallback.Count > 0)} with a fallback line");
        if (game.Features.Has(FeatureSet.LendLease))
            Console.WriteLine($"  lend-lease: {game.Support.Deals.Count} deals, {game.Support.EquipmentSent:0} equipment sent; volunteers: {game.Support.VolunteersSent} sent, {game.World.Divisions.Count(d => d.VolunteerFrom != null)} abroad now");
        if (game.Politics.Enabled)
        {
            var P = game.Politics;
            var alive = game.World.Countries.Where(c => c.Exists).ToList();
            Console.WriteLine($"  politics: rulers {string.Join(", ", alive.GroupBy(c => P.Of(c).Ruling).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}; " +
                              $"{alive.Count(P.HoldsElections)} hold elections, {P.ElectionsHeld} elections, {P.GovernmentChanges} government changes, {P.LawsChanged} laws changed" +
                              (P.CivilWarsEnabled ? $", {P.Movements.Count} movements, {P.CivilWarsStarted} civil wars ({P.CivilWars.Count} active), {P.Revolutions} revolutions/coups" : ""));
        }
    }

    private static void SetGameName(string gameTxt, string name)
    {
        if (!File.Exists(gameTxt)) return;
        string text = File.ReadAllText(gameTxt);
        File.WriteAllText(gameTxt, ScriptPatch.SetValue(text, "game.txt", new[] { "name" }, $"\"{name}\""));
    }

    private static void WriteBackendReadme(string projectDir)
    {
        string dir = Path.Combine(projectDir, "backend");
        Directory.CreateDirectory(dir);
        if (EngineAssets.BackendReadme() is { } readme && !File.Exists(Path.Combine(dir, "README.txt")))
            File.Copy(readme, Path.Combine(dir, "README.txt"));
    }

    // ===============================================================================================
    private static int ListPresets()
    {
        Console.WriteLine("Presets (burger new <Name> --preset <id>):");
        foreach (var p in FeaturePackages.Presets())
            Console.WriteLine($"  {p.Id,-12} {p.Name}\n               {string.Join(" ", p.Features)}");
        Console.WriteLine("Base maps (--base <id>):");
        foreach (var b in FeaturePackages.Bases()) Console.WriteLine($"  {b.Id,-12} {b.Name}");
        return 0;
    }

    private static int Features(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger features <project> [add|remove <id...>]"));
        string? verb = args.FirstOrDefault();
        var ids = args.Skip(1).ToList();
        var installed = FeaturePackages.Installed(project);
        if (verb == null)
        {
            Console.WriteLine(installed == null ? "This project has no game/features.txt: it was made before features existed and every built-in system runs."
                                                : $"Installed: {(installed.Count == 0 ? "(none - blank slate)" : string.Join(" ", installed))}");
            foreach (var g in FeaturePackages.All().GroupBy(p => p.Category))
            {
                Console.WriteLine(g.Key + ":");
                foreach (var p in g)
                    Console.WriteLine($"  [{(installed?.Contains(p.Id, StringComparer.OrdinalIgnoreCase) == true ? "x" : " ")}] {p.Id,-24} {p.Name}" + (p.Requires.Count > 0 ? $"  (needs {string.Join(", ", p.Requires)})" : ""));
            }
            return 0;
        }
        if (ids.Count == 0) return Fail("Name the features, e.g. burger features MyGame add military combat_hoi4");
        if (verb.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            if (installed == null) FeatureSet.Save(GameDir(project), FeatureSet.BuiltIn);
            WriteBackendReadme(project);
            foreach (var line in FeaturePackages.Install(project, ids)) Console.WriteLine("  " + line);
            return 0;
        }
        if (verb.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            if (installed == null) FeatureSet.Save(GameDir(project), FeatureSet.BuiltIn);
            foreach (var id in ids)
            {
                var dependents = FeaturePackages.Dependents(project, id).Where(d => !ids.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
                if (dependents.Count > 0) return Fail($"'{id}' is needed by {string.Join(", ", dependents)} - remove those too");
            }
            // dependents first
            foreach (var id in ids.OrderByDescending(i => FeaturePackages.WithRequirements(new[] { i }).Count))
                foreach (var line in FeaturePackages.Uninstall(project, id)) Console.WriteLine("  " + line);
            return 0;
        }
        return Fail($"Unknown action '{verb}' (use add or remove)");
    }

    private static void CopyDir(string src, string dst, bool excludeBuildOutput)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            if (excludeBuildOutput && f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        }
        foreach (var d in Directory.GetDirectories(src))
        {
            string n = Path.GetFileName(d);
            if (excludeBuildOutput && (n is "bin" or "obj")) continue;
            CopyDir(d, Path.Combine(dst, n), excludeBuildOutput);
        }
    }

    // ===============================================================================================
    private static int Validate(List<string> args)
    {
        bool selftest = Flag(args, "--selftest");
        int days = int.Parse(Opt(args, "--days") ?? (selftest ? "365" : "60"));
        string project = ResolveProject(RequireProjectArg(args, "burger validate <project> [--days N]"));
        Log.EchoToConsole = false;
        Console.WriteLine($"Validating {GameDir(project)}");
        var sw = Stopwatch.StartNew();
        var game = Game.Load(GameDir(project), compileScripts: true, seed: 1234);
        Console.WriteLine($"  loaded in {sw.ElapsedMilliseconds} ms: {game.World.Countries.Count} countries, {game.World.States.Count} states, " +
                          $"{game.World.Locations.Count} locations, {game.World.Characters.Count} characters, {game.World.Divisions.Count} divisions, " +
                          $"{game.Data.Events.Count} events, {game.Mods.Scripts.Count} C# scripts");
        PrintLog();
        int loadErrors = Log.ErrorCount;

        Console.WriteLine($"Simulating {days} days (all AI)...");
        game.StartNewGame(null);
        sw.Restart();
        int battles = 0, wars = 0;
        game.Mods.Add(new CountingScript(() => battles++, () => wars++));
        int errorsBefore = Log.ErrorCount;
        game.Profiling = args.Contains("--profile");
        int warsAtDecade = 0;
        for (int d = 0; d < days; d++)
        {
            for (int h = 0; h < 24; h++) game.Tick();
            if (days >= 3650 && (d + 1) % 3650 == 0)
            {
                Console.WriteLine($"  year {(d + 1) / 365}: {wars - warsAtDecade} wars declared in the last 10 years, {game.World.Wars.Count} active, {game.World.Countries.Count(c => c.Exists)} countries");
                warsAtDecade = wars;
            }
        }
        Console.WriteLine($"  simulated {days} days in {sw.ElapsedMilliseconds} ms - date {game.World.Date.ToDisplay()}, " +
                          $"{wars} wars declared, {battles} battles, {game.World.Wars.Count} active wars, {game.World.Divisions.Count} divisions, " +
                          $"{game.World.Characters.Count(c => c.Alive)} living characters, {game.World.Countries.Count(c => c.Exists)} countries");
        foreach (var w in game.World.Wars) Console.WriteLine($"    {w.Name}: war score {w.WarScore:0}");
        if (args.Contains("--diag-wars"))
        {
            var live = game.World.Countries.Where(c => c.Exists).ToList();
            foreach (var a1 in live)
                foreach (var b1 in Builtins.NeighborCountries(game, a1))
                {
                    var cb = game.Diplomacy.BestCasusBelli(a1, b1);
                    string why = game.Rules.Diplomacy.CannotDeclareWarReason(game, a1, b1, cb) ?? (cb == null ? "no casus belli" : "can declare");
                    Console.WriteLine($"    {a1.Tag} -> {b1.Tag}: {why}, strength {game.Rules.Diplomacy.MilitaryStrength(game, a1):0} vs {game.Rules.Diplomacy.MilitaryStrength(game, b1):0}, " +
                                      $"claims {b1.OwnedStates.Count(st => st.Claims.Contains(a1))}, gold {a1.Gold:0}, allied {game.World.Relations.AreAllied(a1, b1)}, subject {game.World.Relations.OverlordOf(a1)?.Tag}");
                }
        }
        PrintFeatureStats(game);
        if (game.Profiling)
            Console.WriteLine("  time per system (ms per simulated day): " + string.Join(", ", game.ProfileMs.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value / days:0.00}")));

        if (selftest)
        {
            PatchSelfTest(game);
            string tmp = Path.Combine(Path.GetTempPath(), "burger_selftest.sav");
            SaveGame.Save(game, tmp);
            var game2 = Game.Load(GameDir(project), compileScripts: true, seed: 1234);
            SaveGame.Load(game2, tmp);
            Check(game2.World.Date == game.World.Date, "save/load keeps the date");
            Check(game2.World.Divisions.Count == game.World.Divisions.Count, $"save/load keeps divisions ({game2.World.Divisions.Count}/{game.World.Divisions.Count})");
            Check(game2.World.Wars.Count == game.World.Wars.Count, "save/load keeps wars");
            Check(game2.World.Characters.Count == game.World.Characters.Count, "save/load keeps characters");
            Check(game.World.States.All(s => game2.World.GetState(s.Key)?.Owner?.Tag == s.Owner?.Tag), "save/load keeps state owners");
            if (game.Features.Has(FeatureSet.Navy))
                Check(game2.Navy.Fleets.Count == game.Navy.Fleets.Count && game2.Navy.Fleets.Sum(f => f.Ships.Count) == game.Navy.Fleets.Sum(f => f.Ships.Count), $"save/load keeps fleets ({game2.Navy.Fleets.Count}/{game.Navy.Fleets.Count})");
            if (game.Features.Has(FeatureSet.Air))
                Check(game2.Air.Wings.Count == game.Air.Wings.Count && game2.Air.Wings.Count(w => w.Region != null) == game.Air.Wings.Count(w => w.Region != null), $"save/load keeps air wings ({game2.Air.Wings.Count}/{game.Air.Wings.Count})");
            for (int h = 0; h < 24 * 30; h++) game2.Tick();
            Check(true, "loaded game runs for 30 more days");
            foreach (var model in game.Combat.Models.Select(m => m.Id).ToList())
            {
                var g3 = Game.Load(GameDir(project), compileScripts: true, seed: 99);
                g3.Data.Defines.CombatModel = model;
                g3.StartNewGame(null);
                var a = g3.World.Countries.First(c => c.Exists && Builtins.NeighborCountries(g3, c).Count > 0);
                var b = Builtins.NeighborCountries(g3, a).First();
                g3.Diplomacy.DeclareWar(a, b, null, null, force: true);
                foreach (var army in g3.World.ArmiesOf(a)) g3.Warfare.SetFront(army, b, ArmyMode.Offensive);
                int fought = 0;
                g3.Mods.Add(new CountingScript(() => fought++, () => { }));
                for (int h = 0; h < 24 * 120; h++) g3.Tick();
                Check(fought > 0, $"combat model '{model}': {fought} battles fought, war score {g3.World.WarBetween(a, b)?.WarScore ?? 0:0}");
            }
            if (game.Features.Has(FeatureSet.Military) && game.Features.Has(FeatureSet.Diplomacy)) MilitaryFeatureTests(project);
            if (game.Features.Has(FeatureSet.Politics)) PoliticsFeatureTests(project);
        }
        int simErrors = Log.ErrorCount - errorsBefore;
        if (simErrors > 0)
        {
            Console.WriteLine("Errors during simulation:");
            foreach (var e in Log.Entries.Where(e => e.Level == LogLevel.Error).TakeLast(simErrors)) Console.WriteLine("  " + e);
        }
        if (Log.ErrorCount == 0) Ok($"OK - no errors ({Log.WarningCount} warnings)");
        else Console.WriteLine($"{Log.ErrorCount} errors ({loadErrors} while loading), {Log.WarningCount} warnings");
        return Log.ErrorCount == 0 && _failedChecks == 0 ? 0 : 1;
    }

    private static int _failedChecks;

    /// <summary>A scripted war that exercises battle plans, air missions, lend-lease and volunteers (when installed).</summary>
    private static void MilitaryFeatureTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 7);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var a = g.World.Countries.First(c => c.Exists && Builtins.NeighborCountries(g, c).Count > 0);
        var b = Builtins.NeighborCountries(g, a).First();
        var helper = g.World.Countries.FirstOrDefault(c => c.Exists && c != a && c != b && g.World.Divisions.Count(d => d.Owner == c) >= 2);
        g.Diplomacy.DeclareWar(a, b, null, null, force: true);
        var war = g.World.WarBetween(a, b);
        var army = g.World.ArmiesOf(a).FirstOrDefault() ?? g.Warfare.CreateArmy(a);
        foreach (var d in g.World.Divisions.Where(d => d.Owner == a && d.Army == null).ToList()) g.Warfare.AssignToArmy(d, army);
        g.Warfare.SetFront(army, b, ArmyMode.Offensive);
        if (g.Features.Has(FeatureSet.BattlePlans) && a.Capital != null)
        {
            army.Objective = b.Capital;
            g.Warfare.ToggleFallbackLocation(army, a.Capital);
            g.Warfare.SetMode(army, ArmyMode.Fallback);
            for (int h = 0; h < 24 * 20; h++) g.Tick();
            Check(army.Divisions.Count > 0 && army.Divisions.All(d => d.FrontPost == a.Capital), $"battle plan: the army holds its fallback line ({army.Divisions.Count(d => d.Location == a.Capital)}/{army.Divisions.Count} there)");
            g.Warfare.SetMode(army, ArmyMode.Offensive);
            string tmp = Path.Combine(Path.GetTempPath(), "burger_plans.sav");
            SaveGame.Save(g, tmp);
            var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 7);
            SaveGame.Load(g2, tmp);
            var a2 = g2.World.Armies.FirstOrDefault(x => x.Id == army.Id);
            Check(a2 != null && a2.Objective == g2.World.GetLocation(b.Capital?.Key) && a2.Fallback.Count == 1 && a2.Mode == ArmyMode.Offensive, "battle plan: objective, fallback line and mode are saved");
        }
        if (g.Features.Has(FeatureSet.Air))
        {
            var wing = g.Air.WingsOf(a).FirstOrDefault(w => g.Air.CanFly(w.Type, AirMission.Superiority));
            var region = a.Capital?.State != null ? g.Air.RegionOf(a.Capital.State) : null;
            if (wing != null && region != null)
            {
                Check(g.Air.Assign(wing, region, AirMission.Superiority) == null, "air: a fighter wing flies air superiority over its home region");
                g.Air.DailyTick();
                var (share, contested) = g.Air.Superiority(region, a);
                Check(contested && share > 0.5, $"air: the region's sky is held ({share:P0})");
            }
        }
        if (g.Features.Has(FeatureSet.LendLease) && helper != null)
        {
            var reason = g.Support.CannotSendVolunteers(helper, a);
            int sent = reason == null ? g.Support.SendVolunteers(helper, a, 1) : 0;
            Check(sent == 1 && g.World.Divisions.Any(d => d.VolunteerFrom == helper && d.Owner == a), "volunteers: a division fights for a country at war" + (reason != null ? $" ({reason})" : ""));
            if (g.Features.Has(FeatureSet.Production) && g.Data.Equipment.Values.FirstOrDefault() is { } e)
            {
                helper.Stockpile[e.Key] = 1000;
                double before = a.Stockpile.GetValueOrDefault(e.Key);
                Check(g.Support.SetLendLease(helper, a, e, 0.25) == null, "lend-lease deal set");
                g.Support.MonthlyTick();
                Check(a.Stockpile.GetValueOrDefault(e.Key) >= before + 249, $"lend-lease: equipment arrived ({a.Stockpile.GetValueOrDefault(e.Key) - before:0})");
            }
            if (war != null) g.Diplomacy.EndWar(war);
            for (int h = 0; h < 24 * 2; h++) g.Tick();
            Check(!g.World.Divisions.Any(d => d.VolunteerFrom != null) && g.World.Divisions.Count(d => d.Owner == helper) >= 2, "volunteers come home at peace");
        }
    }

    /// <summary>Elections, laws, movements and civil wars (when installed).</summary>
    private static void PoliticsFeatureTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 11);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var P = g.Politics;
        if (!P.Enabled) { Check(false, "politics: no ideologies defined (common/ideologies)"); return; }
        var alive = g.World.Countries.Where(c => c.Exists).ToList();
        Check(alive.All(c => P.Ideology(P.Of(c).Ruling) != null && Math.Abs(P.Of(c).Popularity.Values.Sum() - 1) < 1e-6), "politics: every country has a ruling party and popularity adds up to 100%");
        // an election that the opposition wins
        var demo = alive.FirstOrDefault(P.HoldsElections);
        if (demo != null)
        {
            var other = P.Ideologies.FirstOrDefault(i => i.Key != P.Of(demo).Ruling && !P.IsBanned(demo, i.Key));
            if (other != null)
            {
                P.AddPopularity(demo, other.Key, 0.9);
                P.HoldElection(demo);
                Check(P.InGovernment(demo, other.Key), $"politics: an election puts the most popular party in government ({demo.Tag}: {string.Join(", ", P.Of(demo).Government)})");
            }
        }
        var c = alive.OrderByDescending(x => x.OwnedStates.Count).First();
        if (P.LawsEnabled)
        {
            var law = P.Laws.Values.FirstOrDefault(l => l.Modifier.Count > 0 && !P.HasLaw(c, l.Key));
            if (law != null)
            {
                var key = law.Modifier.Keys.First();
                double before = g.Modifiers.Sum(c, key);
                double oldValue = P.LawOf(c, law.Group)?.Modifier.GetValueOrDefault(key) ?? 0;
                P.Enact(c, law, free: true);
                double expected = before - oldValue + law.Modifier[key];
                Check(P.HasLaw(c, law.Key) && Math.Abs(g.Modifiers.Sum(c, key) - expected) < 1e-6,
                      $"laws: {law.Name} is in force and its modifier applies ({key} {before:0.##} -> {g.Modifiers.Sum(c, key):0.##})");
            }
        }
        if (P.CivilWarsEnabled)
        {
            // a banned, popular ideology forms a movement
            var ban = P.Laws.Values.FirstOrDefault(l => l.Bans.Count > 0);
            var target = alive.FirstOrDefault(x => x != c && x.OwnedStates.Count >= 2);
            if (ban != null && target != null)
            {
                P.SetRulingParty(target, P.Ideologies.First(i => !ban.Bans.Contains(i.Key)).Key);
                P.Enact(target, ban, free: true);
                P.AddPopularity(target, ban.Bans.First(), 0.45);
                g.Politics.MonthlyTick();
                Check(P.MovementsOf(target).Any(), $"movements: a banned party with {P.PopularityOf(target, ban.Bans.First()):P0} support forms a movement");
            }
            if (g.Features.Has(FeatureSet.Military) && g.Features.Has(FeatureSet.Diplomacy))
            {
                var ide = P.Ideologies.First(i => i.Key != P.Of(c).Ruling).Key;
                int statesBefore = c.OwnedStates.Count;
                var rebel = P.StartCivilWar(c, ide, 0.4);
                Check(rebel != null && rebel.Exists && c.OwnedStates.Count < statesBefore && g.World.AtWar(rebel, c), $"civil war: {rebel?.Name} split from {c.Name} ({rebel?.OwnedStates.Count} of {statesBefore} states)");
                if (rebel != null)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "burger_politics.sav");
                    SaveGame.Save(g, tmp);
                    var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 11);
                    SaveGame.Load(g2, tmp);
                    var r2 = g2.World.GetCountry(rebel.Tag);
                    Check(r2 != null && r2.Exists && g2.Politics.InCivilWar(r2) && g2.Politics.Of(r2).Ruling == ide, "civil war: the rebel country and the civil war are saved");
                    // the revolution wins: the loyal army is gone and the rebels hold all of the country's land
                    foreach (var d in g.World.Divisions.Where(d => d.Owner == c).ToList()) g.Warfare.DestroyDivision(d);
                    foreach (var l in c.OwnedStates.SelectMany(s => s.Locations)) g.Warfare.SetController(l, rebel, silent: true);
                    for (int h = 0; h < 24 * 2; h++) g.Tick();
                    Check(!rebel.Exists && c.Exists && P.Of(c).Ruling == ide && !P.InCivilWar(c), $"civil war: the revolution won and {c.Name} is now ruled by {P.Of(c).Ruling}");
                }
            }
        }
    }

    /// <summary>Checks that editing files in place keeps comments and produces valid scripts (used by the editor).</summary>
    private static void PatchSelfTest(Game game)
    {
        const string f = "test.txt";
        string text = "# header\nVAL = {\n\tname = \"Old\" # keep me\n\tcolor = { 1 2 3 }\n}\n# between\nKES = { name = X }\n";
        string t1 = ScriptPatch.SetValue(text, f, new[] { "VAL", "name" }, "\"New Name\"");
        Check(t1.Contains("name = \"New Name\" # keep me") && t1.Contains("# header") && t1.Contains("# between"), "patch: replace a value, comments kept");
        string t2 = ScriptPatch.SetValue(t1, f, new[] { "VAL", "capital" }, "pelastad");
        Check(t2.Contains("\tcapital = pelastad\n}") , "patch: insert into a multi-line block");
        string t3 = ScriptPatch.SetValue(t2, f, new[] { "KES", "gold" }, "5");
        Check(t3.Contains("KES = { name = X gold = 5 }"), "patch: insert into a one-line block");
        string t4 = ScriptPatch.Remove(t3, f, new[] { "VAL", "color" });
        Check(!t4.Contains("color") && t4.Contains("capital = pelastad"), "patch: remove a line");
        string t5 = ScriptPatch.SetValue(t4, f, new[] { "VAL", "names", "male" }, "{ A B }");
        var p5 = ScriptParser.ParseQuiet(t5, f);
        Check(p5.Get("VAL")?.Get("names")?.GetList("male").Count == 2, "patch: create nested blocks");
        string t6 = ScriptPatch.SetValue(t5, f, new[] { "#1", "name" }, "Y");
        Check(ScriptParser.ParseQuiet(t6, f).Get("KES")?.GetString("name") == "Y", "patch: positional path (#index)");
        var errs = new List<(string, string)>();
        ScriptParser.ParseQuiet(t6, f, errs);
        Check(errs.Count == 0, "patch: result parses without errors");

        // states: unknown keys and comments survive, ownership change is written
        var st = game.World.States.FirstOrDefault(s => s.Owner != null);
        if (st != null)
        {
            string stText = $"# my comment\n{st.Key} = {{\n\towner = {st.Owner!.Tag} # owner comment\n\tflags = {{ special }}\n\tlocations = {{ {string.Join(" ", st.Locations.Select(l => l.Key))} }}\n}}\n";
            string written = Burger.Core.Map.StateWriter.SetState(stText, "s.txt", st);
            var pn = ScriptParser.ParseQuiet(written, "s.txt").Get(st.Key);
            Check(written.Contains("# my comment") && written.Contains("# owner comment") && pn?.GetList("flags").Count == 1
                  && pn.GetList("locations").Count == st.Locations.Count && pn.GetString("owner") == st.Owner.Tag, "state writer keeps comments and extra keys");
        }
    }

    private static void Check(bool ok, string what)
    {
        if (ok) Ok("  [pass] " + what);
        else { _failedChecks++; Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("  [FAIL] " + what); Console.ResetColor(); }
    }

    private static void PrintLog()
    {
        foreach (var e in Log.Entries.Where(e => e.Level != LogLevel.Info))
        {
            Console.ForegroundColor = e.Level == LogLevel.Error ? ConsoleColor.Red : ConsoleColor.Yellow;
            Console.WriteLine("  " + e);
        }
        Console.ResetColor();
    }

    private sealed class CountingScript : Burger.Core.Modding.GameScript
    {
        private readonly Action _battle, _war;
        public CountingScript(Action battle, Action war) { _battle = battle; _war = war; }
        public override void OnBattleEnded(Battle battle) => _battle();
        public override void OnWarDeclared(War war) => _war();
    }

    // ===============================================================================================
    private static int Run(List<string> args)
    {
        string? tag = Opt(args, "--tag");
        string project = ResolveProject(RequireProjectArg(args, "burger run <project> [--tag TAG]"));
        string exe = Path.Combine(EngineRoot, "src", "Burger.Game", "bin", "Release", "net8.0", "Burger.Game.exe");
        var gameArgs = new List<string> { "--game", GameDir(project) };
        if (tag != null) { gameArgs.Add("--tag"); gameArgs.Add(tag); }
        gameArgs.AddRange(args);
        ProcessStartInfo psi;
        if (File.Exists(exe) && OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo(exe);
        }
        else
        {
            psi = new ProcessStartInfo("dotnet");
            foreach (var a in new[] { "run", "--project", Path.Combine(EngineRoot, "src", "Burger.Game"), "-c", "Release", "--" }) psi.ArgumentList.Add(a);
        }
        foreach (var a in gameArgs) psi.ArgumentList.Add(a);
        psi.UseShellExecute = false;
        Console.WriteLine($"Starting {Path.GetFileName(project)}...");
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    // ===============================================================================================
    private static int Export(List<string> args)
    {
        string? name = Opt(args, "--name");
        string? outDir = Opt(args, "--out");
        string rid = Opt(args, "--rid") ?? "win-x64";
        string project = ResolveProject(RequireProjectArg(args, "burger export <project> [--name GameName] [--out folder]"));
        string gameDir = GameDir(project);

        // validate first so players never get a broken build
        Log.Clear();
        var game = Game.Load(gameDir, compileScripts: true);
        string title = game.Data.Info.Name;
        name ??= MakeFileName(title);
        PrintLog();
        if (Log.ErrorCount > 0) Console.WriteLine($"Warning: the game has {Log.ErrorCount} errors (exporting anyway).");

        outDir = Path.GetFullPath(outDir ?? Path.Combine(project, "export", name));
        Console.WriteLine($"Exporting '{title}' to {outDir}");
        if (Directory.Exists(outDir))
        {
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            string oldGame = Path.Combine(outDir, "game");
            if (Directory.Exists(oldGame)) Directory.Delete(oldGame, true);
        }
        Directory.CreateDirectory(outDir);

        string publishDir = Path.Combine(Path.GetTempPath(), "burger_publish_" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var a in new[]
                 {
                     "publish", Path.Combine(EngineRoot, "src", "Burger.Game", "Burger.Game.csproj"),
                     "-c", "Release", "-r", rid, "--self-contained", "true",
                     "-p:PublishSingleFile=true", "-p:IncludeAllContentForSelfExtract=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
                     "-p:DebugType=none", "-p:GenerateDocumentationFile=false", "-o", publishDir, "-nologo", "-v", "q",
                 })
            psi.ArgumentList.Add(a);
        // the backend (projects/<Name>/backend/*.cs) is compiled into the EXE so players cannot change it
        var backendFiles = Burger.Core.Modding.ModManager.FindBackendFiles(gameDir);
        if (backendFiles.Count > 0)
        {
            psi.ArgumentList.Add("-p:BackendDir=" + Burger.Core.Modding.ModManager.BackendDir(gameDir));
            Console.WriteLine($"Compiling {backendFiles.Count} backend file(s) into the EXE (players cannot change them).");
        }
        Console.WriteLine("Running dotnet publish (the first time downloads the .NET runtime pack)...");
        using (var p = Process.Start(psi)!)
        {
            p.WaitForExit();
            if (p.ExitCode != 0) return Fail("dotnet publish failed");
        }
        string ext = rid.StartsWith("win") ? ".exe" : "";
        string builtExe = Path.Combine(publishDir, "Burger.Game" + ext);
        if (!File.Exists(builtExe)) return Fail($"Publish output not found: {builtExe}");
        File.Copy(builtExe, Path.Combine(outDir, name + ext), true);
        // any extra files the publish produced (normally none with single-file)
        foreach (var f in Directory.GetFiles(publishDir))
        {
            string fn = Path.GetFileName(f);
            if (fn.StartsWith("Burger.Game", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".pdb")) continue;
            File.Copy(f, Path.Combine(outDir, fn), true);
        }
        try { Directory.Delete(publishDir, true); } catch { /* temp */ }

        CopyDir(gameDir, Path.Combine(outDir, "game"), excludeBuildOutput: true);
        File.WriteAllText(Path.Combine(outDir, "README.txt"), Templates.PlayerReadme(title, name + ext));
        Ok($"Done: {Path.Combine(outDir, name + ext)}");
        Console.WriteLine("Zip the whole folder to share it. Players can edit everything inside game/.");
        return 0;
    }

    private static string MakeFileName(string title)
    {
        var sb = new StringBuilder();
        foreach (char c in title)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.Length == 0 ? "Game" : sb.ToString();
    }

    // ===============================================================================================
    private static int GenerateLocations(List<string> args)
    {
        string project = ResolveProject(RequireProjectArg(args, "burger generate-locations <project>"));
        var (added, total) = MapTools.GenerateLocationNames(GameDir(project));
        if (added == 0) { Ok($"All {total} colors already have a name."); return 0; }
        Ok($"Added {added} locations to map/locations.txt ({total} colors on the map)");
        Console.WriteLine("Sea zones: change a line to  HEX = { name = sea_1 type = sea }  (or use an is_water terrain).");
        return 0;
    }

    // ===============================================================================================
    private static int Docs(List<string> args)
    {
        var projectArg = args.FirstOrDefault(x => !x.StartsWith("--")) ?? "SampleGame";
        Game game;
        try { game = Game.Load(GameDir(ResolveProject(projectArg)), compileScripts: true); }
        catch { game = new Game(); }
        var sb = new StringBuilder();
        sb.AppendLine("# Script reference");
        sb.AppendLine();
        sb.AppendLine("Generated by `burger docs`. C# scripts can add their own entries (see MODDING.md).");
        sb.AppendLine();
        void Section(string title, IEnumerable<string> names, string note)
        {
            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine(note);
            sb.AppendLine();
            foreach (var n in names.OrderBy(n => n)) sb.AppendLine($"- `{n}`");
            sb.AppendLine();
        }
        Section("Triggers", game.Script.TriggerNames, "Used in `trigger`, `potential`, `allow`, `limit` blocks. Logic: `AND`, `OR`, `NOT`, `NOR`, `NAND`, `always`, `custom_tooltip`.");
        Section("Numeric values", game.Script.ValueNames, "Compare in triggers (`gold >= 100`), or use as an argument (`add_gold = income`, `scope:recipient.military_strength`).");
        Section("Effects", game.Script.EffectNames, "Used in `immediate`, `option`, `on_accept`, `effect` blocks. Control flow: `if`/`else_if`/`else` with `limit`, `random = { chance = 50 }`, `random_list = { 60 = {} 40 = {} }`, `hidden_effect`.");
        Section("Iterators", game.Script.IteratorNames, "Prefix with `any_` / `all_` (triggers) or `every_` / `random_` (effects), e.g. `every_owned_state = { limit = { ... } add_tax = 1 }`. `any_` supports `count >= 2` and `percent >= 0.5`.");
        Section("Scope links", game.Script.LinkNames, "Switch scope: `ruler = { add_trait = brave }`, compare: `owner = ROOT`, chain: `ROOT.capital.state`. Also: `ROOT`, `THIS`, `PREV`, `TAG`, `c:TAG`, `state:key`, `location:key`, `character:id`, `scope:name`.");
        Section("Console commands", game.Commands.Names, "Open the console in game with the ` key.");
        string path = Path.Combine(EngineRoot, "docs", "SCRIPT_REFERENCE.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Ok($"Wrote {path}");
        return 0;
    }
}
