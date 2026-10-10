using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Draws things on top of the map: country names, location names, unit counters, battles, fronts, paths.</summary>
public sealed class MapOverlay
{
    private readonly Game _game;
    private readonly MapRenderer _map;
    private readonly CountryLabels _countryLabels;

    public MapOverlay(Game game, MapRenderer map)
    {
        _game = game;
        _map = map;
        _countryLabels = new CountryLabels(game, map);
    }

    private static Color C(Rgb c, int a = 255) => new(c.R, c.G, c.B, (byte)a);

    private bool OnScreen(Vector2 p, float margin = 50)
        => p.X > -margin && p.Y > -margin && p.X < Viewport.W + margin && p.Y < Viewport.H + margin;

    public void DrawLabels(Camera3D cam)
    {
        float h = cam.Position.Y;
        if (h > 18) _countryLabels.Draw(cam);
        if (h < 30)
        {
            int drawn = 0;
            foreach (var l in _game.World.Locations)
            {
                if (l.IsWater || l.PixelCount < 30) continue;
                var s = Viewport.SnapToScreen(_map.LocationPos(l, 0.1f), cam);
                if (!OnScreen(s, 0)) continue;
                string name = l.DisplayName;
                var m = Gui.Measure(name, 13);
                Gui.LabelShadow(name, s.X - m.X / 2, s.Y + 14, 13, new Color(240, 236, 220, 210));
                if (++drawn > 300) break;
            }
        }
    }

    /// <summary>Draws unit counters and handles clicks on them. Returns true if a counter was clicked.</summary>
    /// <summary>Unit counters drawn this frame (screen rectangle, owner, divisions) - used for box selection.</summary>
    public readonly List<(Rectangle Rect, Country Owner, List<Division> Divisions)> Counters = new();

    public bool DrawUnits(Camera3D cam, UiState ui, bool mouseOverUi)
    {
        Counters.Clear();
        if (cam.Position.Y > _map.StrategicZoom) return false; // strategic view: no counters
        bool clicked = false;
        var w = _game.World;
        var player = w.Player;
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        float scale = Gui.Scale * Math.Clamp(60f / cam.Position.Y, 0.65f, 1.15f);

        // battles first (under the counters)
        Counters.Clear();
        foreach (var b in w.Battles)
        {
            if (!_game.IsVisible(b.Location)) continue; // fog of war
            var s = Viewport.SnapToScreen(_map.LocationPos(b.Location, 0.3f), cam);
            if (!OnScreen(s)) continue;
            float r = 13 * scale;
            Raylib.DrawCircleV(s, r + 3, new Color(0, 0, 0, 160));
            Raylib.DrawCircleV(s, r, new Color(170, 30, 30, 230));
            Raylib.DrawLineEx(new Vector2(s.X - r * 0.6f, s.Y - r * 0.6f), new Vector2(s.X + r * 0.6f, s.Y + r * 0.6f), 3, Color.White);
            Raylib.DrawLineEx(new Vector2(s.X + r * 0.6f, s.Y - r * 0.6f), new Vector2(s.X - r * 0.6f, s.Y + r * 0.6f), 3, Color.White);
            var bar = new Rectangle(s.X - 26 * scale, s.Y + r + 4, 52 * scale, 6 * scale);
            Gui.Bar(bar, (float)b.AttackerProgress, C(b.AttackerCountry.Color), C(b.DefenderCountry.Color));
            var hit = new Rectangle(s.X - r, s.Y - r, r * 2, r * 2);
            if (Gui.Hover(hit) && !mouseOverUi)
                Gui.Tooltip($"Battle of {b.Location.DisplayName}\nAttacker: {b.AttackerCountry.Name} ({b.Attackers.Count} divisions)\n" +
                            $"Defender: {b.DefenderCountry.Name} ({b.Defenders.Count} divisions)\nLosses: {b.AttackerLosses:0} / {b.DefenderLosses:0}\n" +
                            $"Terrain: {b.Location.Terrain.Name}{(Burger.Core.Warfare.CombatMath.RiverCrossing(b) ? ", river crossing" : "")}\nModel: {_game.Combat.Current(_game).Name}");
        }

        // counters: one per (location, owner)
        var groups = w.Divisions.GroupBy(d => (d.Location, d.Owner));
        var perLocation = new Dictionary<Location, int>();
        foreach (var g in groups)
        {
            var (loc, owner) = g.Key;
            if (owner != player && !_game.IsVisible(loc)) continue; // fog of war hides other armies
            int slot = perLocation.TryGetValue(loc, out var k) ? k : 0;
            perLocation[loc] = slot + 1;
            var basePos = _map.LocationPos(loc, 0.35f);
            var s = Viewport.SnapToScreen(basePos, cam);
            if (!OnScreen(s)) continue;
            // moving units are drawn part of the way to their next location
            var first = g.First();
            if (first.Path.Count > 0 && first.Battle == null && g.All(d => d.Path.Count > 0 && d.Path[0] == first.Path[0]))
            {
                var next = Viewport.SnapToScreen(_map.LocationPos(first.Path[0], 0.35f), cam);
                s = Vector2.Lerp(s, next, (float)Math.Clamp(first.MoveProgress, 0, 1) * 0.8f);
            }
            float cw = 50 * scale, ch = 22 * scale;
            var rect = new Rectangle(s.X - cw / 2, s.Y - ch / 2 - slot * (ch + 9 * scale), cw, ch);
            bool selected = g.Any(d => ui.SelectedDivisions.Contains(d));
            bool mine = owner == player;
            Counters.Add((rect, owner, g.ToList()));
            Raylib.DrawRectangleRec(new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4 + 7 * scale), selected ? new Color(255, 230, 120, 255) : new Color(0, 0, 0, 200));
            Raylib.DrawRectangleRec(rect, C(owner.Color));
            int count = g.Count();
            string text = count.ToString();
            var m = Gui.Measure(text, 16 * scale / Gui.Scale);
            Raylib.DrawRectangleRec(new Rectangle(rect.X, rect.Y, rect.Width * 0.45f, rect.Height), new Color(0, 0, 0, 110));
            Gui.Label(text, rect.X + rect.Width * 0.225f - m.X / 2, rect.Y + (rect.Height - m.Y) / 2, 16 * scale / Gui.Scale, Color.White);
            // division symbol
            var sym = new Rectangle(rect.X + rect.Width * 0.55f, rect.Y + rect.Height * 0.22f, rect.Width * 0.35f, rect.Height * 0.56f);
            Raylib.DrawRectangleLinesEx(sym, 1.5f, Color.White);
            Raylib.DrawLineEx(new Vector2(sym.X, sym.Y), new Vector2(sym.X + sym.Width, sym.Y + sym.Height), 1.5f, Color.White);
            Raylib.DrawLineEx(new Vector2(sym.X + sym.Width, sym.Y), new Vector2(sym.X, sym.Y + sym.Height), 1.5f, Color.White);
            float str = (float)g.Average(d => d.StrengthFraction), org = (float)g.Average(d => d.OrgFraction);
            Gui.Bar(new Rectangle(rect.X, rect.Y + rect.Height + 1, rect.Width, 3 * scale), str, new Color(90, 200, 90, 255));
            Gui.Bar(new Rectangle(rect.X, rect.Y + rect.Height + 1 + 3.5f * scale, rect.Width, 3 * scale), org, new Color(80, 160, 240, 255));
            // out of supply: a small warning triangle
            double supply = g.Average(d => d.Supply);
            if (_game.Features.HasSupply && supply < 0.75 && (mine || _game.IsVisible(loc)))
            {
                var tp = new Vector2(rect.X + rect.Width + 6 * scale, rect.Y + 2 * scale);
                var tcol = supply < 0.4 ? new Color(230, 70, 50, 255) : new Color(240, 190, 60, 255);
                Raylib.DrawTriangle(tp + new Vector2(0, -6 * scale), tp + new Vector2(-6 * scale, 5 * scale), tp + new Vector2(6 * scale, 5 * scale), tcol);
            }

            if (!mouseOverUi && Gui.Hover(rect))
            {
                string army = string.Join(", ", g.Where(d => d.Army != null).Select(d => d.Army!.Name).Distinct());
                Gui.Tooltip($"{owner.Name}: {count} division(s) in {loc.DisplayName}\n" + (army.Length > 0 ? $"Army: {army}\n" : "") +
                            string.Join("\n", g.Take(8).Select(d => $"{d.Name}  str {d.StrengthFraction:P0}  org {d.OrgFraction:P0}" +
                                (_game.Features.HasSupply ? $"  supply {d.Supply:P0}" : "") + (d.Retreating ? " (retreating)" : ""))) +
                            (count > 8 ? $"\n... and {count - 8} more" : ""));
                if (mine && Gui.Clicked(rect))
                {
                    if (!shift) ui.SelectedDivisions.Clear();
                    foreach (var d in g) if (!ui.SelectedDivisions.Contains(d)) ui.SelectedDivisions.Add(d);
                    ui.SelectedArmy = null;
                    ui.SelectedFleet = null;
                    clicked = true;
                }
            }
        }
        return clicked;
    }

    /// <summary>Fleets (navy feature): a ship counter per fleet, naval battles and the selected fleet's route. Returns true if a fleet was clicked.</summary>
    public bool DrawFleets(Camera3D cam, UiState ui, bool mouseOverUi)
    {
        if (cam.Position.Y > _map.StrategicZoom) return false;
        if (!_game.Features.Has(Burger.Core.Modding.FeatureSet.Navy)) return false;
        var player = _game.World.Player;
        float scale = Gui.Scale * Math.Clamp(60f / cam.Position.Y, 0.65f, 1.15f);
        bool clicked = false;
        if (ui.SelectedFleet != null && !_game.Navy.Fleets.Contains(ui.SelectedFleet)) ui.SelectedFleet = null;
        // route of the selected fleet
        if (ui.SelectedFleet is { Path.Count: > 0 } sf)
        {
            var from = Viewport.SnapToScreen(_map.LocationPos(sf.Location, 0.3f), cam);
            foreach (var l in sf.Path)
            {
                var to = Viewport.SnapToScreen(_map.LocationPos(l, 0.3f), cam);
                Raylib.DrawLineEx(from, to, 3, new Color(120, 200, 250, 230));
                from = to;
            }
            Raylib.DrawCircleV(from, 5, new Color(120, 200, 250, 240));
        }
        var perLocation = new Dictionary<Location, int>();
        foreach (var f in _game.Navy.Fleets)
        {
            if (f.Ships.Count == 0) continue;
            if (f.Owner != player && !_game.IsVisible(f.Location)) continue;
            int slot = perLocation.TryGetValue(f.Location, out var k) ? k : 0;
            perLocation[f.Location] = slot + 1;
            var s = Viewport.SnapToScreen(_map.LocationPos(f.Location, 0.35f), cam);
            if (f.Path.Count > 0)
                s = Vector2.Lerp(s, Viewport.SnapToScreen(_map.LocationPos(f.Path[0], 0.35f), cam), (float)Math.Clamp(f.MoveProgress, 0, 1) * 0.8f);
            if (!OnScreen(s)) continue;
            float cw = 46 * scale, ch = 20 * scale;
            var rect = new Rectangle(s.X - cw / 2, s.Y + 16 * scale + slot * (ch + 8 * scale), cw, ch);
            bool sel = ui.SelectedFleet == f;
            Raylib.DrawRectangleRec(new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 7 * scale), sel ? new Color(120, 210, 255, 255) : new Color(0, 0, 0, 200));
            Raylib.DrawRectangleRec(rect, C(f.Owner.Color));
            // a ship: hull and mast
            var hull = new Vector2(rect.X + rect.Width * 0.62f, rect.Y + rect.Height * 0.62f);
            float hw = rect.Width * 0.3f;
            Raylib.DrawTriangle(hull + new Vector2(-hw, 0), hull + new Vector2(-hw * 0.7f, rect.Height * 0.22f), hull + new Vector2(hw, 0), Color.White);
            Raylib.DrawTriangle(hull + new Vector2(-hw * 0.7f, rect.Height * 0.22f), hull + new Vector2(hw * 0.75f, rect.Height * 0.22f), hull + new Vector2(hw, 0), Color.White);
            Raylib.DrawLineEx(hull + new Vector2(0, 0), hull + new Vector2(0, -rect.Height * 0.45f), 1.5f, Color.White);
            string text = f.Ships.Count.ToString();
            var m = Gui.Measure(text, 15 * scale / Gui.Scale);
            Gui.Label(text, rect.X + rect.Width * 0.2f - m.X / 2, rect.Y + (rect.Height - m.Y) / 2, 15 * scale / Gui.Scale, Color.White);
            Gui.Bar(new Rectangle(rect.X, rect.Y + rect.Height + 1, rect.Width, 3 * scale), (float)f.HpFraction, new Color(120, 200, 250, 255));
            if (f.Battle != null)
            {
                var c = new Vector2(rect.X + rect.Width + 8 * scale, rect.Y + rect.Height / 2);
                Raylib.DrawCircleV(c, 7 * scale, new Color(170, 30, 30, 240));
                Raylib.DrawLineEx(c + new Vector2(-4, -4) * scale, c + new Vector2(4, 4) * scale, 2, Color.White);
                Raylib.DrawLineEx(c + new Vector2(4, -4) * scale, c + new Vector2(-4, 4) * scale, 2, Color.White);
            }
            if (!mouseOverUi && Gui.Hover(rect))
            {
                string status = f.Battle != null ? "In battle" : f.Retreating ? "Retreating" : f.Path.Count > 0 ? $"Moving to {f.Path[^1].DisplayName}" : f.InPort ? "In port" : "At sea";
                Gui.Tooltip($"{f.Name} ({f.Owner.Name})\n{status} - {f.Location.DisplayName}\nShips: " +
                            string.Join(", ", f.Ships.GroupBy(x => x.Type).Select(g => $"{g.Count()} {g.Key.Name}")) + $"\nCondition: {f.HpFraction:P0}" +
                            (f.Owner == player ? "\nClick to select, then right-click a sea location or a port" : ""));
                if (f.Owner == player && Gui.Clicked(rect))
                {
                    ui.SelectedFleet = f;
                    ui.SelectedDivisions.Clear();
                    ui.SelectedArmy = null;
                    clicked = true;
                }
            }
        }
        return clicked;
    }

    /// <summary>Air bases of the player that hold wings (air feature): a plane badge with the number of planes. Returns true if one was clicked.</summary>
    public bool DrawAirBases(Camera3D cam, bool mouseOverUi)
    {
        if (cam.Position.Y > _map.StrategicZoom) return false;
        if (!_game.Features.Has(Burger.Core.Modding.FeatureSet.Air) || _game.World.Player is not { } player) return false;
        float scale = Gui.Scale * Math.Clamp(60f / cam.Position.Y, 0.65f, 1.15f);
        bool clicked = false;
        foreach (var g in _game.Air.WingsOf(player).GroupBy(w => w.Base))
        {
            var st = g.Key;
            if (st.Locations.Count == 0) continue;
            var loc = st.Locations.OrderByDescending(l => l.PixelCount).First();
            var s = Viewport.SnapToScreen(_map.LocationPos(loc, 0.35f), cam);
            if (!OnScreen(s)) continue;
            float cw = 46 * scale, ch = 18 * scale;
            var rect = new Rectangle(s.X - cw / 2, s.Y - 34 * scale, cw, ch);
            Raylib.DrawRectangleRec(new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4), new Color(0, 0, 0, 200));
            Raylib.DrawRectangleRec(rect, new Color(70, 90, 120, 240));
            // a small plane
            var c = new Vector2(rect.X + rect.Width * 0.24f, rect.Y + rect.Height / 2);
            float h = rect.Height * 0.38f;
            Raylib.DrawLineEx(c + new Vector2(0, -h), c + new Vector2(0, h), 2, Color.White);
            Raylib.DrawTriangle(c + new Vector2(0, -h * 0.3f), c + new Vector2(-h, h * 0.25f), c + new Vector2(h, h * 0.25f), Color.White);
            string text = $"{g.Sum(w => w.Planes):0}";
            var m = Gui.Measure(text, 13 * scale / Gui.Scale);
            Gui.Label(text, rect.X + rect.Width * 0.62f - m.X / 2, rect.Y + (rect.Height - m.Y) / 2, 13 * scale / Gui.Scale, Color.White);
            if (!mouseOverUi && Gui.Hover(rect))
            {
                Gui.Tooltip($"Air base: {st.DisplayName} ({_game.Air.PlanesAt(st):0}/{_game.Air.Capacity(st):0} planes)\n" +
                            string.Join("\n", g.Select(w => $"{w.Name}: {w.Planes:0} planes, " + (w.Region == null ? "idle" : $"{Burger.Core.Air.AirSystem.MissionName(w.Mission)} over {w.Region.DisplayName}"))) +
                            "\nClick to open the air menu");
                if (Gui.Clicked(rect)) clicked = true;
            }
        }
        return clicked;
    }

    /// <summary>Front lines of the player's armies and paths of selected divisions.</summary>
    public void DrawOrders(Camera3D cam, UiState ui)
    {
        var player = _game.World.Player;
        if (player == null) return;
        float ppu = _game.World.Map.Settings.PixelsPerUnit;
        foreach (var army in _game.World.Armies)
        {
            if (army.Owner != player || army.FrontEnemy == null) continue;
            bool sel = army == ui.SelectedArmy;
            var col = army.Mode == ArmyMode.Offensive ? new Color(225, 60, 50, sel ? 255 : 200) : new Color(90, 200, 90, sel ? 255 : 190);
            float thick = sel ? 5 : 3.5f;
            foreach (var (own, enemy) in army.FrontPairs)
            {
                var adj = own.AdjacencyTo(enemy);
                if (adj == null || adj.BorderPoints.Count == 0) continue;
                Vector2? prev = null;
                foreach (var (x, y) in adj.BorderPoints)
                {
                    var s = Viewport.SnapToScreen(_map.WorldPos(x, y, 0.15f), cam);
                    if (prev != null && Vector2.Distance(prev.Value, s) < 80) Raylib.DrawLineEx(prev.Value, s, thick, col);
                    prev = s;
                }
                if (adj.BorderPoints.Count == 1) Raylib.DrawCircleV(prev!.Value, thick, col);
            }
        }
        // battle plans: offensive arrows, fallback lines and the drawn front locations
        foreach (var army in _game.World.Armies)
        {
            if (army.Owner != player) continue;
            bool sel = army == ui.SelectedArmy || army == ui.PlanArmy;
            if (army.Objective != null && (army.FrontPairs.Count > 0 || army.Divisions.Count > 0))
            {
                var pts = army.FrontPairs.Count > 0 ? army.FrontPairs.Select(p => p.Own).Distinct().ToList() : army.Divisions.Select(d => d.Location).ToList();
                var start3 = new Vector2(pts.Average(l => l.CenterX), pts.Average(l => l.CenterY));
                var a = Viewport.SnapToScreen(_map.WorldPos(start3.X, start3.Y, 0.4f), cam);
                var b = Viewport.SnapToScreen(_map.LocationPos(army.Objective, 0.4f), cam);
                if (OnScreen(a, 2000) && OnScreen(b, 2000) && Vector2.Distance(a, b) > 8)
                {
                    var col = army.Mode == ArmyMode.Offensive ? new Color(225, 55, 45, sel ? 245 : 190) : new Color(225, 160, 60, sel ? 230 : 160);
                    var dir = Vector2.Normalize(b - a);
                    var perp = new Vector2(-dir.Y, dir.X);
                    float head = Math.Min(28, Vector2.Distance(a, b) * 0.4f), width = sel ? 9 : 6;
                    var neck = b - dir * head;
                    // curved shaft (slight bend like HOI4 arrows)
                    var mid = (a + neck) / 2 + perp * Vector2.Distance(a, neck) * 0.12f;
                    Vector2 prev = a;
                    for (int i = 1; i <= 16; i++)
                    {
                        float t = i / 16f;
                        var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * neck;
                        Raylib.DrawLineEx(prev, p, width + 3, new Color(0, 0, 0, 120));
                        Raylib.DrawLineEx(prev, p, width, col);
                        prev = p;
                    }
                    var last = Vector2.Distance(neck, mid) < 0.5f ? dir : Vector2.Normalize(neck - mid);
                    var lp = new Vector2(-last.Y, last.X);
                    var tip = neck + last * head;
                    Raylib.DrawTriangle(tip, neck + lp * head * 0.6f, neck - lp * head * 0.6f, col);
                    Raylib.DrawTriangle(tip, neck - lp * head * 0.6f, neck + lp * head * 0.6f, col);
                }
            }
            if (army.Fallback.Count > 0)
            {
                var col = army.Mode == ArmyMode.Fallback ? new Color(250, 210, 70, sel ? 250 : 200) : new Color(250, 210, 70, sel ? 200 : 110);
                // order the line along its main direction so it reads as a line
                var line = army.Fallback.ToList();
                float dx = line.Max(l => l.CenterX) - line.Min(l => l.CenterX), dy = line.Max(l => l.CenterY) - line.Min(l => l.CenterY);
                line = dx >= dy ? line.OrderBy(l => l.CenterX).ToList() : line.OrderBy(l => l.CenterY).ToList();
                Vector2? prev = null;
                foreach (var l in line)
                {
                    var p = Viewport.SnapToScreen(_map.LocationPos(l, 0.3f), cam);
                    if (prev != null)
                    {
                        // dashed segment
                        float len = Vector2.Distance(prev.Value, p);
                        var d = (p - prev.Value) / Math.Max(1, len);
                        for (float s = 0; s < len; s += 14)
                            Raylib.DrawLineEx(prev.Value + d * s, prev.Value + d * Math.Min(len, s + 8), sel ? 4 : 3, col);
                    }
                    Raylib.DrawCircleV(p, sel ? 5 : 4, col);
                    prev = p;
                }
            }
            if (sel && army.FrontLimit.Count > 0)
                foreach (var l in army.FrontLimit)
                {
                    var p = Viewport.SnapToScreen(_map.LocationPos(l, 0.3f), cam);
                    Raylib.DrawCircleLines((int)p.X, (int)p.Y, 7, new Color(120, 230, 120, 230));
                    Raylib.DrawCircleLines((int)p.X, (int)p.Y, 8, new Color(0, 0, 0, 160));
                }
        }
        // movement arrows for selected divisions
        foreach (var d in ui.SelectedDivisions)
        {
            if (d.Path.Count == 0) continue;
            var from = Viewport.SnapToScreen(_map.LocationPos(d.Location, 0.3f), cam);
            foreach (var l in d.Path)
            {
                var to = Viewport.SnapToScreen(_map.LocationPos(l, 0.3f), cam);
                Raylib.DrawLineEx(from, to, 3, d.AttackTarget == l ? new Color(230, 60, 60, 230) : new Color(250, 220, 90, 220));
                from = to;
            }
            Raylib.DrawCircleV(from, 5, new Color(250, 220, 90, 230));
        }
        _ = ppu;
    }
}
