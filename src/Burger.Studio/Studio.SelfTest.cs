using System.Text;
using Burger.Core;

namespace Burger.Studio;

/// <summary>
/// Automated test of the editing operations (run with --selftest on a throwaway copy of a project):
/// creates assets of every kind, edits fields, duplicates, deletes, edits states, saves, then loads the game
/// with C# scripts and reports every error. Results go to studio_selftest.txt next to the exe.
/// </summary>
public sealed partial class Studio
{
    public void RunSelfTest()
    {
        var sb = new StringBuilder();
        int fails = 0;
        void Check(bool ok, string what) { sb.AppendLine((ok ? "[pass] " : "[FAIL] ") + what); if (!ok) fails++; }
        try
        {
            string mainResFile = TargetFilesOf(Specs.Resources).FirstOrDefault() ?? Specs.Resources.DefaultFile;
            var res = DoCreateBlockAsset(Specs.Resources, "piety");
            Check(res != null && res.File.Equals(mainResFile, StringComparison.OrdinalIgnoreCase) && !Store!.Exists("common/resources/piety.txt"), "create resource (added to the main resources file)");
            if (res != null)
            {
                SetField(res, "start", "5");
                SetField(res, "color", "{ 10 20 30 }");
                SetField(res, "name", "\"Piety Points\"");
                SetField(res, "top_bar", null);
                var n = Catalog!.NodeOf(Catalog.Refresh(res)!)!;
                Check(n.GetString("start") == "5" && n.GetString("name") == "Piety Points" && n.Get("top_bar") == null, "edit resource fields");
            }
            int countryFiles = TargetFilesOf(Specs.Countries).Count;
            var tst = DoCreateBlockAsset(Specs.Countries, "TST");
            Check(tst != null && TargetFilesOf(Specs.Countries).Count == countryFiles, "create country (no new file)");
            if (tst != null) SetField(tst, "color", "{ 200 30 30 }");
            var ev = DoCreateBlockAsset(Specs.Events, "studiotest.1", "country_event");
            Check(ev != null && ev.Type == "country_event", "create event");
            var ch = DoCreateBlockAsset(Specs.Events, "studiotest.2", "character_event");
            Check(ch != null && ch.Type == "character_event", "create character event");
            var evFile = DoCreateEventFile("events", "studio_file_events");
            Check(evFile != null && evFile.Id == "studio_file_events.1" && evFile.File == "events/studio_file_events.txt", "create event file with a starter event");
            var ev2 = DoCreateBlockAsset(Specs.Events, NextEventId("events/studio_file_events.txt"), "character_event", "events/studio_file_events.txt");
            Check(ev2 != null && ev2.Id == "studio_file_events.2" && ev2.File == "events/studio_file_events.txt", "new event inside an existing event file");
            var mm = DoCreateBlockAsset(Specs.Interface, "studio_mode", "map_mode");
            Check(mm != null && mm.Type == "map_mode", "create map mode");
            var pn = DoCreateBlockAsset(Specs.Interface, "studio_panel", "panel");
            Check(pn != null && pn.Type == "panel", "create window");
            var ut = DoCreateBlockAsset(Specs.UnitTypes, "studio_unit");
            Check(ut != null, "create unit type (inside unit_types)");
            var dt = DoCreateBlockAsset(Specs.DivisionTemplates, "studio_division");
            Check(dt != null, "create division template");
            foreach (var k in new[] { Specs.Modifiers, Specs.Decisions, Specs.ScriptValues, Specs.OnActions, Specs.Characters, Specs.Terrain,
                         Specs.DiplomaticActions, Specs.CharacterInteractions, Specs.CasusBelli, Specs.SubjectTypes, Specs.Traits })
            {
                string id = k == Specs.OnActions ? "on_yearly_pulse" : "studio_" + Singular(k).Replace(' ', '_');
                if (k == Specs.OnActions && Catalog!.Find(k, id) != null) id = "on_studio_test";
                Check(DoCreateBlockAsset(k, id) != null, "create " + Singular(k));
            }
            var dec = Catalog!.Of(Specs.Decisions).FirstOrDefault(b => b.Id != "studio_decision");
            if (dec != null)
            {
                var copy = DoDuplicateBlock(dec, dec.Id + "_copy");
                Check(copy != null && copy.File == dec.File, "duplicate decision");
            }
            var evDup = DoDuplicateBlock(Catalog.Find(Specs.Events, "studiotest.1")!, "studiotest.3");
            Check(evDup != null, "duplicate event (id inside the block)");
            var utDup = DoDuplicateBlock(Catalog.Find(Specs.UnitTypes, "studio_unit")!, "studio_unit2");
            Check(utDup != null, "duplicate unit type (inside unit_types)");
            var del = Catalog.Find(Specs.Traits, "studio_trait");
            if (del != null)
            {
                Store!.Edit(del.File, t => Burger.Core.Script.ScriptPatch.Remove(t, del.File, del.Path), this);
                Catalog.Invalidate();
                Check(Catalog.Find(Specs.Traits, "studio_trait") == null, "delete trait");
            }

            // map: give TST a state, move a location
            if (_world != null)
            {
                var st = _world.World.States.First(s => s.Owner != null && s.Locations.Count >= 2);
                var other = _world.World.States.First(s => s != st && s.Owner != null);
                var loc = other.Locations[0];
                SetStateOwner(st, CountryOf("TST"));
                MoveToState(loc, st);
                FlushPendingStates();
                var file = Store!.Parse(st.SourceFile);
                var stNode = file.Get(st.Key);
                Check(stNode?.GetString("owner") == "TST" && stNode.GetList("locations").Contains(loc.Key), "state owner + paint written to file");
                Check(!(file.Get(other.Key)?.GetList("locations").Contains(loc.Key) ?? false), "location removed from the old state");
                if (tst != null) SetField(Catalog.Refresh(tst)!, "capital", st.Locations[0].Key);
            }

            // backend C#: compiled at load time while developing
            EnsureBackend();
            Store!.Set(BackendRoot + "/LockedRules.cs", "public class LockedRules : GameScript\n{\n    public override void OnLoad() => Game.Script.RegisterTrigger(\"backend_ok\", (ctx, n) => true);\n}\n", this);
            Store.Set("scripts/UsesBackend.cs", "public class UsesBackend : GameScript\n{\n    public override void OnLoad() { var r = new LockedRules(); }\n}\n", this);

            // features: remove and re-add a package, add a custom feature
            SaveAll();
            if (Burger.Core.Modding.FeaturePackages.Installed(Store!.ProjectDir) is { } feats && feats.Contains("combat_eu4"))
            {
                Burger.Core.Modding.FeaturePackages.Uninstall(Store.ProjectDir, "combat_eu4");
                bool gone = !File.Exists(Path.Combine(Store.ProjectDir, "backend", "Combat_EU4.cs")) && !Burger.Core.Modding.FeaturePackages.Installed(Store.ProjectDir)!.Contains("combat_eu4");
                Burger.Core.Modding.FeaturePackages.Install(Store.ProjectDir, new[] { "combat_eu4" });
                bool back = File.Exists(Path.Combine(Store.ProjectDir, "backend", "Combat_EU4.cs")) && Burger.Core.Modding.FeaturePackages.Installed(Store.ProjectDir)!.Contains("combat_eu4");
                Check(gone && back, "remove and re-add a feature (Combat: EU style)");
                Burger.Core.Modding.FeaturePackages.SetDefine(Store.GameDir, "combat_model", "hoi4");
            }
            Burger.Core.Modding.FeaturePackages.Install(Store.ProjectDir, new[] { "studio_custom" });
            // military features with install steps that read the map (fleets, air regions, bases)
            var milFeatures = new[] { "production", "supply_hubs", "navy", "air", "battle_plans", "lend_lease" };
            Burger.Core.Modding.FeaturePackages.Install(Store.ProjectDir, milFeatures);
            var nowInstalled = Burger.Core.Modding.FeaturePackages.Installed(Store.ProjectDir) ?? new List<string>();
            Check(milFeatures.All(nowInstalled.Contains) && File.Exists(Path.Combine(Store.GameDir, "map", "air_regions.txt")) && File.Exists(Path.Combine(Store.GameDir, "common", "ships", "00_ships.txt")),
                "add production, supply, navy, air, battle plans and lend-lease (files and air regions written)");
            Store.ReloadAll();
            Log.Clear();
            Game? g = null;
            try { g = Game.Load(Store!.GameDir, compileScripts: true, seed: 1); }
            catch (Exception ex) { Check(false, "game loads: " + ex.Message); }
            var errors = Log.Entries.Where(e => e.Level == LogLevel.Error).ToList();
            Check(g != null && errors.Count == 0, $"game loads after all edits ({errors.Count} errors)");
            foreach (var e in errors.Take(30)) sb.AppendLine("    " + e);
            if (g != null)
            {
                Check(g.World.GetCountry("TST")?.OwnedStates.Count > 0, "new country owns land");
                Check(g.Resources.Defs.Any(r => r.Key == "piety"), "new resource is loaded");
                Check(g.Data.Events.ContainsKey("studiotest.3"), "duplicated event is loaded");
                Check(g.Data.UnitTypes.ContainsKey("studio_unit2"), "duplicated unit type is loaded");
                Check(g.Interface.MapModes.Any(m => m.Id == "studio_mode"), "new map mode is loaded");
                Check(g.Mods.BackendAssembly != null && g.Script.HasTrigger("backend_ok"), "backend C# compiled and running");
                Check(g.Mods.Scripts.Any(x => x.GetType().Name == "UsesBackend"), "a moddable script can use backend classes");
                bool defaults = Burger.Core.Modding.FeaturePackages.Installed(Store!.ProjectDir)?.Contains("ai") == true;
                Check(g.Features.Has("studio_custom"), "custom feature recorded (has_feature)");
                Check(!defaults || (g.Rules.Warfare.GetType().Name == "GameWarfareRules" && g.Rules.Vision.GetType().Name == "GameVisionRules"
                                    && g.Rules.Economy.GetType().Name == "GameEconomyRules" && g.Rules.Diplomacy.GetType().Name == "GameDiplomacyRules"
                                    && g.Rules.Characters.GetType().Name == "GameCharacterRules" && g.Ai.GetType().Name == "GameAi"
                                    && g.Combat.Get("hoi4").GetType().Assembly == g.Mods.BackendAssembly), "default backend files (combat, warfare, economy, diplomacy, characters, AI) are used");
                Check(g.Rules.Navy.GetType().Name == "GameNavyRules" && g.Rules.Air.GetType().Name == "GameAirRules" && g.Rules.Support.GetType().Name == "GameSupportRules", "navy, air and support backend files are used");
                Check(g.Air.Regions.Count > 0 && g.Data.ShipTypes.Count > 0 && g.Data.PlaneTypes.Count > 0, $"air regions ({g.Air.Regions.Count}), ship and plane types are loaded");
                g.StartNewGame("TST");
                Check(g.Navy.Fleets.Count > 0 && g.Air.Wings.Count > 0, $"starting fleets ({g.Navy.Fleets.Count}) and air wings ({g.Air.Wings.Count}) are created");
                for (int h = 0; h < 24 * 60; h++) g.Tick();
                int simErrors = Log.Entries.Count(e => e.Level == LogLevel.Error);
                Check(simErrors == 0, $"60 days simulated with the new content ({simErrors} errors)");
                foreach (var e in Log.Entries.Where(e => e.Level == LogLevel.Error).Take(20)) sb.AppendLine("    " + e);
            }
        }
        catch (Exception ex)
        {
            fails++;
            sb.AppendLine("[FAIL] exception: " + ex);
        }
        sb.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "studio_selftest.txt"), sb.ToString());
    }
}
