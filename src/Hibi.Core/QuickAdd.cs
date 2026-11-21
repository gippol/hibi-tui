using System.Globalization;

namespace Hibi.Core;

/// <summary>
/// タスクを追加するときのタイトルの一行記法。スペース（全角も）で区切った語のうち、次のものを取り出す。
/// <list type="bullet">
/// <item><c>#名前</c>: プロジェクト（<c>#123</c> のように数字だけなら番号とみてタイトルに残す）</item>
/// <item><c>!</c> / <c>!高</c> / <c>!普通</c> / <c>!低</c>: 優先度</item>
/// <item><c>@今日</c> / <c>@明日</c> / <c>@yyyy-M-d</c> / <c>@M-d</c>（<c>/</c> 区切りも可）: 期限。
/// 月日だけで今日より前なら来年にする</item>
/// </list>
/// 当てはまらない語はタイトルに残す。同じ種類を2回書いたら後のほうを使う。
/// </summary>
public static class QuickAdd
{
    /// <summary>取り出した結果。書かれていなかったものは null。</summary>
    public sealed record Result(string Title, string? Project, DateOnly? Due, TaskPriority? Priority);

    public static Result Parse(string text, DateOnly today)
    {
        string? project = null;
        DateOnly? due = null;
        TaskPriority? priority = null;
        var rest = new List<string>();
        var consumed = false;

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (AsProject(word) is { } p) project = p;
            else if (AsPriority(word) is { } pr) priority = pr;
            else if (AsDue(word, today) is { } d) due = d;
            else
            {
                rest.Add(word);
                continue;
            }
            consumed = true;
        }

        // 記法がなければ、全角スペースなども含めて打ったとおりのタイトルにする
        var title = consumed ? string.Join(' ', rest) : text.Trim();
        return new Result(title, project, due, priority);
    }

    private static string? AsProject(string word) =>
        word.Length > 1 && word[0] == '#' && !word[1..].All(char.IsAsciiDigit) ? word[1..] : null;

    private static TaskPriority? AsPriority(string word) => word switch
    {
        "!" or "!高" => TaskPriority.High,
        "!普通" => TaskPriority.Normal,
        "!低" => TaskPriority.Low,
        _ => null,
    };

    private static DateOnly? AsDue(string word, DateOnly today)
    {
        if (word.Length < 2 || word[0] != '@') return null;
        var s = word[1..];
        if (s == "今日") return today;
        if (s == "明日") return today.AddDays(1);

        // yyyy-M-d か M-d（/ 区切りも可）。年がなければ今年、それが今日より前なら来年
        var parts = s.Split('-', '/');
        if (parts.Length is not (2 or 3) || !parts.All(p => p.Length is >= 1 and <= 4 && p.All(char.IsAsciiDigit)))
            return null;
        var n = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        if (n.Length == 3) return parts[0].Length == 4 ? TryDate(n[0], n[1], n[2]) : null;
        var thisYear = TryDate(today.Year, n[0], n[1]);
        return thisYear < today ? TryDate(today.Year + 1, n[0], n[1]) : thisYear;
    }

    private static DateOnly? TryDate(int y, int m, int d) =>
        y is >= 1 and <= 9999 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m) ? new DateOnly(y, m, d) : null;
}
