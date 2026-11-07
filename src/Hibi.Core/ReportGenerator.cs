using System.Globalization;
using System.Text;

namespace Hibi.Core;

/// <summary>日報（要件 4.4）と月報（要件 4.6）の Markdown の生成。</summary>
public static class ReportGenerator
{
    public const string NoProject = "その他";
    private const string None = "なし";

    // ---------- 日報 ----------

    public static string Generate(TaskService service, DateOnly day)
    {
        var completed = service.CompletedOn(day);
        // その日の時点で完了していないもの（過去の日報なら、あとで完了したものも進行中）
        var inProgress = service.Data.Tasks
            .Where(t => !(t.CompletedAt is { } c && service.Calendar.DayOf(c) <= day) && service.WorkedOn(t, day))
            .OrderBy(t => t.WorkLogs.Where(l => service.Calendar.DayOf(l.Start) == day).Min(l => l.Start))
            .ToList();

        var total = service.Data.Tasks.Aggregate(TimeSpan.Zero, (acc, t) => acc + service.DurationOn(t, day));

        var record = service.GetDay(day);
        var tomorrow = (record?.TomorrowTaskIds ?? [])
            .Select(service.Find)
            .OfType<TaskItem>()
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"# 日報 {BusinessDayCalendar.Key(day)}（{BusinessDayCalendar.JaWeekday(day)}）\n\n");
        sb.Append($"稼働合計: {DurationFormat.Short(total)}\n\n");

        // 累計はその日まで（今日の日報なら全期間と同じ）
        TimeSpan UpToDay(TaskItem t) => service.DurationIn(t, DateOnly.MinValue, day);

        sb.Append("## 完了\n");
        AppendGrouped(sb, completed, t => service.DurationOn(t, day), UpToDay);

        sb.Append("\n## 進行中\n");
        AppendGrouped(sb, inProgress, t => service.DurationOn(t, day), UpToDay);

        sb.Append("\n## 明日の予定\n");
        if (tomorrow.Count == 0) sb.Append(None).Append('\n');
        foreach (var t in tomorrow) sb.Append($"- {t.Title}\n");

        sb.Append("\n## 所感\n");
        var note = record?.Note;
        sb.Append(string.IsNullOrWhiteSpace(note) ? None : note.ReplaceLineEndings("\n")).Append('\n');

        return sb.ToString();
    }

    // ---------- 月報 ----------

    /// <summary><paramref name="month"/> を含む月（業務日の1日〜末日）の月報。</summary>
    public static string GenerateMonthly(TaskService service, DateOnly month)
    {
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var completed = service.CompletedIn(from, to);
        var completedIds = completed.Select(t => t.Id).ToHashSet();
        // 月内に作業して、月内には完了していないもの（あとで完了したものも、その月の時点では進行中）
        var inProgress = service.Data.Tasks
            .Where(t => !completedIds.Contains(t.Id) && service.WorkedIn(t, from, to))
            .OrderBy(t => service.LogsIn(t, from, to).Min(l => l.Start))
            .ToList();

        // 日別・プロジェクト別の合計（秒単位で足してから表示で切り捨てる, 4.4.3）
        var now = service.Now;
        var byDay = new SortedDictionary<DateOnly, TimeSpan>();
        var byProject = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var t in service.Data.Tasks)
            foreach (var l in service.LogsIn(t, from, to))
            {
                var d = l.DurationAt(now);
                var day = service.Calendar.DayOf(l.Start);
                byDay[day] = byDay.GetValueOrDefault(day) + d;
                var key = t.Project ?? "";
                byProject[key] = byProject.GetValueOrDefault(key) + d;
            }
        var total = byDay.Values.Aggregate(TimeSpan.Zero, (acc, d) => acc + d);

        var notes = service.Data.Days
            .Select(kv => (Day: ParseKey(kv.Key), kv.Value.Note))
            .Where(x => x.Day is { } d && d >= from && d <= to && !string.IsNullOrWhiteSpace(x.Note))
            .OrderBy(x => x.Day)
            .ToList();

        var sb = new StringBuilder();
        sb.Append($"# 月報 {from:yyyy-MM}\n\n");
        sb.Append($"期間: {Day(from, full: true)} 〜 {Day(to, full: true)}\n");
        sb.Append($"稼働合計: {DurationFormat.Short(total)}（稼働 {byDay.Count}日）\n\n");

        sb.Append("## プロジェクト別\n");
        if (byProject.Count == 0) sb.Append(None).Append('\n');
        foreach (var key in OrderProjects(byProject.Keys.Select(k => k.Length == 0 ? null : k)))
            sb.Append($"- {key ?? NoProject} — {DurationFormat.Short(byProject[key ?? ""])}\n");

        sb.Append("\n## 完了\n");
        AppendGrouped(sb, completed, t => service.DurationIn(t, from, to), service.TotalDuration);

        sb.Append("\n## 進行中\n");
        AppendGrouped(sb, inProgress, t => service.DurationIn(t, from, to), service.TotalDuration);

        sb.Append("\n## 日別\n");
        if (byDay.Count == 0) sb.Append(None).Append('\n');
        foreach (var (day, d) in byDay)
            sb.Append($"- {Day(day, full: false)} — {DurationFormat.Short(d)}\n");

        sb.Append("\n## 所感\n");
        if (notes.Count == 0) sb.Append(None).Append('\n');
        for (var i = 0; i < notes.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append($"### {Day(notes[i].Day!.Value, full: false)}\n");
            sb.Append(notes[i].Note!.ReplaceLineEndings("\n")).Append('\n');
        }

        return sb.ToString();
    }

    // ---------- helpers ----------

    private static string Day(DateOnly d, bool full) =>
        $"{d.ToString(full ? "yyyy-MM-dd" : "MM-dd", CultureInfo.InvariantCulture)}（{BusinessDayCalendar.JaWeekday(d)}）";

    private static DateOnly? ParseKey(string key) =>
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>プロジェクト名の順。プロジェクトなし（null）は最後。</summary>
    private static IEnumerable<string?> OrderProjects(IEnumerable<string?> projects) =>
        projects.OrderBy(p => p is null ? 1 : 0).ThenBy(p => p, StringComparer.Ordinal);

    private static void AppendGrouped(StringBuilder sb, IReadOnlyList<TaskItem> tasks,
        Func<TaskItem, TimeSpan> duration, Func<TaskItem, TimeSpan> cumulative)
    {
        if (tasks.Count == 0)
        {
            sb.Append(None).Append('\n');
            return;
        }

        // グループ内は tasks の順を保つ。プロジェクトなし（null）は最後に「その他」として出す
        var groups = tasks.ToLookup(t => t.Project);
        foreach (var project in OrderProjects(groups.Select(g => g.Key)))
        {
            sb.Append($"### {project ?? NoProject}\n");
            foreach (var t in groups[project])
                sb.Append($"- {t.Title} — {DurationFormat.Short(duration(t))}（累計 {DurationFormat.Short(cumulative(t))}）\n");
        }
    }
}
