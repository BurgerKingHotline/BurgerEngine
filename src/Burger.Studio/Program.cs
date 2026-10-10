namespace Burger.Studio;

/// <summary>
/// Burger Studio - the BurgerEngine editor.
///   BurgerStudio.exe                        project hub
///   BurgerStudio.exe &lt;project&gt;              open a project (name in projects/ or a path)
/// Test options: --screenshot file.png --frames N (save the window after N frames and quit),
///               --select &lt;outliner id&gt;, --open &lt;file&gt;, --browse &lt;folder&gt;, --settings
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var opt = new StudioOptions();
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
                switch (a)
                {
                    case "--screenshot": opt.Screenshot = Path.GetFullPath(Next()); break;
                    case "--frames": opt.ScreenshotFrame = int.Parse(Next()); break;
                    case "--select": opt.Select = Next(); break;
                    case "--open": opt.OpenFile = Next(); break;
                    case "--browse": opt.Browse = Next(); break;
                    case "--settings": opt.ShowSettings = true; break;
                    case "--features": opt.ShowFeatures = true; break;
                    case "--selftest": opt.SelfTest = true; break;
                    case "--windowed": opt.Windowed = true; break;
                    case "--size":
                        var p = Next().Split('x');
                        opt.Width = int.Parse(p[0]);
                        opt.Height = int.Parse(p[1]);
                        break;
                    // relative paths are relative to where Studio was started (it changes its working folder later)
                    default: opt.Project ??= Directory.Exists(a) ? Path.GetFullPath(a) : a; break;
                }
            }
            new Studio(opt).Run();
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "studio_crash.log"), $"Burger Studio crash - {DateTime.Now}\n{ex}");
            }
            catch { /* nothing else we can do */ }
            return 1;
        }
    }
}

public sealed class StudioOptions
{
    public string? Project;
    public bool ShowFeatures;
    public string? Screenshot;
    public int ScreenshotFrame = 90;
    public string? Select;
    public string? OpenFile;
    public string? Browse;
    public bool ShowSettings;
    public bool SelfTest;
    public bool Windowed;
    public int Width = 1600, Height = 900;
}
