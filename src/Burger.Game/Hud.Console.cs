using Burger.Core;
using Raylib_cs;

namespace Burger.Runtime;

public sealed partial class Hud
{
    private string _consoleInput = "";
    private readonly List<(string Text, Color Color)> _consoleLines = new() { ("Developer console - type 'help' for commands. Esc or ` closes.", Gui.Gold) };
    private readonly List<string> _history = new();
    private int _historyPos = -1;
    private bool _consoleSkipChar;

    private void DrawConsole()
    {
        // input
        int ch;
        while ((ch = Raylib.GetCharPressed()) > 0)
        {
            if (_consoleSkipChar) { _consoleSkipChar = false; continue; } // the ` that opened the console
            if (ch == '`') continue;
            _consoleInput += char.ConvertFromUtf32(ch);
        }
        _consoleSkipChar = false;
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && _consoleInput.Length > 0)
            _consoleInput = _consoleInput[..^1];
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Ui.ConsoleOpen = false; return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Up) && _history.Count > 0)
        {
            _historyPos = _historyPos < 0 ? _history.Count - 1 : Math.Max(0, _historyPos - 1);
            _consoleInput = _history[_historyPos];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Down) && _historyPos >= 0)
        {
            _historyPos++;
            if (_historyPos >= _history.Count) { _historyPos = -1; _consoleInput = ""; }
            else _consoleInput = _history[_historyPos];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
        {
            string cmd = _consoleInput.Trim();
            _consoleInput = "";
            _historyPos = -1;
            if (cmd.Length > 0)
            {
                _history.Add(cmd);
                _consoleLines.Add(("> " + cmd, Gui.TextDim));
                int errs = Log.ErrorCount;
                string result = cmd is "save" ? _app.QuickSave() : G.Commands.Execute(cmd);
                foreach (var line in result.Split('\n')) _consoleLines.Add((line, Gui.Text));
                if (Log.ErrorCount > errs)
                    foreach (var e in Log.Entries.Skip(Log.Entries.Count - (Log.ErrorCount - errs)))
                        _consoleLines.Add((e.ToString(), Gui.Bad));
                Map.Dirty = true;
            }
        }

        float h = Raylib.GetScreenHeight() * 0.4f;
        var r = new Rectangle(0, TopH, Raylib.GetScreenWidth(), h);
        Gui.Block(r);
        Raylib.DrawRectangleRec(r, new Color(8, 8, 12, 235));
        Raylib.DrawLine(0, (int)(r.Y + h), (int)r.Width, (int)(r.Y + h), Gui.Gold);
        float lineH = 18 * S;
        int visible = (int)((h - 34 * S) / lineH);
        float y = r.Y + 6;
        foreach (var (text, color) in _consoleLines.Skip(Math.Max(0, _consoleLines.Count - visible)))
        {
            Gui.Label(text, 10, y, 15, color);
            y += lineH;
        }
        bool blink = (int)(Raylib.GetTime() * 2) % 2 == 0;
        Gui.Label("> " + _consoleInput + (blink ? "_" : ""), 10, r.Y + h - 26 * S, 17, Gui.Gold);
        if (_consoleLines.Count > 500) _consoleLines.RemoveRange(0, _consoleLines.Count - 500);
    }

    private void DrawErrorLog()
    {
        float w = Math.Min(1100 * S, Raylib.GetScreenWidth() - 40 * S), h = Raylib.GetScreenHeight() * 0.6f;
        var r = new Rectangle((Raylib.GetScreenWidth() - w) / 2, TopH + 20 * S, w, h);
        Gui.Panel(r, $"Error log - {Log.ErrorCount} errors, {Log.WarningCount} warnings (F2 to close)");
        if (Gui.CloseButton(r)) { Ui.ErrorsOpen = false; return; }
        var entries = Log.Entries.Where(e => e.Level != LogLevel.Info || _showInfo).ToList();
        if (Gui.Button(new Rectangle(r.X + w - 160 * S, r.Y + 3 * S, 128 * S, 22 * S), _showInfo ? "Hide info" : "Show info", true, null, false, 13)) _showInfo = !_showInfo;
        float lineH = 19 * S;
        float top = Gui.BeginScroll("errors", new Rectangle(r.X + 8, r.Y + 34 * S, w - 16, h - 42 * S), entries.Count * lineH);
        float y = top;
        foreach (var e in entries)
        {
            var col = e.Level switch { LogLevel.Error => Gui.Bad, LogLevel.Warning => Gui.Gold, _ => Gui.TextDim };
            string t = e.ToString().Replace('\n', ' ');
            if (t.Length > 180) t = t[..177] + "...";
            if (y > r.Y && y < r.Y + h) Gui.Label(t, r.X + 12, y, 14, col);
            y += lineH;
        }
        Gui.EndScroll();
        if (entries.Count == 0) Gui.Label("No problems found.", r.X + 16, r.Y + 40 * S, 16, Gui.Good);
    }

    private bool _showInfo;
}
