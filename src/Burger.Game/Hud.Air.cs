using Burger.Core;
using Burger.Core.Air;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The air menu: wings, missions over air regions, air bases and new wings (air feature).</summary>
public sealed partial class Hud
{
    public int AirTab;

    private static readonly AirMission[] Missions = { AirMission.Superiority, AirMission.CloseSupport, AirMission.Bombing };
    private static string MissionShort(AirMission m) => m switch { AirMission.Superiority => "Superiority", AirMission.CloseSupport => "CAS", AirMission.Bombing => "Bombing", _ => "None" };

    /// <summary>Map click while a wing waits for an order: assign the clicked region, or move to the clicked air base.</summary>
    private void WingMapOrder(AirWing w, Location loc)
    {
        if (!G.Air.Wings.Contains(w)) { Ui.OrderingWing = null; return; }
        if (Ui.WingMission is { } m)
        {
            var region = G.Air.RegionOf(loc);
            if (region == null) { Ui.SetStatus("No air region there"); return; }
            var err = G.Air.Assign(w, region, m);
            Ui.SetStatus(err ?? $"{w.Name}: {AirSystem.MissionName(m)} over {region.DisplayName}");
            if (err == null) { Ui.OrderingWing = null; Map.Dirty = true; }
        }
        else
        {
            if (loc.State == null) return;
            var err = G.Air.Rebase(w, loc.State);
            Ui.SetStatus(err ?? $"{w.Name} moved to {loc.State.DisplayName}");
            if (err == null) Ui.OrderingWing = null;
        }
    }

    private void StartWingOrder(AirWing w, AirMission? m)
    {
        Ui.OrderingWing = w;
        Ui.WingMission = m;
        if (Map.Mode != MapMode.Air) { Map.Mode = MapMode.Air; Map.CustomMode = null; }
        Map.Dirty = true;
        Ui.SetStatus(m != null ? $"Click an air region in range for {AirSystem.MissionName(m.Value)} (right-click to cancel)" : "Click a state with an air base (right-click to cancel)");
    }

    private void DrawAirPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.Air)) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 450 * S;
        Gui.Panel(r, "Air");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var A = G.Air;
        var wings = A.WingsOf(player).ToList();
        Gui.Label($"Wings: {wings.Count}   Planes: {wings.Sum(x2 => x2.Planes):0}", x, y, 15);
        string sky = "";
        if (player.Capital?.State is { } cs && A.RegionOf(cs) is { } home)
        {
            var (share, contested) = A.Superiority(home, player);
            sky = contested ? $"Home sky: {share:P0}" : "Home sky: quiet";
        }
        var sm = Gui.Measure(sky, 15);
        Gui.Label(sky, x + w - sm.X, y, 15, Gui.TextDim);
        y += 28 * S;
        int tab = AirTab;
        Tabs(x, y, w, new[] { "Wings", "New wings & bases" }, ref tab);
        AirTab = tab;
        y += 36 * S;
        if (tab == 0) DrawWingList(player, wings, r, x, y, w);
        else DrawWingBuilding(player, r, x, y, w);
    }

    private void DrawWingList(Country c, List<AirWing> wings, Rectangle r, float x, float y, float w)
    {
        var A = G.Air;
        if (wings.Count == 0)
        {
            Gui.Wrapped("You have no air wings. Make one in the 'New wings & bases' tab.", x, y, w, 14, Gui.TextDim);
            return;
        }
        float rowH = 104 * S;
        float bottom = Ui.OrderingWing != null ? 54 * S : 10 * S;
        float top = Gui.BeginScroll("wings", new Rectangle(x, y, w, r.Y + r.Height - y - bottom), wings.Count * (rowH + 5 * S));
        float wy = top;
        foreach (var wing in wings.ToList())
        {
            var box = new Rectangle(x, wy, w - 8 * S, rowH);
            bool ordering = Ui.OrderingWing == wing;
            Card(box, ordering ? new Color(40, 52, 66, 255) : new Color(36, 38, 44, 255), ordering);
            Gui.Label(Gui.Fit(wing.Name, box.Width - 130 * S, 16, FontKind.Bold), box.X + 8 * S, box.Y + 5 * S, 16, Gui.Gold, FontKind.Bold);
            var bar = new Rectangle(box.X + box.Width - 120 * S, box.Y + 10 * S, 70 * S, 9 * S);
            Gui.Bar(bar, (float)(wing.Planes / A.WingSize), new Color(120, 200, 250, 255));
            Gui.Label($"{wing.Planes:0}/{A.WingSize:0}", bar.X + bar.Width + 6 * S, box.Y + 5 * S, 13, Gui.TextDim);
            if (Gui.Hover(bar)) Gui.Tooltip(Has(FeatureSet.Production) ? "Planes. Lost planes are replaced from the stockpile." : "Planes. Lost planes are replaced slowly" + (Has(FeatureSet.Economy) ? " (costs gold)." : "."));
            string where = $"{wing.Type.Name} at {wing.Base.DisplayName}";
            Gui.Label(Gui.Fit(where, box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 27 * S, 13);
            string task = wing.Region == null || wing.Mission == AirMission.None ? "Idle" : $"{AirSystem.MissionName(wing.Mission)} over {wing.Region.DisplayName}" + (wing.Active ? "" : " (out of range or no base)");
            Gui.Label(Gui.Fit(task, box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 45 * S, 13, wing.Region == null ? Gui.TextDim : wing.Active ? Gui.Good : Gui.Bad);
            // missions
            float bx = box.X + 8 * S, by = box.Y + rowH - 30 * S, bw = 82 * S;
            foreach (var m in Missions)
            {
                if (!A.CanFly(wing.Type, m)) continue;
                bool on = wing.Mission == m && wing.Region != null || ordering && Ui.WingMission == m;
                if (Gui.Button(new Rectangle(bx, by, bw, 24 * S), MissionShort(m), true, $"{AirSystem.MissionName(m)}: pick an air region on the map\nRange: {wing.Type.Range:0} km from the air base", on, 13))
                    StartWingOrder(wing, m);
                bx += bw + 4 * S;
            }
            float rx = box.X + box.Width - 8 * S;
            if (Gui.Button(new Rectangle(rx - 46 * S, by, 46 * S, 24 * S), "Stop", wing.Region != null, "Stop flying (stay at the base)", false, 13)) A.Assign(wing, null, AirMission.None);
            if (Gui.Button(new Rectangle(rx - 98 * S, by, 48 * S, 24 * S), "Base", true, "Move to another air base (click it on the map)", ordering && Ui.WingMission == null, 13)) StartWingOrder(wing, null);
            if (Gui.Button(new Rectangle(rx - 26 * S, box.Y + 26 * S, 22 * S, 20 * S), "x", true, Has(FeatureSet.Production) ? "Disband (the planes go back to the stockpile)" : "Disband this wing", false, 12))
            {
                if (Ui.OrderingWing == wing) Ui.OrderingWing = null;
                A.Disband(wing);
            }
            wy += rowH + 5 * S;
        }
        Gui.EndScroll();
        if (Ui.OrderingWing != null)
            Gui.Wrapped(Ui.WingMission != null ? "Click an air region on the map (green/red = air superiority). Right-click or Esc cancels." : "Click a state with one of your air bases. Right-click or Esc cancels.", x, r.Y + r.Height - 46 * S, w, 13, Gui.Gold);
    }

    private void DrawWingBuilding(Country c, Rectangle r, float x, float y, float w)
    {
        var A = G.Air;
        var types = G.Data.PlaneTypes.Values.ToList();
        bool production = Has(FeatureSet.Production);
        if (production)
            y += Gui.Wrapped("Military factories build planes (add a production line). A new wing takes up to " + $"{A.WingSize:0} planes from the stockpile.", x, y, w, 14) + 6 * S;
        float rowH = 56 * S;
        var orders = production ? new List<WingOrder>() : A.OrdersOf(c);
        float listH = Math.Min(types.Count * (rowH + 4 * S), (r.Y + r.Height - y) * 0.5f);
        float top = Gui.BeginScroll("planetypes", new Rectangle(x, y, w, listH), types.Count * (rowH + 4 * S));
        float py = top;
        foreach (var t in types)
        {
            var box = new Rectangle(x, py, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(t.Name, box.X + 8 * S, box.Y + 5 * S, 15, Gui.Gold, FontKind.Bold);
            string stats = $"range {t.Range:0} km" +
                           (t.Stat("air_attack") > 0 ? $"  air {t.Stat("air_attack"):0.#}" : "") +
                           (t.Stat("ground_attack") > 0 ? $"  ground {t.Stat("ground_attack"):0.#}" : "") +
                           (t.Stat("strategic_attack") > 0 ? $"  strategic {t.Stat("strategic_attack"):0.#}" : "") +
                           (t.Stat("air_defense") > 0 ? $"  defense {t.Stat("air_defense"):0.#}" : "");
            Gui.Label(Gui.Fit(stats, box.Width - 130 * S, 13), box.X + 8 * S, box.Y + 28 * S, 13, Gui.TextDim);
            var btn = new Rectangle(box.X + box.Width - 112 * S, box.Y + 6 * S, 104 * S, 22 * S);
            var reason = A.CannotBuildReason(c, t);
            if (production)
            {
                Gui.Label($"{Math.Floor(c.Stockpile.GetValueOrDefault(t.Key)):0} in stock", btn.X, box.Y + 32 * S, 13, Gui.TextDim);
                if (Gui.Button(btn, "New wing", reason == null, reason ?? $"Make a wing of {t.Name} from the stockpile", false, 13))
                    Ui.SetStatus(A.BuildWing(c, t) ?? $"New {t.Name} wing");
                if (G.Data.Equipment.TryGetValue(t.Key, out var e) && !c.ProductionLines.Any(l => l.Equipment == e))
                    if (Gui.Button(new Rectangle(btn.X + 60 * S, box.Y + 30 * S, 44 * S, 20 * S), "Make", true, $"Start a production line for {t.Name}\nProduction cost {t.ProductionCost:0.#} per plane", false, 12))
                        G.Production.AddLine(c, e, Math.Min(1, G.Production.FreeFactories(c, false)));
            }
            else
            {
                string cost = (Has(FeatureSet.Economy) ? $"{t.Cost:0} gold, " : "") + $"{t.Manpower:0} manpower, {t.BuildDays} days";
                if (Gui.Button(btn, "Build wing", reason == null, reason ?? $"Build a {t.Name} wing\n{cost}", false, 13))
                    Ui.SetStatus(A.BuildWing(c, t) ?? $"{t.Name} wing ordered");
            }
            py += rowH + 4 * S;
        }
        Gui.EndScroll();
        y += listH + 8 * S;
        if (orders.Count > 0)
        {
            Gui.Section($"Being built ({orders.Count})", x, y, w);
            y += 28 * S;
            foreach (var o in orders.Take(3))
            {
                Gui.Label(Gui.Fit(o.Type.Name, 150 * S, 14), x, y, 14);
                Gui.Bar(new Rectangle(x + 160 * S, y + 4 * S, w - 240 * S, 9 * S), 1f - o.HoursLeft / (float)Math.Max(1, o.TotalHours), Gui.Gold);
                Gui.Label($"{Math.Max(1, o.HoursLeft / 24)} days", x + w - 70 * S, y, 13, Gui.TextDim);
                y += 26 * S;
            }
        }
        // air bases
        var bases = G.World.States.Where(s => s.Owner == c && s.Buildings.GetValueOrDefault("air_base") > 0).OrderByDescending(s => A.Capacity(s)).ToList();
        Gui.Section($"Air bases ({bases.Count})", x, y, w);
        y += 28 * S;
        float bt = Gui.BeginScroll("airbases", new Rectangle(x, y, w, Math.Max(20 * S, r.Y + r.Height - y - 10 * S)), bases.Count * 24 * S);
        float ay = bt;
        foreach (var s in bases)
        {
            var row = new Rectangle(x, ay, w - 10 * S, 22 * S);
            Gui.Label(Gui.Fit($"{s.DisplayName} (level {s.Buildings["air_base"]})", w - 140 * S, 14), x, ay, 14);
            Gui.Label($"{A.PlanesAt(s):0}/{A.Capacity(s):0} planes", x + w - 130 * S, ay, 13, Gui.TextDim);
            if (Gui.Hover(row) && s.Locations.Count > 0 && Gui.Clicked(row)) Cam.LookAt(Map.LocationPos(s.Locations[0]));
            ay += 24 * S;
        }
        Gui.EndScroll();
        if (bases.Count == 0) Gui.Label(production ? "No air bases - build them in Production > Construction." : "No air bases.", x, y, 14, Gui.TextDim);
    }
}
