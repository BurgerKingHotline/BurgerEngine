using Burger.Core.Save;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private void DrawPauseMenu()
    {
        G.Clock.Paused = true;
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 120));
        float w = 300 * S, h = 330 * S;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, (Raylib.GetScreenHeight() - h) / 2, w, h);
        Gui.Panel(r, "Menu");
        float x = r.X + 20 * S, y = r.Y + 44 * S, bw = w - 40 * S, bh = 38 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Resume")) Ui.PauseMenuOpen = false;
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Save / Load")) { Ui.SaveMenuOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Error log (F2)")) { Ui.ErrorsOpen = true; Ui.PauseMenuOpen = false; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Main menu")) { Ui.PauseMenuOpen = false; _app.ReturnToMainMenu(); return; }
        y += bh + 8 * S;
        if (Gui.Button(new Rectangle(x, y, bw, bh), "Quit")) _app.RequestQuit();
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
