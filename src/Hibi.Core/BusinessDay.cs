using System.Globalization;

namespace Hibi.Core;

/// <summary>日付の切り替わり時刻を基準にした「業務日」の計算。</summary>
public sealed class BusinessDayCalendar(TimeSpan boundary)
{
    public TimeSpan Boundary { get; } = boundary;

    /// <summary>
    /// その時刻が属する業務日。DateTimeOffset が持つオフセット上の壁時計時刻で判定する
    /// （保存データはローカルオフセットで記録している前提）。
    /// </summary>
    public DateOnly DayOf(DateTimeOffset t) => DateOnly.FromDateTime(t.DateTime - Boundary);

    /// <summary>業務日が始まる時刻（その日の切り替わり時刻）。<paramref name="offset"/> の壁時計で表す。</summary>
    public DateTimeOffset StartOf(DateOnly day, TimeSpan offset) =>
        new(day.ToDateTime(TimeOnly.MinValue) + Boundary, offset);

    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly string[] JaWeekdays = ["日", "月", "火", "水", "木", "金", "土"];

    public static string JaWeekday(DateOnly day) => JaWeekdays[(int)day.DayOfWeek];
}
