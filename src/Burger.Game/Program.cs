using System.Text;

namespace Burger.Runtime;

public sealed class AppOptions
{
    public string GameRoot = "";
    public string? Tag;
    public bool Observe;
    public string? LoadSave;
    public int Width = 1600;
    public int Height = 900;
    public bool Hidden;
    /// <summary>Take a screenshot after this many frames and quit (for automated tests).</summary>
    public string? Screenshot;
    public int ScreenshotFrame = 120;
    public int TicksBeforeScreenshot;
    /// <summary>Start unpaused at this speed (1-5).</summary>
    public int Speed;
    /// <summary>Write frame and simulation timings to the log every few seconds.</summary>
    public bool Perf;
    /// <summary>Console commands to run after the game starts (testing / automation).</summary>
    public List<string> Exec = new();
    public string? MapMode;
    public float? Zoom;
    public bool NoFogShading;
    public bool NoUnits;
    /// <summary>Historical bias 0-1 for this run (default: the player's setting).</summary>
    public double? Bias;
    public string? Open;
}

public static class Program
{
    private const string Usage = @"Usage: Game.exe [--game <folder>] [--tag TAG | --observe] [--load <save>] [--size 1600x900]
  --game       game folder (default: 'game' next to the exe)
  --tag        skip the menu and play as TAG
  --observe    skip the menu and watch the AI
  --load       load a save file
  --screenshot <file.png> [--frames N] [--days D]   render, save a screenshot and quit (testing)";

    public static int Main(string[] args)
    {
        var opt = new AppOptions();
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
                switch (a.ToLowerInvariant())
                {
                    case "--game": opt.GameRoot = Next(); break;
                    case "--tag": opt.Tag = Next().ToUpperInvariant(); break;
                    case "--observe": opt.Observe = true; break;
                    case "--load": opt.LoadSave = Next(); break;
                    case "--screenshot": opt.Screenshot = Path.GetFullPath(Next()); break;
                    case "--frames": opt.ScreenshotFrame = int.Parse(Next()); break;
                    case "--days": opt.TicksBeforeScreenshot = int.Parse(Next()) * 24; break;
                    case "--hidden": opt.Hidden = true; break;
                    case "--exec": opt.Exec.Add(Next()); break;
                    case "--mapmode": opt.MapMode = Next(); break;
                    case "--no-fog-shading": opt.NoFogShading = true; break;
                    case "--no-units": opt.NoUnits = true; break;
                    case "--bias": opt.Bias = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--zoom": opt.Zoom = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--open": opt.Open = Next(); break;
                    case "--speed": opt.Speed = int.Parse(Next()); break;
                    case "--perf": opt.Perf = true; break;
                    case "--size":
                        var parts = Next().ToLowerInvariant().Split('x');
                        opt.Width = int.Parse(parts[0]);
                        opt.Height = int.Parse(parts[1]);
                        break;
                    case "--help": case "-h": case "/?":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        // a bare folder argument also works:  Game.exe path\to\game
                        if (Directory.Exists(a)) opt.GameRoot = a;
                        else throw new ArgumentException($"Unknown argument '{a}'");
                        break;
                }
            }
            opt.GameRoot = FindGameRoot(opt.GameRoot);
            new App(opt).Run();
            return 0;
        }
        catch (Exception ex)
        {
            WriteCrash(opt, ex);
            return 1;
        }
    }

    /// <summary>Locates the game folder: argument, then 'game' next to the exe, then 'game' in the working directory.</summary>
    public static string FindGameRoot(string arg)
    {
        if (!string.IsNullOrEmpty(arg)) return Path.GetFullPath(arg);
        var candidates = new List<string>();
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir != null) candidates.Add(Path.Combine(exeDir, "game"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "game"));
        candidates.Add(Directory.GetCurrentDirectory());
        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "game.txt")) || Directory.Exists(Path.Combine(c, "common"))) return Path.GetFullPath(c);
        return Path.GetFullPath(candidates[0]);
    }

    private static void WriteCrash(AppOptions opt, Exception ex)
    {
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "logs");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"BurgerEngine crash - {DateTime.Now}");
            sb.AppendLine($"Game folder: {opt.GameRoot}");
            sb.AppendLine(ex.ToString());
            File.WriteAllText(Path.Combine(dir, "crash.log"), sb.ToString());
        }
        catch { /* nothing else we can do */ }
    }
}
