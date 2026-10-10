namespace Burger.Core.Modding;

/// <summary>
/// The engine's own starter files (<c>assets/</c> in the engine folder): feature packages, presets, base maps and fonts.
/// </summary>
public static class EngineAssets
{
    /// <summary>Finds <c>assets/&lt;sub&gt;</c> by walking up from the running program (works from bin/ folders too).</summary>
    public static string? Find(string sub)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "assets", sub);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>The explanation file for a project's backend folder (null if the engine assets are missing).</summary>
    public static string? BackendReadme()
    {
        var dir = Find("");
        string? p = dir == null ? null : Path.Combine(dir, "README_backend.txt");
        return p != null && File.Exists(p) ? p : null;
    }

    /// <summary>Copies every file of an engine asset folder (e.g. "fonts") into a destination folder, keeping existing files.</summary>
    public static int CopyFolder(string sub, string destDir)
    {
        var dir = Find(sub);
        if (dir == null) return 0;
        Directory.CreateDirectory(destDir);
        int n = 0;
        foreach (var src in Directory.GetFiles(dir))
        {
            string dst = Path.Combine(destDir, Path.GetFileName(src));
            if (File.Exists(dst)) continue;
            File.Copy(src, dst);
            n++;
        }
        return n;
    }
}
