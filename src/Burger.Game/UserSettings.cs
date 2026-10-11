using System.Globalization;

namespace Burger.Runtime;

/// <summary>
/// Player settings kept between sessions in <c>settings.cfg</c> next to the exe (one <c>key = value</c> per line).
/// </summary>
public static class UserSettings
{
    /// <summary>Most in-game days per real second at speed 5; 0 = as fast as the computer allows.</summary>
    public static double Speed5DaysPerSecond;
    /// <summary>Historical bias of the AI for new games (0 = free alternate history, 1 = strongly historical).</summary>
    public static double HistoricalBias = 0.75;

    private static string PathOf => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.cfg");

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathOf)) return;
            foreach (var raw in File.ReadAllLines(PathOf))
            {
                var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (parts[0] == "speed5_days_per_second") Speed5DaysPerSecond = Math.Max(0, v);
                else if (parts[0] == "historical_bias") HistoricalBias = Math.Clamp(v, 0, 1);
            }
        }
        catch { /* settings are a convenience: the defaults will do */ }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllLines(PathOf, new[]
            {
                $"speed5_days_per_second = {Speed5DaysPerSecond.ToString("0.##", CultureInfo.InvariantCulture)}",
                $"historical_bias = {HistoricalBias.ToString("0.##", CultureInfo.InvariantCulture)}",
            });
        }
        catch { }
    }

    /// <summary>Gives the clock the speed 5 limit (the other speeds come from the game's defines).</summary>
    public static void Apply(Burger.Core.Game game)
    {
        game.Clock.DaysPerSecond[5] = Speed5DaysPerSecond;
    }
}
