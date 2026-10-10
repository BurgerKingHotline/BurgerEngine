using System.Collections.Concurrent;

namespace Burger.Core;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(LogLevel Level, string Message, string? Where, DateTime Time)
{
    public override string ToString()
        => $"[{Time:HH:mm:ss}] {Level.ToString().ToUpperInvariant()}: {(Where != null ? Where + ": " : "")}{Message}";
}

/// <summary>
/// Engine-wide log, similar to Paradox's error.log. Every script/data problem is reported here with
/// file and line so modders can find mistakes quickly. The game shows it with F2; it is also written to
/// <c>logs/error.log</c> next to the game folder.
/// </summary>
public static class Log
{
    private static readonly ConcurrentQueue<LogEntry> _entries = new();
    private static readonly HashSet<string> _once = new();
    private static readonly object _fileLock = new();
    private static string? _filePath;

    public static event Action<LogEntry>? EntryAdded;

    public static IReadOnlyCollection<LogEntry> Entries => _entries;
    public static int ErrorCount { get; private set; }
    public static int WarningCount { get; private set; }

    /// <summary>When true, also echo to the console (used by the CLI tools).</summary>
    public static bool EchoToConsole { get; set; }

    public static void SetLogFile(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"BurgerEngine log - {DateTime.Now}\n");
            _filePath = path;
        }
        catch
        {
            _filePath = null;
        }
    }

    public static void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        lock (_once) _once.Clear();
        ErrorCount = 0;
        WarningCount = 0;
    }

    public static void Info(string msg, string? where = null) => Add(LogLevel.Info, msg, where);
    public static void Warn(string msg, string? where = null) => Add(LogLevel.Warning, msg, where);
    public static void Error(string msg, string? where = null) => Add(LogLevel.Error, msg, where);

    /// <summary>Reports an error only once per unique key (avoids flooding from per-tick scripts).</summary>
    public static void ErrorOnce(string key, string msg, string? where = null)
    {
        lock (_once)
            if (!_once.Add(key)) return;
        Error(msg, where);
    }

    public static void WarnOnce(string key, string msg, string? where = null)
    {
        lock (_once)
            if (!_once.Add(key)) return;
        Warn(msg, where);
    }

    private static void Add(LogLevel level, string msg, string? where)
    {
        var e = new LogEntry(level, msg, where, DateTime.Now);
        _entries.Enqueue(e);
        while (_entries.Count > 5000 && _entries.TryDequeue(out _)) { }
        if (level == LogLevel.Error) ErrorCount++;
        if (level == LogLevel.Warning) WarningCount++;
        if (EchoToConsole)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = level switch { LogLevel.Error => ConsoleColor.Red, LogLevel.Warning => ConsoleColor.Yellow, _ => ConsoleColor.Gray };
            Console.WriteLine(e.ToString());
            Console.ForegroundColor = old;
        }
        if (_filePath != null)
        {
            lock (_fileLock)
            {
                try { File.AppendAllText(_filePath, e + "\n"); } catch { /* logging must never crash the game */ }
            }
        }
        EntryAdded?.Invoke(e);
    }
}
