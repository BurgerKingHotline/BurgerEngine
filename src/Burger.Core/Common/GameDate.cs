using System.Globalization;

namespace Burger.Core;

/// <summary>
/// In-game date using the Paradox calendar (365 days per year, no leap years) written as <c>year.month.day</c>,
/// e.g. <c>1936.1.1</c>. Internally stored as total days since 1.1.1 plus the hour of day.
/// </summary>
public readonly struct GameDate : IComparable<GameDate>, IEquatable<GameDate>
{
    public static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    private static readonly int[] CumulativeDays;
    public static readonly string[] MonthNames =
        { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    static GameDate()
    {
        CumulativeDays = new int[13];
        for (int i = 0; i < 12; i++) CumulativeDays[i + 1] = CumulativeDays[i] + DaysInMonth[i];
    }

    /// <summary>Days since 1.1.1 (day 0).</summary>
    public readonly int TotalDays;
    public readonly int Hour;

    public GameDate(int totalDays, int hour = 0)
    {
        TotalDays = totalDays;
        Hour = hour;
    }

    public GameDate(int year, int month, int day, int hour = 0)
    {
        month = Math.Clamp(month, 1, 12);
        day = Math.Clamp(day, 1, DaysInMonth[month - 1]);
        TotalDays = (year - 1) * 365 + CumulativeDays[month - 1] + (day - 1);
        Hour = Math.Clamp(hour, 0, 23);
    }

    public int Year => TotalDays / 365 + 1;
    public int DayOfYear => TotalDays % 365; // 0-based

    public int Month
    {
        get
        {
            int d = DayOfYear;
            for (int m = 0; m < 12; m++)
                if (d < CumulativeDays[m + 1]) return m + 1;
            return 12;
        }
    }

    public int Day => DayOfYear - CumulativeDays[Month - 1] + 1;

    public long TotalHours => (long)TotalDays * 24 + Hour;

    public GameDate AddHours(int h)
    {
        long total = TotalHours + h;
        return new GameDate((int)(total / 24), (int)(total % 24));
    }

    public GameDate AddDays(int d) => new(TotalDays + d, Hour);
    public GameDate AddMonths(int m)
    {
        int y = Year, mo = Month - 1 + m;
        y += Math.DivRem(mo, 12, out int rem) ;
        if (rem < 0) { rem += 12; y--; }
        return new GameDate(y, rem + 1, Math.Min(Day, DaysInMonth[rem]), Hour);
    }
    public GameDate AddYears(int y) => new(TotalDays + y * 365, Hour);

    /// <summary>Whole years between two dates (used for character ages).</summary>
    public static int YearsBetween(GameDate from, GameDate to) => (to.TotalDays - from.TotalDays) / 365;

    public static bool TryParse(string? s, out GameDate date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split('.');
        if (parts.Length < 1 || parts.Length > 4) return false;
        int[] v = { 1, 1, 1, 0 };
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
        if (v[1] < 1 || v[1] > 12) return false;
        if (v[2] < 1 || v[2] > DaysInMonth[v[1] - 1]) return false;
        date = new GameDate(v[0], v[1], v[2], v[3]);
        return true;
    }

    public static GameDate Parse(string s, GameDate fallback)
        => TryParse(s, out var d) ? d : fallback;

    /// <summary>Script format: <c>1936.1.1</c>.</summary>
    public string ToScript() => $"{Year}.{Month}.{Day}";

    public string ToDisplay(bool withHour = false)
        => withHour ? $"{Hour:00}:00, {Day} {MonthNames[Month - 1]} {Year}" : $"{Day} {MonthNames[Month - 1]} {Year}";

    public override string ToString() => ToScript();

    public int CompareTo(GameDate o) => TotalHours.CompareTo(o.TotalHours);
    public bool Equals(GameDate o) => TotalDays == o.TotalDays && Hour == o.Hour;
    public override bool Equals(object? obj) => obj is GameDate d && Equals(d);
    public override int GetHashCode() => HashCode.Combine(TotalDays, Hour);

    public static bool operator <(GameDate a, GameDate b) => a.TotalHours < b.TotalHours;
    public static bool operator >(GameDate a, GameDate b) => a.TotalHours > b.TotalHours;
    public static bool operator <=(GameDate a, GameDate b) => a.TotalHours <= b.TotalHours;
    public static bool operator >=(GameDate a, GameDate b) => a.TotalHours >= b.TotalHours;
    public static bool operator ==(GameDate a, GameDate b) => a.Equals(b);
    public static bool operator !=(GameDate a, GameDate b) => !a.Equals(b);
}
