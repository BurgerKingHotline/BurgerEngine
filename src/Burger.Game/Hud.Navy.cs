using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Modding;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>The navy menu: fleets, transports and ship building (navy feature).</summary>
public sealed partial class Hud
{
    public int NavyTab;

    private void DrawNavyPanel()
    {
        var player = G.World.Player;
        if (player == null || !Has(FeatureSet.Navy)) { CloseLeft(); return; }
        var r = LeftRect(2000);
        r.Width = 440 * S;
        Gui.Panel(r, "Navy");
        if (Gui.CloseButton(r)) { CloseLeft(); return; }
        float x = r.X + 12 * S, y = r.Y + Gui.HeaderHeight + 10 * S, w = r.Width - 24 * S;
        var N = G.Navy;
        var fleets = N.FleetsOf(player).ToList();
        int ships = fleets.Sum(f => f.Ships.Count);
        double capacity = N.TransportCapacity(player);
        Gui.Label($"Ships: {ships}   Fleets: {fleets.Count}", x, y, 15);
        string transport = $"Transports: {N.DivisionsAtSea(player)}/{capacity:0} divisions";
        var tm = Gui.Measure(transport, 15);
        Gui.Label(transport, x + w - tm.X, y, 15, capacity > 0 ? Gui.Text : Gui.TextDim);
        if (Gui.Hover(new Rectangle(x + w - tm.X, y, tm.X, 20 * S)))
            Gui.Tooltip(capacity > 0
                ? "Your transport ships can carry this many divisions over the sea at once.\nOrder divisions to a location across the water and they embark on their own.\nArmies at sea without an escort are sunk by enemy fleets; landing on an enemy coast is a naval invasion (combat penalty)."
                : "Without transport ships your divisions cannot cross the sea.");
        y += 28 * S;
        int tab = NavyTab;
        Tabs(x, y, w, new[] { "Fleets", "Build ships" }, ref tab);
        NavyTab = tab;
        y += 36 * S;
        if (tab == 0) DrawFleetList(player, fleets, r, x, y, w);
        else DrawShipBuilding(player, r, x, y, w);
    }

    private void DrawFleetList(Country c, List<Fleet> fleets, Rectangle r, float x, float y, float w)
    {
        var N = G.Navy;
        if (fleets.Count == 0)
        {
            Gui.Wrapped("You have no fleets. Build ships in the 'Build ships' tab; they appear in your main port.", x, y, w, 14, Gui.TextDim);
            return;
        }
        float rowH = 92 * S;
        float top = Gui.BeginScroll("fleets", new Rectangle(x, y, w, r.Y + r.Height - y - 50 * S), fleets.Count * (rowH + 5 * S));
        float fy = top;
        foreach (var f in fleets)
        {
            var box = new Rectangle(x, fy, w - 8 * S, rowH);
            bool sel = Ui.SelectedFleet == f;
            Card(box, sel ? new Color(40, 52, 66, 255) : new Color(36, 38, 44, 255), sel);
            Gui.Label(Gui.Fit(f.Name, box.Width - 120 * S, 16, FontKind.Bold), box.X + 8 * S, box.Y + 5 * S, 16, Gui.Gold, FontKind.Bold);
            string status = f.Battle != null ? "In battle" : f.Retreating ? "Retreating" : f.Path.Count > 0 ? $"Sailing to {f.Path[^1].DisplayName}" : f.InPort ? $"In port: {f.Location.DisplayName}" : $"At sea: {f.Location.DisplayName}";
            Gui.Label(Gui.Fit(status, box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 27 * S, 13, f.Battle != null ? Gui.Bad : Gui.TextDim);
            string comp = string.Join(", ", f.Ships.GroupBy(s => s.Type).Select(g => $"{g.Count()} {g.Key.Name}"));
            Gui.Label(Gui.Fit(comp, box.Width - 16 * S, 13), box.X + 8 * S, box.Y + 45 * S, 13);
            var hp = new Rectangle(box.X + 8 * S, box.Y + 68 * S, 130 * S, 9 * S);
            Gui.Bar(hp, (float)f.HpFraction, new Color(120, 200, 250, 255));
            if (Gui.Hover(hp)) Gui.Tooltip($"Condition {f.HpFraction:P0}. Damaged ships repair in a friendly port (faster with naval bases).");
            // buttons
            float bx = box.X + box.Width - 112 * S;
            if (Gui.Button(new Rectangle(bx, box.Y + 5 * S, 104 * S, 22 * S), sel ? "Selected" : "Select", true, "Select this fleet, then right-click a sea location or a port to sail there", sel, 13))
            {
                Ui.SelectedFleet = f;
                Ui.SelectedDivisions.Clear();
                Ui.SelectedArmy = null;
                Cam.LookAt(Map.LocationPos(f.Location));
            }
            var others = fleets.Where(o => o != f && o.Location == f.Location && o.Battle == null).ToList();
            float by = box.Y + rowH - 27 * S;
            if (Gui.Button(new Rectangle(bx, by, 50 * S, 22 * S), "Port", f.Battle == null && !(f.InPort && f.Path.Count == 0), "Return to the home port", false, 13))
            {
                N.RetreatHome(f);
                f.Retreating = false;
            }
            if (Gui.Button(new Rectangle(bx + 54 * S, by, 50 * S, 22 * S), "Merge", others.Count > 0 && f.Battle == null, others.Count > 0 ? $"Join the {others.Count} other fleet(s) here into this one" : "No other fleet of yours is here", false, 13))
            {
                foreach (var o in others) N.Merge(f, o);
                if (Ui.SelectedFleet != null && !N.Fleets.Contains(Ui.SelectedFleet)) Ui.SelectedFleet = f;
            }
            fy += rowH + 5 * S;
        }
        Gui.EndScroll();
        if (Ui.SelectedFleet != null)
            Gui.Wrapped("Right-click a sea location or one of your ports to move the selected fleet. Esc deselects.", x, r.Y + r.Height - 44 * S, w, 13, Gui.Gold);
    }

    private void DrawShipBuilding(Country c, Rectangle r, float x, float y, float w)
    {
        var N = G.Navy;
        var types = G.Data.ShipTypes.Values.ToList();
        var port = N.MainPort(c);
        Gui.Label(port != null ? $"New ships appear in {port.DisplayName}" : "You have no port - you cannot build ships", x, y, 14, port != null ? Gui.TextDim : Gui.Bad);
        y += 24 * S;
        if (types.Count == 0) { Gui.Label("No ship types (common/ships).", x, y, 14, Gui.TextDim); return; }
        bool production = Has(FeatureSet.Production);
        if (production)
        {
            int docks = G.Production.Dockyards(c);
            y += Gui.Wrapped($"Ships are built by dockyards ({docks}). Each finished ship is launched at your main port. Start a production line here or in the Production menu.", x, y, w, 14) + 8 * S;
        }
        float rowH = 58 * S;
        float listH = r.Y + r.Height - y - 10 * S;
        var orders = production ? new List<ShipOrder>() : N.OrdersOf(c);
        if (orders.Count > 0) listH -= Math.Min(4, orders.Count) * 34 * S + 34 * S;
        float top = Gui.BeginScroll("shiptypes", new Rectangle(x, y, w, listH), types.Count * (rowH + 4 * S));
        float sy = top;
        foreach (var t in types)
        {
            var box = new Rectangle(x, sy, w - 8 * S, rowH);
            Card(box, new Color(36, 38, 44, 255));
            Gui.Label(t.Name, box.X + 8 * S, box.Y + 5 * S, 15, Gui.Gold, FontKind.Bold);
            string stats = $"{t.Category}  hp {t.Hp:0}  speed {t.Speed:0}" +
                           (t.Stat("light_attack") > 0 ? $"  light {t.Stat("light_attack"):0.#}" : "") +
                           (t.Stat("heavy_attack") > 0 ? $"  heavy {t.Stat("heavy_attack"):0.#}" : "") +
                           (t.Stat("armor") > 0 ? $"  armor {t.Stat("armor"):0.#}" : "") +
                           (t.Stat("transport") > 0 ? $"  carries {t.Stat("transport"):0} div." : "");
            Gui.Label(Gui.Fit(stats, box.Width - 120 * S, 13), box.X + 8 * S, box.Y + 28 * S, 13, Gui.TextDim);
            var btn = new Rectangle(box.X + box.Width - 108 * S, box.Y + 15 * S, 100 * S, 28 * S);
            if (production)
            {
                G.Data.Equipment.TryGetValue(t.Key, out var e);
                var line = e == null ? null : c.ProductionLines.FirstOrDefault(l => l.Equipment == e);
                if (line != null)
                    Gui.Label($"{line.Factories} dock. | {G.Production.Stock(c, t.Key):0.##}", btn.X, btn.Y + 5 * S, 13, Gui.Text);
                else if (Gui.Button(btn, "Produce", e != null && port != null, $"Start a dockyard production line for {t.Name}\nProduction cost {t.ProductionCost:0}", false, 13))
                    G.Production.AddLine(c, e!, Math.Min(1, G.Production.FreeFactories(c, true)));
            }
            else
            {
                var reason = N.CannotBuyReason(c, t);
                string cost = (Has(FeatureSet.Economy) ? $"{t.Cost:0} gold, " : "") + $"{t.Manpower:0} manpower, {t.BuildDays} days";
                if (Gui.Button(btn, "Build", reason == null, reason ?? $"Build a {t.Name}\n{cost}", false, 13))
                {
                    var err = N.BuyShip(c, t);
                    Ui.SetStatus(err ?? $"{t.Name} ordered");
                }
            }
            sy += rowH + 4 * S;
        }
        Gui.EndScroll();
        if (orders.Count == 0) return;
        float oy = y + listH + 6 * S;
        Gui.Section($"Being built ({orders.Count})", x, oy, w);
        oy += 28 * S;
        foreach (var o in orders.Take(4))
        {
            Gui.Label(Gui.Fit(o.Type.Name, 140 * S, 14), x, oy, 14);
            Gui.Bar(new Rectangle(x + 150 * S, oy + 4 * S, w - 230 * S, 9 * S), 1f - o.HoursLeft / (float)Math.Max(1, o.TotalHours), Gui.Gold);
            Gui.Label($"{Math.Max(1, o.HoursLeft / 24)} days", x + w - 70 * S, oy, 13, Gui.TextDim);
            oy += 34 * S;
        }
    }
}
