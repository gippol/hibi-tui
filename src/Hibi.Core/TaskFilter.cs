namespace Hibi.Core;

/// <summary>
/// タスク一覧の絞り込み。スペースで区切った語をすべて含むものに当たる（大文字小文字は区別しない）。
/// 語はタイトルかプロジェクトに当たる。<c>#</c> で始まる語はプロジェクトだけに当たる。
/// </summary>
public static class TaskFilter
{
    public static bool Matches(TaskItem task, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var ok = term.StartsWith('#')
                ? Contains(task.Project, term[1..]) || term.Length == 1
                : Contains(task.Title, term) || Contains(task.Project, term);
            if (!ok) return false;
        }
        return true;
    }

    private static bool Contains(string? s, string term) =>
        s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);
}
