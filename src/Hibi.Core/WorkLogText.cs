using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hibi.Core;

/// <summary>
/// 作業記録をテキストで編集するための変換。1行1記録:
/// <code>
/// 2026-11-06 10:00 - 12:30          （終了が開始より前の時刻なら翌日扱い）
/// 2026-11-06 23:00 - 2026-11-07 01:00
/// 2026-11-06 13:00 -                （計測中）
/// </code>
/// 空行と # で始まる行は無視する。
/// </summary>
public static partial class WorkLogText
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";

    public static string Format(IEnumerable<WorkLog> logs)
    {
        var sb = new StringBuilder();
        foreach (var l in logs.OrderBy(l => l.Start))
        {
            sb.Append(l.Start.ToString(DateTimeFormat, CultureInfo.InvariantCulture)).Append(" - ");
            if (l.End is { } e)
                sb.Append(e.Date == l.Start.Date
                    ? e.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : e.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <param name="offset">入力された時刻に付けるオフセット（ローカル）。</param>
    public static List<WorkLog> Parse(string text, Func<DateTime, TimeSpan> offset)
    {
        var result = new List<WorkLog>();
        var lineNo = 0;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var m = LineRegex().Match(line);
            if (!m.Success)
                throw new FormatException($"{lineNo}行目の形式が正しくありません: {line}");

            var start = ParseDateTime(m.Groups["sd"].Value + " " + m.Groups["st"].Value, lineNo);
            DateTime? end = null;
            if (m.Groups["et"].Success)
            {
                if (m.Groups["ed"].Success)
                    end = ParseDateTime(m.Groups["ed"].Value + " " + m.Groups["et"].Value, lineNo);
                else
                {
                    var e = start.Date + ParseTime(m.Groups["et"].Value, lineNo);
                    end = e < start ? e.AddDays(1) : e;
                }
            }

            result.Add(new WorkLog
            {
                Start = new DateTimeOffset(start, offset(start)),
                End = end is { } ev ? new DateTimeOffset(ev, offset(ev)) : null,
            });
        }
        return result;
    }

    private static DateTime ParseDateTime(string s, int lineNo) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new FormatException($"{lineNo}行目の日時が正しくありません: {s}");

    private static TimeSpan ParseTime(string s, int lineNo) =>
        TimeSpan.TryParseExact(s, @"h\:mm", CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
            ? t
            : throw new FormatException($"{lineNo}行目の時刻が正しくありません: {s}");

    [GeneratedRegex(@"^(?<sd>\d{4}-\d{2}-\d{2})\s+(?<st>\d{1,2}:\d{2})\s*-\s*(?:(?<ed>\d{4}-\d{2}-\d{2})\s+)?(?<et>\d{1,2}:\d{2})?$")]
    private static partial Regex LineRegex();
}
