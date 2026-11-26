namespace Hibi.Core;

/// <summary>
/// 古い完了タスクを tasks.json から archive\tasks-YYYY.json に移して、tasks.json を小さく保つ。
/// <list type="bullet">
/// <item>移すのは、完了していて最後に触ってから keepDays 日（設定の archiveAfterDays）以上たったもの</item>
/// <item>ファイルは最後に触った業務日の年で分ける。同じ年のファイルには足していく</item>
/// <item>一覧・プロジェクトや作業分類の候補には出なくなるが、過去の日報・週報・月報には入る
/// （<see cref="ViewFrom"/> で読み足す）</item>
/// <item>移したもののうち最後に触った日がいちばん新しい日を <see cref="TaskData.ArchivedThrough"/> に残す。
/// 日数の設定をあとで変えても、どの期間で読み足せばいいかがわかる</item>
/// <item>先にアーカイブを書いてから tasks.json から外す。途中で落ちても両方に残るだけで消えない
/// （読み足すときは id が重なれば tasks.json のほうを使う）</item>
/// </list>
/// </summary>
public static class TaskArchive
{
    /// <summary>移す予定（年ごとの件数）。移すものがなければ空。</summary>
    public static IReadOnlyList<(int Year, int Count)> Plan(TaskService service, int keepDays) =>
        service.ArchiveCandidates(keepDays)
            .GroupBy(t => service.LastTouchedDay(t).Year)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Count()))
            .ToList();

    /// <summary>
    /// 移して、移した件数を返す。tasks.json は <see cref="TaskService.Changed"/> で保存する側が書く
    /// （ここではアーカイブのファイルだけ書く）。読めないアーカイブがあれば <see cref="DataLoadException"/> で何もしない。
    /// </summary>
    public static int Archive(TaskService service, TaskStore store, int keepDays)
    {
        var groups = service.ArchiveCandidates(keepDays)
            .GroupBy(t => service.LastTouchedDay(t).Year)
            .Select(g => (Year: g.Key, Tasks: g.ToList(), Data: store.LoadArchive(g.Key))) // 先に全部読む（書く前に失敗させる）
            .ToList();
        if (groups.Count == 0) return 0;

        foreach (var (year, tasks, data) in groups)
        {
            var ids = tasks.Select(t => t.Id).ToHashSet();
            data.SchemaVersion = TaskData.CurrentSchemaVersion;
            data.Tasks = [.. data.Tasks.Where(t => !ids.Contains(t.Id)), .. tasks];
            data.Tasks.Sort((a, b) => Nullable.Compare(a.CompletedAt, b.CompletedAt));
            data.Days = [];
            store.SaveArchive(year, data);
        }

        var moved = groups.SelectMany(g => g.Tasks).ToList();
        var through = moved.Max(service.LastTouchedDay);
        if (service.Data.ArchivedThrough is not { } prev || prev < through) service.Data.ArchivedThrough = through;
        var all = moved.Select(t => t.Id).ToHashSet();
        service.RemoveArchived(all); // ArchivedThrough もここで保存される
        return all.Count;
    }

    /// <summary>
    /// <paramref name="from"/> からの期間の報告に使うサービス。アーカイブしたタスクが入りうる期間なら読み足し、
    /// そうでなければ <paramref name="service"/> をそのまま返す。
    /// </summary>
    public static TaskService ViewFrom(TaskService service, TaskStore store, DateOnly from)
    {
        // アーカイブしたタスクの作業記録は ArchivedThrough より後の日にはない
        if (service.Data.ArchivedThrough is not { } through || from > through) return service;
        // 最後に触った年で分けているので、期間に作業記録のあるタスクは from の年以降のファイルにある
        var years = store.ArchiveYears().Where(y => y >= from.Year).ToList();
        if (years.Count == 0) return service;
        return service.WithArchived(years.SelectMany(y => store.LoadArchive(y).Tasks));
    }
}
