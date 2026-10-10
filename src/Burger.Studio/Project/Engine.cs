using System.Diagnostics;
using System.Text;

namespace Burger.Studio;

/// <summary>Finds the engine folder and runs the burger command line tool and the game.</summary>
public static class Engine
{
    public static readonly string Root = FindRoot();
    public static string ProjectsDir => Path.Combine(Root, "projects");
    public static string ToolDll => Path.Combine(Root, "src", "Burger.Tools", "bin", "Release", "net8.0", "burger.dll");
    public static string GameExe => Path.Combine(Root, "src", "Burger.Game", "bin", "Release", "net8.0", "Burger.Game.exe");
    public static string DocsDir => Path.Combine(Root, "docs");

    private static string FindRoot()
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

    /// <summary>Project folder (the one containing game/) from a name in projects/ or a path; null if not found.</summary>
    public static string? ResolveProject(string nameOrPath)
    {
        foreach (var candidate in new[] { nameOrPath, Path.Combine(ProjectsDir, nameOrPath) })
        {
            try
            {
                if (Directory.Exists(Path.Combine(candidate, "game"))) return Path.GetFullPath(candidate);
                if (File.Exists(Path.Combine(candidate, "game.txt"))) return Path.GetFullPath(Path.Combine(candidate, ".."));
            }
            catch { /* invalid path */ }
        }
        return null;
    }

    public static List<string> ListProjects()
    {
        if (!Directory.Exists(ProjectsDir)) return new List<string>();
        return Directory.GetDirectories(ProjectsDir)
            .Where(d => Directory.Exists(Path.Combine(d, "game")))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Runs <c>dotnet burger.dll args</c>, streaming output lines to <paramref name="output"/> (called on a worker thread).
    /// Returns the exit code.</summary>
    public static Task<int> RunToolAsync(IEnumerable<string> args, Action<string> output)
    {
        var list = new List<string> { ToolDll };
        list.AddRange(args);
        if (!File.Exists(ToolDll))
        {
            output("The engine tools are not built yet - building now (dotnet build)...");
            return RunAsync("dotnet", new[] { "build", Path.Combine(Root, "BurgerEngine.sln"), "-c", "Release", "-nologo", "-v", "q" }, output)
                .ContinueWith(t => t.Result != 0 ? Task.FromResult(t.Result) : RunAsync("dotnet", list, output)).Unwrap();
        }
        return RunAsync("dotnet", list, output);
    }

    public static Task<int> RunAsync(string exe, IEnumerable<string> args, Action<string> output, string? workDir = null)
    {
        return Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir ?? Root,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = new Process { StartInfo = psi };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) output(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                output("Could not start " + exe + ": " + ex.Message + (exe == "dotnet" ? " (is the .NET SDK installed?)" : ""));
                return -1;
            }
        });
    }

    /// <summary>Starts the game for a game folder without waiting.</summary>
    public static void StartGame(string gameDir, Action<string> output, string? tag = null)
    {
        var args = new List<string> { "--game", gameDir };
        if (tag != null) { args.Add("--tag"); args.Add(tag); }
        if (File.Exists(GameExe))
        {
            var psi = new ProcessStartInfo(GameExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(GameExe)! };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            output("Started the game.");
            return;
        }
        output("The game is not built yet - building and starting it (first time takes a minute)...");
        var full = new List<string> { "run", "--project", Path.Combine(Root, "src", "Burger.Game"), "-c", "Release", "--" };
        full.AddRange(args);
        _ = RunAsync("dotnet", full, output);
    }

    public static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Burger.Core.Log.Warn("Could not open " + path + ": " + ex.Message); }
    }
}
