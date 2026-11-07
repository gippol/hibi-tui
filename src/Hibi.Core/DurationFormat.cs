namespace Hibi.Core;

public static class DurationFormat
{
    /// <summary>`1h30m` 形式。分未満は切り捨て。</summary>
    public static string Short(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        var totalMinutes = (long)d.TotalMinutes;
        return $"{totalMinutes / 60}h{totalMinutes % 60:00}m";
    }

    /// <summary>`00:42:15` 形式（タイマー表示用）。</summary>
    public static string Clock(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return $"{(long)d.TotalHours:00}:{d.Minutes:00}:{d.Seconds:00}";
    }
}
