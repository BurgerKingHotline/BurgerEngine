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
  burger validate <project> --days 36500 --seeds 1,2,3  play several worlds at the same time (--seed N: one other world)
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
        if (game.Focus.Enabled)
        {
            var done = live.SelectMany(c => game.Focus.FocusesOf(c).Where(f => game.Focus.IsDone(c, f.Key)).Select(f => (c, f))).ToList();
            Console.WriteLine($"  focuses: {done.Count} completed by {done.Select(d => d.c).Distinct().Count()} countries; " +
                string.Join(", ", done.Where(d => !d.f.Tree.Default).GroupBy(d => d.c.Tag).Select(g => $"{g.Key} {g.Count()}/{game.Focus.FocusesOf(g.First().c).Count()}")));
        }
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
        if (game.Population.Enabled)
        {
            var Pop = game.Population;
            var alive = game.World.Countries.Where(c => c.Exists).ToList();
            double now = game.World.States.Sum(Pop.TotalOf), start = game.World.States.Sum(s => Pop.HasPops(s) ? Pop.Of(s).InitialTotal : 0);
            int foreignMajority = game.World.States.Count(s => s.Owner != null && Pop.MajorityCulture(s) is { } mc && !Pop.IsAccepted(s.Owner, mc));
            Console.WriteLine($"  population: {Burger.Core.Population.PopulationSystem.Format(now)} people ({(start > 0 ? (now / start - 1) : 0):+0.0%;-0.0%} since the start), {Pop.CultureByKey.Count} cultures, " +
                              $"{foreignMajority} states with a foreign majority" + (Pop.ReligionEnabled ? $", {Pop.ReligionByKey.Count} religions" : "") +
                              (Pop.CoresEnabled ? $", cores +{Pop.CoresGained}/-{Pop.CoresLost}, {Pop.ClaimsMade} claims" : "") +
                              (Pop.ColonizationEnabled ? $", {Pop.ColoniesFounded} colonies ({game.World.States.Count(s => s.Owner == null)} unowned states left)" : "") +
                              $", {Pop.BuildingsBuilt} gold buildings" +
                              (Pop.IndependenceEnabled ? $", {Pop.Independence.Count} independence movements (strongest {(Pop.Independence.Count > 0 ? Pop.Independence.Max(m => m.Progress) : 0):0}%), {Pop.IndependenceDeclared} declared independence ({Pop.IndependenceWon} won, {Pop.IndependenceCrushed} crushed, {Pop.IndependenceWars.Count} wars active)" : ""));
        }
        if (game.Tech.Enabled)
        {
            var liveC = game.World.Countries.Where(c => c.Exists).ToList();
            var top = liveC.OrderByDescending(c => game.Tech.Count(c)).Take(6).Select(c => $"{c.Tag} {game.Tech.Count(c)}");
            Console.WriteLine($"  technology: average {liveC.Average(c => game.Tech.Count(c)):0} of {game.Tech.Defs.Count} known; most: {string.Join(", ", top)}; {string.Join(", ", new[] { "GER", "USA", "SOV", "JAP", "ITA", "FRA", "ENG" }.Select(t => game.World.GetCountry(t)).Where(c => c != null).Select(c => $"{c!.Tag} {game.Tech.Count(c)}"))}");
        }
        if (game.Commodities.Enabled)
        {
            foreach (var k in game.Commodities.Defs.Values)
            {
                var liveC = game.World.Countries.Where(c => c.Exists).ToList();
                double world = liveC.Sum(c => game.Commodities.BalanceOf(c, k.Key).Output);
                var short_ = liveC.Where(c => game.Commodities.BalanceOf(c, k.Key).Satisfied < 0.85).Select(c => $"{c.Tag} {game.Commodities.BalanceOf(c, k.Key).Satisfied:P0}").Take(8);
                string main = string.Join(" ", new[] { "USA", "GER", "ENG", "FRA", "SOV", "JAP", "ITA" }.Select(t => game.World.GetCountry(t)).Where(c => c != null).Select(c => { var b = game.Commodities.BalanceOf(c!, k.Key); return $"{c!.Tag}:{b.Output:0}/{b.Demand:0}"; }));
                Console.WriteLine($"  {k.Name}: world {world:0}/month, output/demand {main}; short: {string.Join(", ", short_)}");
            }
        }
        if (game.Features.Has(FeatureSet.Demilitarization)) Console.WriteLine($"  demilitarized states: {game.Diplomacy.DemilitarizedStates.Count()}");
        if (game.Politics.Enabled)
        {
            var P = game.Politics;
            var alive = game.World.Countries.Where(c => c.Exists).ToList();
            Console.WriteLine($"  politics: rulers {string.Join(", ", alive.GroupBy(c => P.Of(c).Ruling).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}; " +
                              $"{alive.Count(P.HoldsElections)} hold elections, {P.ElectionsHeld} elections, {P.GovernmentChanges} government changes, {P.LawsChanged} laws changed" +
                              (P.CivilWarsEnabled ? $", {P.Movements.Count} movements, {P.CivilWarsStarted} civil wars ({P.CivilWars.Count} active), {P.Revolutions} revolutions ({P.Coups} coups)" : ""));
            if (alive.Count > 0)
                Console.WriteLine($"  popularity: average {string.Join(", ", P.Ideologies.Select(i => $"{i.Key} {alive.Average(c => P.Of(c).Popularity.GetValueOrDefault(i.Key)):P0}"))}; " +
                                  $"{alive.Count(c => P.Of(c).Popularity.Values.DefaultIfEmpty(0).Max() > 0.9)} of {alive.Count} countries have one ideology above 90%");
        }
        if (game.Features.Has(FeatureSet.Diplomacy))
        {
            var D = game.Diplomacy;
            var R = game.World.Relations;
            var alive = game.World.Countries.Where(c => c.Exists).ToList();
            var subj = R.Subjects.Keys.Where(c => c.Exists).ToList();
            Console.WriteLine($"  diplomacy: casus belli used {string.Join(", ", D.CasusBelliUsed.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}; " +
                              $"{D.PeaceDeals} peace deals ({D.WhitePeaces} white), {R.Pacts.Count} pacts, {subj.Count} subjects (liberty {(subj.Count > 0 ? subj.Average(D.LibertyDesire) : 0):0}% avg), " +
                              $"war exhaustion avg {(alive.Count > 0 ? alive.Average(D.WarExhaustion) : 0):0.0} (max {(alive.Count > 0 ? alive.Max(D.WarExhaustion) : 0):0.0}), {D.MenLost.Values.Sum() / 1e6:0.0}M war dead");
            if (Environment.GetEnvironmentVariable("BURGER_PEACE_LOG") != null)
            {
                foreach (var line in D.PeaceLog) Console.WriteLine("    " + line);
                foreach (var s in subj)
                    Console.WriteLine($"    subject {s.Tag} {s.Name} ({(game.Politics.Enabled ? game.Politics.Of(s).Ruling : "")}, {s.OwnedStates.Count} states) of {R.OverlordOf(s)!.Tag} ({(game.Politics.Enabled ? game.Politics.Of(R.OverlordOf(s)!).Ruling : "")})");
            }
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

    /// <summary>
    /// Plays the project once per seed, all at the same time (one process each, so a long simulation of several worlds
    /// takes as long as one), and prints the worlds one after the other. One world is one roll of the dice: compare a few.
    /// </summary>
    private static int ValidateSeeds(List<string> args, string seedList)
    {
        var seeds = seedList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string exe = Environment.ProcessPath ?? "dotnet";
        var sw = Stopwatch.StartNew();
        var runs = seeds.Select(seed =>
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) psi.ArgumentList.Add(typeof(Program).Assembly.Location);
            psi.ArgumentList.Add("validate");
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--seed");
            psi.ArgumentList.Add(seed);
            var process = Process.Start(psi)!;
            return (Seed: seed, Process: process, Output: process.StandardOutput.ReadToEndAsync());
        }).ToList();
        int worst = 0;
        var results = new Dictionary<string, List<string>>();
        foreach (var run in runs)
        {
            string text = run.Output.Result;
            run.Process.WaitForExit();
            worst = Math.Max(worst, run.Process.ExitCode);
            Console.WriteLine($"=== seed {run.Seed}");
            var textLines = text.Split((char)10);
            Console.Write(string.Join(((char)10).ToString(), textLines.Where(l => !l.StartsWith("SCORE|"))));
            foreach (var l in textLines.Where(l => l.StartsWith("SCORE|")))
            {
                var parts = l.Trim().Split('|');
                if (parts.Length == 3) { if (!results.TryGetValue(parts[1], out var lst)) results[parts[1]] = lst = new List<string>(); lst.Add(parts[2]); }
            }
        }
        Console.WriteLine($"{seeds.Length} worlds in {sw.Elapsed.TotalSeconds:0} s");
        if (args.Contains("--scorecard") && results.Count > 0) PrintScorecard(args, results, seeds.Length);
        return worst;
    }

    private static void PrintScorecard(List<string> args, Dictionary<string, List<string>> results, int worlds)
    {
        string project = args.FirstOrDefault(x => !x.StartsWith("--") && !int.TryParse(x, out _)) ?? "";
        string path = Path.Combine(Path.GetDirectoryName(GameDir(ResolveProject(project)))!, "scorecard.txt");
        var lines = ScorecardRecorder.Parse(path);
        int ok = 0, low = 0, high = 0, na = 0;
        double okW = 0, allW = 0;
        Console.WriteLine();
        Console.WriteLine($"HISTORICAL SCORECARD over {worlds} worlds (hit rate / band):");
        foreach (var l in lines)
        {
            var r = results.GetValueOrDefault(l.Name) ?? new List<string>();
            int hits = r.Count(x => x == "1"), n = r.Count(x => x != "NA");
            if (n == 0) { na++; Console.WriteLine($"  [ n/a] {l.Name,-34} did not arise in any world   ({l.Note})"); continue; }
            double rate = hits / (double)n;
            string verdict = rate < l.Min - 1e-9 ? "LOW " : rate > l.Max + 1e-9 ? "HIGH" : "ok  ";
            if (verdict == "ok  ") ok++; else if (verdict == "LOW ") low++; else high++;
            allW += l.Weight; if (verdict == "ok  ") okW += l.Weight;
            Console.WriteLine($"  [{verdict}] {l.Name,-34} {hits}/{n} = {rate:P0}  band {l.Min:P0}-{l.Max:P0}   {l.Note}");
        }
        Console.WriteLine($"  within band {ok}, too rare {low}, too common {high}, not arisen {na}; weighted score {(allW > 0 ? okW / allW : 0):P0}");
    }

    // ===============================================================================================
    private static int Validate(List<string> args)
    {
        if (Opt(args, "--seeds") is { } seedList) return ValidateSeeds(args, seedList);
        bool selftest = Flag(args, "--selftest");
        int days = int.Parse(Opt(args, "--days") ?? (selftest ? "365" : "60"));
        string project = ResolveProject(RequireProjectArg(args, "burger validate <project> [--days N]"));
        Log.EchoToConsole = false;
        Console.WriteLine($"Validating {GameDir(project)}");
        var sw = Stopwatch.StartNew();
        var game = Game.Load(GameDir(project), compileScripts: true, seed: int.Parse(Opt(args, "--seed") ?? "1234"));
        Console.WriteLine($"  loaded in {sw.ElapsedMilliseconds} ms: {game.World.Countries.Count} countries, {game.World.States.Count} states, " +
                          $"{game.World.Locations.Count} locations, {game.World.Characters.Count} characters, {game.World.Divisions.Count} divisions, " +
                          $"{game.Data.Events.Count} events, {game.Mods.Scripts.Count} C# scripts");
        PrintLog();
        int loadErrors = Log.ErrorCount;

        Console.WriteLine($"Simulating {days} days (all AI)...");
        if (Opt(args, "--bias") is { } biasText) game.HistoricalBias = double.Parse(biasText, System.Globalization.CultureInfo.InvariantCulture);
        game.StartNewGame(null);
        sw.Restart();
        int battles = 0, wars = 0;
        game.Mods.Add(new CountingScript(() => battles++, () => wars++));
        int errorsBefore = Log.ErrorCount;
        bool scorecard = args.Contains("--scorecard");
        string[] watchTags = (Opt(args, "--watch") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        ScorecardRecorder? recorder = null;
        if (scorecard) { recorder = new ScorecardRecorder(game); game.Mods.Add(recorder); }
        game.Profiling = args.Contains("--profile");
        int warsAtDecade = 0;
        for (int d = 0; d < days; d++)
        {
            for (int h = 0; h < 24; h++) game.Tick();
            recorder?.Daily();
            if (watchTags.Length > 0 && game.World.Date.Day == 1 && game.World.Date.Month % 6 == 1 && game.World.Date.Hour == 0 && game.Politics.Enabled)
                foreach (var wt in watchTags.Select(t => game.World.GetCountry(t)).Where(c => c != null))
                    Console.WriteLine($"  watch {game.World.Date.ToDisplay()} {wt!.Tag} {game.Politics.Of(wt).Ruling} stab {game.Rules.Politics.StabilityOf(game, wt):0} " +
                                      $"pop {string.Join("/", game.Politics.Ideologies.Select(i => game.Politics.Of(wt).Popularity.GetValueOrDefault(i.Key).ToString("P0")))} wealth {game.Politics.WealthIndex(wt):0.0} war {game.World.WarsOf(wt).Count()} exh {game.Diplomacy.WarExhaustion(wt):0.0} div {game.World.DivisionsOf(wt).Count()}");
            if (days >= 3650 && (d + 1) % 3650 == 0)
            {
                Console.WriteLine($"  year {(d + 1) / 365}: {wars - warsAtDecade} wars declared in the last 10 years, {game.World.Wars.Count} active, {game.World.Countries.Count(c => c.Exists)} countries");
                if (game.Politics.Enabled)
                {
                    var P = game.Politics;
                    var alive = game.World.Countries.Where(c => c.Exists).ToList();
                    var stab = alive.Select(c => game.Rules.Politics.StabilityOf(game, c)).OrderBy(v => v).ToList();
                    Console.WriteLine($"    rulers {string.Join(", ", alive.GroupBy(c => P.Of(c).Ruling).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}; " +
                                      $"stability lowest {stab[0]:0}, median {stab[stab.Count / 2]:0}, {stab.Count(v => v < 45)} under 45, {stab.Count(v => v < 30)} under 30; " +
                                      $"{P.CivilWarsStarted} civil wars, {P.Coups} coups, {game.World.Relations.Subjects.Count} subjects so far");
                }
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
        if (recorder != null)
        {
            var lines = ScorecardRecorder.Parse(Path.Combine(Path.GetDirectoryName(GameDir(project))!, "scorecard.txt"));
            if (Environment.GetEnvironmentVariable("BURGER_FACT_LOG") != null) Console.WriteLine("  factories (civilian + military):\n" + recorder.FactoryTable());
            foreach (var (name, result) in recorder.EvaluateAll(lines)) Console.WriteLine($"SCORE|{name}|{(result.HasValue ? result.Value.ToString() : "NA")}");
        }
        if (game.Profiling)
            Console.WriteLine("  time per system (ms per simulated day): " + string.Join(", ", game.ProfileMs.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value / days:0.00}")));
            Console.WriteLine("  longest single call (ms): " + string.Join(", ", game.ProfileMaxMs.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value:0.0}")));

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
            if (game.Features.Has(FeatureSet.Military) && game.Features.Has(FeatureSet.Diplomacy)) WarGoalTests(project);
            if (game.Features.Has(FeatureSet.Diplomacy) && game.Features.Has(FeatureSet.Population)) ReleaseTests(project);
            if (game.Features.Has(FeatureSet.Politics)) PoliticsFeatureTests(project);
            if (game.Features.Has(FeatureSet.Population)) PopulationFeatureTests(project);
            HistoricalFeatureTests(project);
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

    /// <summary>Growth, assimilation, conversion, cores, colonies and saving of pops (when installed).</summary>
    /// <summary>Casus belli and war goals, ticking war score, faction goals, war exhaustion and AI peace, pacts, liberty desire and
    /// independence wars, diplomatic plays, the end of a country (player becomes an observer, nothing of it keeps acting).</summary>
    /// <summary>Releases, separations, stranded land, names of governments and who a country accepts.</summary>
    /// <summary>Technology, commodities, demilitarized zones, AI profiles and the historical scorecard (each only where its feature is installed).</summary>
    private static void HistoricalFeatureTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 5);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var live = g.World.Countries.Where(c => c.Exists).ToList();
        if (g.Tech.Enabled)
        {
            var gated = g.Data.Equipment.Values.FirstOrDefault(e => e.Definition.GetList("requires_tech").Any());
            if (gated != null)
            {
                var need = gated.Definition.GetList("requires_tech").ToList();
                var c = live.FirstOrDefault(x => need.Any(t => !g.Tech.Has(x, t)));
                if (c != null)
                {
                    Check(!g.Tech.CanUse(c, gated.Definition), $"technology: {c.Tag} cannot use {gated.Key} before knowing {string.Join("+", need)}");
                    foreach (var t in need) g.Tech.Grant(c, t);
                    Check(g.Tech.CanUse(c, gated.Definition), $"technology: {c.Tag} can use {gated.Key} once it knows it");
                }
            }
            var withFactor = g.Tech.Defs.Values.FirstOrDefault(t => t.Modifier.Keys.Any(k => k.EndsWith("_factor", StringComparison.OrdinalIgnoreCase)));
            if (withFactor != null)
            {
                string key = withFactor.Modifier.Keys.First(k => k.EndsWith("_factor", StringComparison.OrdinalIgnoreCase));
                var c = live.FirstOrDefault(x => !g.Tech.Has(x, withFactor.Key) && withFactor.Requires.All(r => g.Tech.Has(x, r)));
                if (c != null)
                {
                    double before = c.StatFactor(key.Substring(0, key.Length - "_factor".Length));
                    g.Tech.Grant(c, withFactor.Key);
                    double after = c.StatFactor(key.Substring(0, key.Length - "_factor".Length));
                    Check(Math.Abs(after - before) > 1e-9, $"technology: {withFactor.Key} changes {c.Tag}'s {key} ({before:0.###} -> {after:0.###})");
                }
            }
            Check(live.Any(c => g.Tech.Count(c) > 0), "technology: countries start with the technology of their time");
        }
        if (g.Commodities.Enabled && g.Production.Enabled)
        {
            var e = g.Data.Equipment.Values.FirstOrDefault(x => g.Commodities.UseOf(x).Count > 0);
            var c = live.FirstOrDefault();
            if (e != null && c != null)
            {
                string k = g.Commodities.UseOf(e).Keys.First();
                var balance = g.Commodities.BalanceOf(c, k);
                double was = balance.Satisfied;
                balance.Satisfied = 0;
                double starved = g.Commodities.Factor(c, e);
                balance.Satisfied = 1;
                double fed = g.Commodities.Factor(c, e);
                balance.Satisfied = was;
                Check(starved < fed && fed >= 0.999, $"commodities: a shortage of {k} slows {e.Key} ({starved:0.00} against {fed:0.00})");
            }
        }
        if (g.Diplomacy.DemilEnabled)
        {
            var owner = live.FirstOrDefault(c => c.OwnedStates.Count > 2 && !g.World.WarsOf(c).Any() && c.Capital?.State != null);
            var enemy = owner == null ? null : Builtins.NeighborCountries(g, owner).FirstOrDefault(x => !g.World.WarsOf(x).Any());
            var state = owner?.OwnedStates.FirstOrDefault(x => x != owner.Capital?.State && !x.Demilitarized);
            if (owner != null && enemy != null && state != null)
            {
                g.Diplomacy.Demilitarize(state, enemy);
                Check(state.Demilitarized && g.Diplomacy.ImposedBy(state) == enemy, $"demilitarization: {state.Key} is demilitarized by {enemy.Tag}");
                string tmp = Path.Combine(Path.GetTempPath(), "burger_demil.sav");
                SaveGame.Save(g, tmp);
                var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 5);
                SaveGame.Load(g2, tmp);
                Check(g2.World.GetState(state.Key)?.Demilitarized == true, "demilitarization: the zone is saved and loaded");
                g.Diplomacy.DeclareWar(owner, enemy, null, null, force: true);
                for (int h = 0; h < 24 * 3; h++) g.Tick();
                Check(!state.Demilitarized, "demilitarization: the zone opens when its owner goes to war");
                var other = owner.OwnedStates.FirstOrDefault(x => x != state && x != owner.Capital?.State);
                if (other != null)
                {
                    g.Diplomacy.Demilitarize(other, null);
                    g.Diplomacy.Remilitarize(other, "decision");
                    Check(!other.Demilitarized, "demilitarization: the owner can remilitarize");
                }
            }
        }
        if (g.AiProfiles.Enabled)
        {
            var c = live.FirstOrDefault(x => g.AiProfiles.ProfileOf(x) != null);
            if (c != null)
            {
                g.HistoricalBias = 0;
                double none = g.Rules.AiProfile.HistoryWeight(g, c);
                g.HistoricalBias = 1;
                double full = g.Rules.AiProfile.HistoryWeight(g, c);
                Check(none <= 0 && full > 0, $"AI profiles: the historical bias slider scales the weight of {c.Tag}'s history ({none:0.##} -> {full:0.##})");
                Check(g.AiProfiles.PersonalityOf(c).Archetype.Length > 0, $"AI profiles: {c.Tag} has an archetype ({g.AiProfiles.PersonalityOf(c).Archetype})");
            }
        }
        if (g.Focus.Enabled)
        {
            var lint = g.Focus.Lint();
            if (project.Contains("Earth1930", StringComparison.OrdinalIgnoreCase)) Check(lint.Count == 0, "focus: the Earth1930 trees check no dates and make no war" + (lint.Count > 0 ? ": " + string.Join("; ", lint.Take(3)) : ""));
            var c = live.FirstOrDefault(x => g.Focus.TreeOf(x) is { Default: false } && g.Focus.FocusesOf(x).Any(f => g.Focus.CannotStart(x, f) == null));
            if (c != null)
            {
                var f = g.Focus.FocusesOf(c).First(x => g.Focus.CannotStart(c, x) == null);
                Check(g.Focus.Start(c, f) == null && g.Focus.Current(c) == f, $"focus: {c.Tag} starts {f.Key}");
                Check(g.Focus.CannotStart(c, g.Focus.FocusesOf(c).First(x => x != f)) != null, "focus: a country works on one focus at a time");
                for (int i = 0; i < 14 && g.Focus.Current(c) == f; i++) g.Focus.MonthlyTick();
                Check(g.Focus.IsDone(c, f.Key), $"focus: {f.Key} completes and gives its reward");
                string tmp = Path.Combine(Path.GetTempPath(), "burger_focus.sav");
                SaveGame.Save(g, tmp);
                var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 5);
                SaveGame.Load(g2, tmp);
                Check(g2.World.GetCountry(c.Tag) is { } c2 && g2.Focus.IsDone(c2, f.Key), "focus: completed focuses are saved and loaded");
            }
        }
        string card = Path.Combine(Path.GetDirectoryName(GameDir(project))!, "scorecard.txt");
        if (File.Exists(card)) Check(ScorecardRecorder.Parse(card).Count > 0, "scorecard: scorecard.txt parses");
    }

    private static void ReleaseTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 7);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var D = g.Diplomacy;
        var P = g.Population;
        // country names follow the government and dependence
        var named = g.World.Countries.FirstOrDefault(c => c.Exists && c.NameVariants.ContainsKey("communist") && g.World.Relations.OverlordOf(c) == null);
        if (named != null && g.Politics.Enabled)
        {
            g.Politics.SetRulingParty(named, "communist", "coup");
            Check(named.RawName == named.NameVariants["communist"], $"names: {named.Tag} is '{named.Name}' under communists");
            g.Politics.SetRulingParty(named, "democratic", "coup");
            Check(named.RawName == (named.NameVariants.GetValueOrDefault("democratic") ?? named.DefaultName), $"names: {named.Tag} is '{named.Name}' under democrats");
        }
        var dependent = g.World.Countries.FirstOrDefault(c => c.Exists && c.NameVariants.ContainsKey("subject") && g.World.Relations.OverlordOf(c) != null);
        if (dependent != null) Check(dependent.RawName == dependent.NameVariants["subject"], $"names: the dependency {dependent.Tag} is '{dependent.Name}'");
        if (g.World.GetCountry("RAJ") is { } raj && raj.Exists)
            Check(P.IsAccepted(raj, "punjabi") && !P.IsAccepted(raj, "bengali") && !P.IsAccepted(raj, "tamil"), "culture: the Raj accepts its own north-west, not all of India");
        // a people of the winner's own: its land goes to the winner, there is no second country of it
        var a = g.World.Countries.Where(c => c.Exists && c.Capital != null && Builtins.NeighborCountries(g, c).Count >= 1 && c.OwnedStates.Count >= 3).OrderByDescending(c => c.OwnedStates.Count).First();
        var b = Builtins.NeighborCountries(g, a).Where(x => x.OwnedStates.Count >= 3 && g.World.Relations.OverlordOf(x) == null).OrderByDescending(x => x.OwnedStates.Count).FirstOrDefault();
        if (b == null) return;
        foreach (var t in g.World.Relations.Truces.Keys.ToList()) g.World.Relations.Truces.Remove(t);
        g.World.Relations.SetAlliance(a, b, false);
        var war = D.DeclareWar(a, b, null, null, force: true);
        var st = b.OwnedStates.FirstOrDefault(s => s != b.Capital?.State && D.Touches(a, s));
        string? own = P.Of(a).Culture;
        if (war != null && st != null && own != null)
        {
            P.Of(st).Pops.Clear();
            P.Of(st).Pops.Add(new Burger.Core.Population.Pop { Culture = own, Size = 5000 });
            P.InvalidateMajorities();
            var plan = D.PlanRelease(war, b, own, a, a, false);
            Check(plan.Problem != null || plan.Parts.All(p => p.Outcome != Burger.Core.Diplomacy.DiplomacySystem.ReleaseOutcome.NewCountry),
                  $"release: the {own} land of {b.Tag} goes to {a.Tag}, not to a second country of its own people ({plan.Problem ?? string.Join(", ", plan.Parts.Select(p => p.Outcome))})");
            // the region of a people is one block, never the capital
            var other = P.CultureByKey.Keys.FirstOrDefault(k => !g.World.Countries.Any(c => c.Exists && P.Of(c).Culture == k) && !P.IsAccepted(b, k));
            if (other != null)
            {
                foreach (var s in b.OwnedStates.Where(s => s != b.Capital?.State).Take(2)) { P.Of(s).Pops.Clear(); P.Of(s).Pops.Add(new Burger.Core.Population.Pop { Culture = other, Size = 5000 }); }
                P.InvalidateMajorities();
                var parts = g.Rules.Population.SeparatistRegion(g, b, other, true);
                Check(parts.Count == 0 || (parts[0].States.All(s => s != b.Capital?.State) && P.Components(parts[0].States).Count == 1),
                      $"separation: the land of the {other} people is one block without the capital ({parts.FirstOrDefault()?.States.Count ?? 0} states)");
                // after the separation the old country is still in one piece
                if (parts.Count > 0 && b.Capital?.State is { } cap)
                {
                    var taken = parts.SelectMany(p => p.States).ToHashSet();
                    var rest = b.OwnedStates.Where(s => !taken.Contains(s)).ToList();
                    var linkedBefore = P.Reachable(cap, b.OwnedStates);
                    var linkedAfter = P.Reachable(cap, rest);
                    Check(linkedBefore.All(s => taken.Contains(s) || linkedAfter.Contains(s)), $"separation: {b.Tag} keeps no land cut off from its capital");
                }
            }
            // a people that has a country that cannot take the land is not made a second one
            var third = g.World.Countries.FirstOrDefault(c => c.Exists && c != a && c != b && P.Of(c).Culture != null && P.Of(c).Culture != own && g.Diplomacy.Opinion(a, c) < 25
                                                              && !D.Touches(c, st) && g.World.Relations.OverlordOf(c) == null);
            if (third != null)
            {
                string tc = P.Of(third).Culture!;
                P.Of(st).Pops.Clear();
                P.Of(st).Pops.Add(new Burger.Core.Population.Pop { Culture = tc, Size = 5000 });
                P.InvalidateMajorities();
                var plan2 = D.PlanRelease(war, b, tc, a, a, false);
                Check(plan2.Problem != null || plan2.Parts.All(p => p.Outcome != Burger.Core.Diplomacy.DiplomacySystem.ReleaseOutcome.NewCountry),
                      $"release: no second country of the {tc} people, {third.Tag} exists ({plan2.Problem ?? "land goes to it"})");
            }
        }
        // colors follow the government: Russia is red only as a communist state
        var tinted = g.World.Countries.FirstOrDefault(c => c.Exists && c.ColorVariants.ContainsKey("democratic") && c.ColorVariants.ContainsKey("communist") && !c.Dynamic);
        if (tinted != null && g.Politics.Enabled)
        {
            g.Politics.SetRulingParty(tinted, "democratic", "coup");
            Check(tinted.Color.Equals(tinted.ColorVariants["democratic"]), $"colors: {tinted.Tag} has its democratic color");
            g.Politics.SetRulingParty(tinted, "communist", "coup");
            Check(tinted.Color.Equals(tinted.ColorVariants["communist"]), $"colors: {tinted.Tag} has its communist color");
        }
        // land without cultural ties is let go (a puppet does not keep what it conquered; a country without its homeland loses its colonies)
        var foreignPeople = P.CultureByKey.Keys.FirstOrDefault(k => !g.World.Countries.Any(c => c.Exists && P.Of(c).Culture == k) && !P.IsAccepted(b, k));
        if (foreignPeople != null)
        {
            var abroad = b.OwnedStates.Where(s => s != b.Capital?.State).Take(2).ToList();
            foreach (var s in abroad) { s.Cores.Remove(b); P.Of(s).Pops.Clear(); P.Of(s).Pops.Add(new Burger.Core.Population.Pop { Culture = foreignPeople, Size = 5000 }); }
            P.InvalidateMajorities();
            Check(abroad.Count == 0 || abroad.All(s => !g.Rules.Population.HasCulturalTies(g, b, s)), $"ties: {b.Tag} has no ties to land of the {foreignPeople} people");
            if (abroad.Count > 0 && b.Capital?.State is { } hs) Check(g.Rules.Population.HasCulturalTies(g, b, hs) || P.MajorityCulture(hs) != P.Of(b).Culture, "ties: a country has ties to its own homeland");
            int let = abroad.Count > 0 ? D.DissolveHoldings(b, abroad) : 0;
            Check(abroad.Count == 0 || (let > 0 && abroad.All(s => s.Owner != b)), $"ties: {let} states without ties are let go");
        }
        // a civil war does not go on for ever: the side far ahead wins, and after three years the side ahead takes the country
        if (war != null)
        {
            var cw = new Burger.Core.Politics.CivilWar { War = war!, Origin = b, Rebel = a };
            war!.StartDate = g.World.Date.AddYears(-4);
            war.WarScore = 80; // the attacker (the rebels) far ahead
            Check(g.Rules.Politics.CivilWarVerdict(g, cw, false, false) == false, "civil war: rebels far ahead win");
            war.WarScore = -10;
            Check(g.Rules.Politics.CivilWarVerdict(g, cw, false, false) == true, "civil war: after three years the side ahead (the government) takes the country");
            war.StartDate = g.World.Date;
            Check(g.Rules.Politics.CivilWarVerdict(g, cw, false, false) == null, "civil war: a young war with a small lead is not decided");
        }
        // the home of a country is the block around its capital
        Check(b.Capital?.State is not { } cs || D.LinkedHome(b).Contains(cs), $"stranded: the home of {b.Tag} holds its capital");
    }

    private static void WarGoalTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 9);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var D = g.Diplomacy;
        var a = g.World.Countries.Where(c => c.Exists && Builtins.NeighborCountries(g, c).Count >= 2).OrderByDescending(c => c.OwnedStates.Count).First();
        var b = Builtins.NeighborCountries(g, a).Where(x => x.OwnedStates.Count >= 2).OrderByDescending(x => x.OwnedStates.Count).First();
        foreach (var t in g.World.Relations.Truces.Keys.ToList()) g.World.Relations.Truces.Remove(t);
        g.World.Relations.SetAlliance(a, b, false);
        // nobody takes land it cannot reach: landlocked land that does not touch its own
        {
            var far = g.World.States.FirstOrDefault(s => s.Owner != null && s.Owner != a && !s.Locations.Any(l => l.IsCoastal) && !D.Touches(a, s));
            var near = b.OwnedStates.FirstOrDefault(s => D.Touches(a, s));
            if (far != null) Check(!D.CanReach(a, far), $"peace: {a.Tag} cannot reach landlocked {far.DisplayName}, which does not touch its land");
            if (near != null) Check(D.CanReach(a, near), $"peace: {a.Tag} can reach {near.DisplayName} next to its land");
        }
        // two countries of one people are rivals
        if (g.Population.Enabled && g.Population.Of(a).Culture is { } people)
        {
            string? was = g.Population.Of(b).Culture;
            double liked = D.Opinion(a, b);
            g.Population.Of(b).Culture = people;
            Check(D.Opinion(a, b) < liked, $"opinion: countries of one people are rivals ({liked:0} -> {D.Opinion(a, b):0})");
            g.Population.Of(b).Culture = was;
        }
        // a lost core gives a cheap casus belli
        var core = b.OwnedStates.Where(s => s != b.Capital?.State).OrderBy(s => s.Index).First();
        core.Cores.Add(a);
        var cbs = D.ValidCasusBelli(a, b);
        var reclaim = cbs.FirstOrDefault(cb => cb.WarGoal == WarGoalType.Cores);
        Check(reclaim != null, $"war goals: a lost core gives {a.Tag} a casus belli on {b.Tag} ({string.Join(", ", cbs.Select(cb => cb.Key))})");
        if (reclaim == null) return;
        var war = D.DeclareWar(a, b, reclaim)!;
        Check(war != null && war.Goals.Count >= 1 && war.Goals[0].Holder == a && D.GoalStates(war.Goals[0]).Contains(core), "war goals: the war is about the core state");
        var goalTerm = new PeaceTerm { Type = PeaceTermType.TransferState, State = core, Beneficiary = a };
        double goalCost = D.TermCost(war!, goalTerm, a, b);
        var other = b.OwnedStates.FirstOrDefault(s => s != core && s != b.Capital?.State && !s.Cores.Contains(a) && !s.Claims.Contains(a));
        if (other != null)
        {
            double otherCost = D.TermCost(war!, new PeaceTerm { Type = PeaceTermType.TransferState, State = other, Beneficiary = a }, a, b);
            double size(State s) => s.Locations.Count;
            Check(goalCost / size(core) < otherCost / size(other), $"war goals: the goal state is cheaper to demand ({goalCost:0.0} for {size(core)} locations vs {otherCost:0.0} for {size(other)})");
        }
        // holding the goal ticks war score
        foreach (var l in core.Locations) g.Warfare.SetController(l, a, silent: true);
        for (int m = 0; m < 3; m++) D.MonthlyTick();
        Check(war!.TickingScore >= 3, $"war goals: holding the goal adds war score ({war.TickingScore:0})");
        // an ally with its own claim adds its war goal (faction war goals)
        var ally = Builtins.NeighborCountries(g, b).FirstOrDefault(x => x != a && !war.Involves(x));
        if (ally != null)
        {
            var claim = b.OwnedStates.FirstOrDefault(s => s != core && s != b.Capital?.State);
            if (claim != null)
            {
                claim.Claims.Add(ally);
                D.JoinWar(ally, war, true);
                Check(war.Goals.Any(x => x.Holder == ally && D.GoalStates(x).Contains(claim)), $"faction war goals: {ally.Tag} joined with its own claim on {claim.DisplayName}");
            }
        }
        // war exhaustion makes the loser accept more
        D.UpdateWarScores();
        double score = war.ScoreFor(a);
        var offer = new PeaceOffer { War = war, From = a, To = b, FromIsWinner = true };
        foreach (var st in b.OwnedStates.Where(s => s != b.Capital?.State).OrderByDescending(s => s == core))
        {
            offer.Terms.Add(new PeaceTerm { Type = PeaceTermType.TransferState, State = st, Beneficiary = a });
            if (D.OfferCost(offer) > Math.Max(0, score) + 15) break;
        }
        double cost = D.OfferCost(offer);
        D.SetWarExhaustion(b, 0);
        bool fresh = D.AiWouldAccept(offer);
        D.SetWarExhaustion(b, 18);
        bool tired = D.AiWouldAccept(offer);
        Check(D.InvalidReason(offer) != null || (!fresh && tired), $"war exhaustion: {b.Tag} refuses demands worth {cost:0} at war score {score:0} when fresh, accepts when exhausted (fresh {fresh}, tired {tired})");
        Check(g.Modifiers.Sum(b, "stability_target") < g.Modifiers.Sum(a, "stability_target") + 50 && D.ExhaustionModifierValue(b, "stability_target") < 0, "war exhaustion: it lowers stability");
        for (int m = 0; m < 2; m++) D.MonthlyTick();
        Check(D.WarExhaustion(a) > 0, $"war exhaustion: grows while at war ({a.Tag} {D.WarExhaustion(a):0.0})");
        D.EndWar(war);
        foreach (var t in g.World.Relations.Truces.Keys.ToList()) g.World.Relations.Truces.Remove(t);
        // non-aggression pacts
        D.CreatePact(a, b, 10);
        Check(g.Rules.Diplomacy.CannotDeclareWarReason(g, a, b, reclaim) != null, "pacts: a non-aggression pact blocks war");
        D.BreakPact(a, b);
        Check(g.Rules.Diplomacy.CannotDeclareWarReason(g, a, b, reclaim) == null && D.Opinion(b, a) < 0, "pacts: breaking it allows war again and angers them");
        // diplomatic play: cede the claimed state
        int before = a.OwnedStates.Count;
        g.RunEffect("cede_claimed_state = " + b.Tag, a);
        Check(a.OwnedStates.Count == before + 1 && core.Owner == a, $"diplomatic play: {b.Tag} ceded {core.DisplayName}");
        // liberty desire and a war of independence
        var type = g.Data.SubjectTypes.Values.First();
        double baseLd = type.LibertyDesire;
        g.World.Relations.SetSubject(b, a, type);
        type.LibertyDesire = 90;
        D.MonthlyTick();
        double ld = D.LibertyDesire(b);
        var indep = g.Data.CasusBelli.Values.FirstOrDefault(cb => cb.WarGoal == WarGoalType.Independence);
        Check(ld >= 50 && indep != null && g.Rules.Diplomacy.CannotDeclareWarReason(g, b, a, indep) == null, $"liberty desire: a subject at {ld:0}% may fight for independence");
        if (indep != null && ld >= 50)
        {
            var iw = D.DeclareWar(b, a, indep);
            var free = new PeaceOffer { War = iw!, From = b, To = a, FromIsWinner = true };
            free.Terms.Add(new PeaceTerm { Type = PeaceTermType.Independence, Beneficiary = b });
            Check(iw != null && D.InvalidReason(free) == null, "liberty desire: the war of independence can end with independence" + (iw != null ? $" ({D.InvalidReason(free)})" : ""));
            if (iw != null) { D.ApplyPeace(free); Check(g.World.Relations.OverlordOf(b) == null, $"liberty desire: {b.Tag} is free"); }
        }
        type.LibertyDesire = baseLd;
        // the end of a country: nothing keeps acting, and a player becomes an observer
        foreach (var t in g.World.Relations.Truces.Keys.ToList()) g.World.Relations.Truces.Remove(t);
        var victim = g.World.Countries.Where(c => c.Exists && c != a).OrderByDescending(c => g.Navy.FleetsOf(c).Count() + g.World.Divisions.Count(d => d.Owner == c)).First();
        g.SetPlayer(victim.Tag);
        g.Diplomacy.Annex(a, victim);
        Check(!victim.Exists && g.World.Player == null && !g.Navy.FleetsOf(victim).Any() && !g.World.Divisions.Any(d => d.Owner == victim) && !g.Air.WingsOf(victim).Any(),
              $"elimination: {victim.Tag} lost everything, its fleets, wings and divisions are gone and the player is an observer");
        // the ledger knows the world total
        g.Statistics.MonthlyTick();
        Check(g.Statistics.WorldSeries("states").Last() > 0, "ledger: world totals");
        // subjects have no subjects: what a subject subjugates goes to its overlord
        {
            var R = g.World.Relations;
            var three = g.World.Countries.Where(c => c.Exists && R.OverlordOf(c) == null && !R.SubjectsOf(c).Any()).Take(3).ToList();
            var st = g.Data.SubjectTypes.Values.FirstOrDefault();
            if (three.Count == 3 && st != null)
            {
                D.MakeSubject(three[1], three[0], st);
                D.MakeSubject(three[2], three[1], st);
                Check(R.OverlordOf(three[2]) == three[0] && D.TopOverlord(three[2]) == three[0],
                      $"subjects: {three[2].Tag}, subjugated by the subject {three[1].Tag}, answers to its overlord {three[0].Tag}");
            }
        }
        // far coasts are only for navies: a country without ships reaches a short hop over sea, each ship adds range
        if (g.Navy.Enabled)
        {
            var sailor = g.World.Countries.Where(c => c.Exists).OrderByDescending(c => g.Rules.Diplomacy.NavalStrength(g, c)).First();
            var landlubber = g.World.Countries.Where(c => c.Exists && c.OwnedStates.Any(s => s.Locations.Any(l => l.IsCoastal))).OrderBy(c => g.Rules.Diplomacy.NavalStrength(g, c)).First();
            Check(g.Rules.Diplomacy.SeaReachKm(g, sailor) > g.Rules.Diplomacy.SeaReachKm(g, landlubber) || sailor == landlubber,
                  $"peace: the navy of {sailor.Tag} reaches {g.Rules.Diplomacy.SeaReachKm(g, sailor):0} km over sea, {landlubber.Tag} only {g.Rules.Diplomacy.SeaReachKm(g, landlubber):0} km");
            var farCoast = g.World.States.Where(s => s.Owner != null && s.Owner != landlubber && s.Locations.Any(l => l.IsCoastal) && !D.Touches(landlubber, s))
                .OrderByDescending(s => D.SeaDistanceKm(landlubber, s)).FirstOrDefault();
            if (farCoast != null && D.SeaDistanceKm(landlubber, farCoast) > g.Rules.Diplomacy.SeaReachKm(g, landlubber))
                Check(!D.CanReach(landlubber, farCoast), $"peace: {landlubber.Tag} cannot take the coast of {farCoast.DisplayName}, {D.SeaDistanceKm(landlubber, farCoast):0} km away over sea");
        }
    }

    private static void PopulationFeatureTests(string project)
    {
        var g = Game.Load(GameDir(project), compileScripts: true, seed: 21);
        g.StartNewGame(null);
        g.Ai.Enabled = false;
        var P = g.Population;
        var land = g.World.States.Where(s => s.Locations.Any(l => l.IsLand)).ToList();
        Check(land.All(s => P.TotalOf(s) > 0) && g.World.Countries.Where(c => c.Exists).All(c => P.Of(c).Culture != null), "population: every land state has people and every country a culture");
        var c = g.World.Countries.Where(x => x.Exists).OrderByDescending(x => x.OwnedStates.Count).First();
        var st = c.OwnedStates.OrderBy(s => s.Index).First();
        double before = P.TotalOf(st);
        // a foreign minority of another culture group and religion
        var foreign = P.CultureByKey.Values.FirstOrDefault(cu => !P.IsAccepted(c, cu.Key));
        var otherFaith = P.ReligionEnabled ? P.ReligionByKey.Keys.FirstOrDefault(r => r != P.Of(c).Religion) : null;
        if (foreign != null)
        {
            g.RunEffect($"add_pops = {{ culture = {foreign.Key} {(otherFaith != null ? $"religion = {otherFaith}" : "")} size = {before * 0.5:0} }}", st);
            // shares of the state's people (the state may grow or take in migrants meanwhile)
            double f0 = P.Of(st).Pops.Where(p => p.Culture == foreign.Key).Sum(p => p.Size) / P.TotalOf(st);
            double r0 = otherFaith != null ? P.Of(st).Pops.Where(p => p.Religion == otherFaith).Sum(p => p.Size) / P.TotalOf(st) : 0;
            for (int h = 0; h < 24 * 365; h++) g.Tick();
            double f1 = P.Of(st).Pops.Where(p => p.Culture == foreign.Key).Sum(p => p.Size) / P.TotalOf(st);
            Check(f1 < f0, $"population: a foreign minority assimilates ({f0:P1} -> {f1:P1} of the state in a year)");
            if (otherFaith != null)
            {
                double r1 = P.Of(st).Pops.Where(p => p.Religion == otherFaith).Sum(p => p.Size) / P.TotalOf(st);
                Check(r1 < r0, $"religion: people of another faith convert ({r0:P1} -> {r1:P1})");
            }
            // colonies are not assimilated or converted
            var colony = c.OwnedStates.FirstOrDefault(s => s != st && s != c.Capital?.State && P.HasPops(s));
            if (colony != null)
            {
                colony.Category = "colony";
                Check(P.IsColony(colony) && g.Rules.Population.YearlyAssimilation(g, colony, P.Of(colony).Pops[0]) == 0
                      && g.Rules.Population.YearlyConversion(g, colony, P.Of(colony).Pops[0]) == 0, "population: colonies are not assimilated or converted");
                colony.Category = "";
            }
        }
        else for (int h = 0; h < 24 * 365; h++) g.Tick();
        Check(g.World.States.Sum(P.TotalOf) > 0 && Math.Abs(P.TotalOf(st) - before) > 0.5, $"population: people grow or move ({Burger.Core.Population.PopulationSystem.Format(before)} -> {Burger.Core.Population.PopulationSystem.Format(P.TotalOf(st))})");
        if (P.CoresEnabled)
        {
            // a conquered state of a foreign culture becomes a core only after core_years
            var other = g.World.Countries.First(x => x.Exists && x != c);
            var target = other.OwnedStates.First();
            g.Diplomacy.TransferState(target, c);
            target.Cores.Remove(c);
            for (int h = 0; h < 24 * 31; h++) g.Tick();
            bool foreignMajority = P.MajorityCulture(target) is { } mc && !P.IsAccepted(c, mc);
            Check(!foreignMajority || !target.Cores.Contains(c), "cores: a newly conquered foreign state is not a core");
            P.Of(target).OwnedSinceDay -= (int)(g.Data.Defines.Get("core_years", 25) * 365) + 1;
            for (int h = 0; h < 24 * 31; h++) g.Tick();
            Check(!foreignMajority || !target.Cores.Contains(c), "cores: no core after core_years while under half the people are of our cultures");
            // once most people there are of our culture (and we have held it long enough) it becomes a core
            string own = P.Of(c).Culture!;
            var tp = P.Of(target).Pops;
            foreach (var p in tp) if (!P.IsAccepted(c, p.Culture)) p.Culture = own;
            for (int h = 0; h < 24 * 31; h++) g.Tick();
            Check(target.Cores.Contains(c), $"cores: a core once over half the people are of our cultures ({P.AcceptedShare(target, c):P0})");
        }
        if (P.ColonizationEnabled)
        {
            // make an owned border state unowned, then settle it from the neighbour
            var empty = g.World.States.FirstOrDefault(s => s.Owner != null && s.Owner != c && s.Owner.OwnedStates.Count > 1 && P.NeighbourStates(s).Any(n => n.Owner == c));
            if (empty != null)
            {
                var old = empty.Owner!;
                empty.Owner = null;
                foreach (var l in empty.Locations) l.Controller = null;
                g.World.RefreshOwnership();
                c.Gold += 1000;
                var reason = P.Colonize(c, empty);
                Check(reason == null, "colonization: colonists are sent to unowned land" + (reason != null ? $" ({reason})" : ""));
                for (int m = 0; m < 60 && empty.Owner == null; m++) for (int h = 0; h < 24 * 30; h++) g.Tick();
                Check(empty.Owner == c, $"colonization: the colony became a state of {c.Name}");
            }
        }
        if (P.IndependenceEnabled)
        {
            // a state whose people are all of an unaccepted culture forms an independence movement, then breaks away
            var sep = c.OwnedStates.Where(s => s != c.Capital?.State && P.HasPops(s)).OrderByDescending(P.TotalOf).FirstOrDefault(); // a big state: the movement needs a share of the country
            var cult = P.CultureByKey.Values.FirstOrDefault(cu => !P.IsAccepted(c, cu.Key) && cu.Group.Key != "auto_cultures");
            if (sep != null && cult != null)
            {
                foreach (var p in P.Of(sep).Pops) p.Culture = cult.Key;
                for (int h = 0; h < 24 * 31; h++) g.Tick();
                Check(P.IndependenceOf(c).Any(m => m.Culture == cult.Key), $"independence: a {cult.Name} movement forms in {sep.DisplayName}");
                var free = P.DeclareIndependence(c, cult.Key);
                Check(free != null && free.Exists && sep.Owner == free, $"independence: {free?.Name} broke away from {c.Name}");
                if (free != null && g.Features.Has(FeatureSet.Military) && g.Features.Has(FeatureSet.Diplomacy))
                    Check(g.World.AtWar(free, c) && P.InIndependenceWar(c) && g.World.Divisions.Any(d => d.Owner == free), "independence: it fights a war of independence with its own army");
            }
        }
        string tmp = Path.Combine(Path.GetTempPath(), "burger_population.sav");
        SaveGame.Save(g, tmp);
        var g2 = Game.Load(GameDir(project), compileScripts: true, seed: 21);
        SaveGame.Load(g2, tmp);
        double t1 = g.World.States.Sum(P.TotalOf), t2 = g2.World.States.Sum(g2.Population.TotalOf);
        Check(Math.Abs(t1 - t2) < 1 + t1 * 1e-6 && g2.Population.Of(g2.World.GetCountry(c.Tag)!).Culture == P.Of(c).Culture, "population: pops and cultures are saved");
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
            // a hugely popular shut-out ideology takes power in a coup when the government has little support
            var dict = P.Ideologies.FirstOrDefault(i => !i.Elections);
            var coupLand = alive.FirstOrDefault(x => x != c && x != target && x.Exists);
            if (dict != null && coupLand != null)
            {
                P.SetRulingParty(coupLand, dict.Key);
                var rival = P.Ideologies.First(i => i.Key != dict.Key);
                P.AddPopularity(coupLand, rival.Key, 0.85 - P.PopularityOf(coupLand, rival.Key));
                P.Of(coupLand).CalmUntilDay = int.MinValue;
                for (int m = 0; m < 36 && P.Of(coupLand).Ruling == dict.Key; m++) g.Politics.MonthlyTick();
                Check(P.Of(coupLand).Ruling != dict.Key && !P.InCivilWar(coupLand), $"coups: {rival.Name} with 85% support took power in {coupLand.Name} without a civil war (now {P.Of(coupLand).Ruling})");
            }
            if (g.Features.Has(FeatureSet.Military) && g.Features.Has(FeatureSet.Diplomacy))
            {
                var ide = P.Ideologies.First(i => i.Key != P.Of(c).Ruling).Key;
                int statesBefore = c.OwnedStates.Count;
                int divsBefore = g.World.Divisions.Count(d => d.Owner == c && d.VolunteerFrom == null);
                var rebel = P.StartCivilWar(c, ide, 0.4);
                if (rebel != null)
                {
                    int rd = g.World.Divisions.Count(d => d.Owner == rebel);
                    Check(Math.Abs(rd - divsBefore * 0.4) <= 1.5, $"civil war: the rebels get their share of the army ({rd} of {divsBefore} divisions at 40% support)");
                    g.Statistics.MonthlyTick();
                    double ledgerStates = g.Statistics.Series("states", c.Tag).Last();
                    Check(Math.Abs(ledgerStates - statesBefore) < 0.5 && double.IsNaN(g.Statistics.Series("states", rebel.Tag).Last()),
                          $"ledger: rebels count as part of {c.Name} during the civil war ({ledgerStates} states)");
                }
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
        // parties of minorities win seats but never govern (population)
        var Pop = g.Population;
        var demo2 = alive.FirstOrDefault(x => x.Exists && P.HoldsElections(x));
        if (Pop.Enabled && demo2 != null)
        {
            var foreign = Pop.CultureByKey.Values.FirstOrDefault(cu => !Pop.IsAccepted(demo2, cu.Key) && cu.Group.Key != "auto_cultures");
            var st = demo2.OwnedStates.FirstOrDefault(x => Pop.HasPops(x) && !Pop.IsColony(x)); // colonies do not vote
            if (foreign != null && st != null)
            {
                g.RunEffect($"add_pops = {{ culture = {foreign.Key} size = {Pop.TotalOf(demo2) * 0.4:0} }}", st);
                P.HoldElection(demo2);
                string key = "culture:" + foreign.Key;
                Check(P.Of(demo2).Seats.GetValueOrDefault(key) > 0 && P.Of(demo2).Government.All(x => P.Ideology(x) != null),
                      $"parties: the {P.PartyName(demo2, key)} won {P.Of(demo2).Seats.GetValueOrDefault(key)} seats and is not in government");
            }
        }
        // culture policy laws
        if (Pop.Enabled && P.LawsEnabled && P.Laws.TryGetValue("equal_rights", out var eq))
        {
            var cc = alive.First(x => x.Exists);
            P.Enact(cc, eq, free: true);
            Check(g.Modifiers.Sum(cc, "minority_output") > 0 && g.Modifiers.Sum(cc, "culture_tension") < 0, "culture policy: Equal Rights raises minority output and lowers tension");
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
