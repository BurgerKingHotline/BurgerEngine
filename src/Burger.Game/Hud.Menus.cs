using Burger.Core.Save;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawPauseMenu()
    {
        G.Clock.Paused = true;
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 120));
        if (Ui.SettingsOpen) { DrawSettingsPanel(); return; }
        float w = 300 * S, h = 380 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Menu");
        float x = r.X + 20 * S, y = r.Y + 44 * S, bw = w - 40 * S, bh = 38 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Resume")) Ui.PauseMenuOpen = false;
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Save / Load")) { Ui.SaveMenuOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Error log (F2)")) { Ui.ErrorsOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Settings")) Ui.SettingsOpen = true;
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Main menu")) { Ui.PauseMenuOpen = false; _app.ReturnToMainMenu(); return; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Quit")) _app.RequestQuit();
    }

    private string _speed5Text = "";

    /// <summary>Settings: the most days per second speed 5 may run (0 = as fast as the computer allows).</summary>
    private void DrawSettingsPanel()
    {
        float w = 420 * S, h = 230 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Settings");
        if (Gui.CloseButton(r)) { Ui.SettingsOpen = false; UserSettings.Save(); return; }
        float x = r.X + 20 * S, y = r.Y + 46 * S, bw = w - 40 * S;
        if (_speed5Text.Length == 0) _speed5Text = UserSettings.Speed5DaysPerSecond.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Gui.Label("Speed 5: most days per second", x, y, 16);
        y += 26 * S;
        Gui.TextField("speed5", new Rectangle(x, y, 120 * S, 30 * S), ref _speed5Text, 8, "0");
        Gui.Label("0 = as fast as the computer allows", x + 134 * S, y + 6 * S, 14, Gui.TextDim);
        if (double.TryParse(_speed5Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 && Math.Abs(v - UserSettings.Speed5DaysPerSecond) > 1e-9)
        {
            UserSettings.Speed5DaysPerSecond = v;
            UserSettings.Apply(G);
        }
        y += 44 * S;
        Gui.Label($"Currently {G.Clock.ActualDaysPerSecond:0.0} days per second.", x, y, 14, Gui.TextDim);
        y += 30 * S;
        if (Gui.Button(new Rectangle(x, y, bw, 34 * S), "Done")) { Ui.SettingsOpen = false; UserSettings.Save(); }
    }

    private void DrawSaveMenu()
    {
        float w = 520 * S, h = Math.Min(560 * S, Raylib.GetScreenHeight() - 120 * S);
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Save / Load");
        if (Gui.CloseButton(r)) { Ui.SaveMenuOpen = false; return; }
        float x = r.X + 14 * S, y = r.Y + 40 * S, bw = w - 28 * S;
        if (Gui.Button(new Rectangle(x, y, bw, 34 * S), "Save new game", true, "Saves to the saves folder next to the game folder (F5 = quick save)"))
            Ui.SetStatus(_app.SaveAs(null));
        y += 44 * S;
        Gui.Label("Load a saved game:", x, y, 16, Gui.TextDim);
        y += 24 * S;
        var saves = _app.ListSaves();
        float top = Gui.BeginScroll("saves", new Rectangle(x, y, bw, r.Y + h - y - 10 * S), saves.Count * 40 * S);
        float sy = top;
        foreach (var path in saves)
        {
            var (_, date, player) = SaveGame.Peek(path);
            string label = $"{Path.GetFileNameWithoutExtension(path)}   ({player}, {date})";
            if (Gui.Button(new Rectangle(x, sy, bw - 12 * S, 34 * S), label, true, path, false, 15))
            {
                Ui.SaveMenuOpen = false;
                _app.LoadSave(path);
                Gui.EndScroll();
                return;
            }
            sy += 40 * S;
        }
        Gui.EndScroll();
        if (saves.Count == 0) Gui.Label("No saves yet.", x, y, 15, Gui.TextDim);
    }
}
