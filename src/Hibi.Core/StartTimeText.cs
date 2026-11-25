using System.Globalization;

namespace Hibi.Core;

/// <summary>
/// さかのぼって開始するときの時刻の入力。次のどれか。
/// <list type="bullet">
/// <item><c>-15</c>: 今から15分前</item>
/// <item><c>9:30</c> / <c>09:30</c>: その時刻。今より後なら前の日のその時刻（深夜に前の晩の時刻を書けるように）</item>
/// <item><c>2026-11-26 09:30</c>: その日時</item>
/// </list>
/// 時刻は壁時計（<paramref name="now"/> と同じオフセット）。
/// </summary>
public static class StartTimeText
{
    public static DateTimeOffset Parse(string text, DateTimeOffset now)
    {
        var s = text.Trim();
        if (s.StartsWith('-'))
        {
            if (int.TryParse(s[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
                return Minute(now) - TimeSpan.FromMinutes(minutes);
        }
        else if (TimeOnly.TryParseExact(s, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            var at = new DateTimeOffset(DateOnly.FromDateTime(now.DateTime).ToDateTime(time), now.Offset);
            return at > now ? at.AddDays(-1) : at;
        }
        else if (DateTime.TryParseExact(s, "yyyy-M-d H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return new DateTimeOffset(dt, now.Offset);
        }
        throw new FormatException("開始時刻は 9:30 / -15（15分前）/ 2026-11-26 09:30 のどれかの形で入力してください。");
    }

    /// <summary>秒を切り捨てる（「15分前」を分ちょうどにする）。</summary>
    private static DateTimeOffset Minute(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);
}
