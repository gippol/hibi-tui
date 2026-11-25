namespace Hibi.Core;

/// <summary>
/// タスク一覧の絞り込み。スペースで区切った語をすべて含むものに当たる（大文字小文字は区別しない）。
/// 語はタイトル・プロジェクト・作業分類に当たる。<c>#</c> で始まる語は `プロジェクト/作業分類` だけに当たる
/// （<c>#開発/設計</c> のようにまとめても探せる）。
/// </summary>
public static class TaskFilter
{
    public static bool Matches(TaskItem task, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var ok = term.StartsWith('#')
                ? Contains(task.ProjectLabel, term[1..]) || term.Length == 1
                : Contains(task.Title, term) || Contains(task.Project, term) || Contains(task.Activity, term);
            if (!ok) return false;
        }
        return true;
    }

    private static bool Contains(string? s, string term) =>
        s is not null && s.Contains(term, StringComparison.OrdinalIgnoreCase);
}
