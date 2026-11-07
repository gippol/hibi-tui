using System.Globalization;

namespace Hibi.Core;

/// <summary>月報・週報の対象期間（業務日。From と To はどちらも含む）。<see cref="Name"/> は見出しとファイル名に使う。</summary>
public sealed record ReportPeriod(DateOnly From, DateOnly To, string Name)
{
    /// <summary><paramref name="day"/> を含む月（1日〜末日）。名前は yyyy-MM。</summary>
    public static ReportPeriod MonthOf(DateOnly day)
    {
        var from = new DateOnly(day.Year, day.Month, 1);
        return new(from, from.AddMonths(1).AddDays(-1), from.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    /// <summary><paramref name="day"/> を含む週（月曜〜日曜）。名前は ISO 週番号の yyyy-Www。</summary>
    public static ReportPeriod WeekOf(DateOnly day)
    {
        var from = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var dt = from.ToDateTime(TimeOnly.MinValue);
        var name = $"{ISOWeek.GetYear(dt)}-W{ISOWeek.GetWeekOfYear(dt):00}";
        return new(from, from.AddDays(6), name);
    }
}
