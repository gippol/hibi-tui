using System.Collections.ObjectModel;
using System.Globalization;
using Hibi.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView は v2.5 で非推奨（別パッケージの EditorView が後継）だが、v1 では標準の TextView で足りる
#pragma warning disable CS0618

namespace Hibi;

internal sealed record TaskInput(string Title, string? Project, string? Memo, DateOnly? Due, TaskPriority Priority);

/// <summary>各種ダイアログ（要件 5.3）。</summary>
internal static class Dialogs
{
    private const string Ok = "OK";
    private const string Cancel = "キャンセル";

    public static void Error(IApplication app, string message) =>
        MessageBox.ErrorQuery(app, "エラー", message, Ok);

    public static void Info(IApplication app, string title, string message) =>
        MessageBox.Query(app, title, message, Ok);

    public static bool Confirm(IApplication app, string title, string message, string yes, string no = Cancel) =>
        MessageBox.Query(app, title, message, yes, no) == 0;

    /// <summary>
    /// ボタンを付けたダイアログを開き、押されたボタンの番号を返す（Esc なら null）。
    /// 結果は Dialog.Result に頼らず自前で持つ:
    /// ボタン = その番号、<paramref name="submitOnEnter"/> の欄で Enter = 0、Ctrl+S = 0。
    /// </summary>
    private static int? Show(IApplication app, Dialog dialog, string[] buttons, params View[] submitOnEnter)
    {
        int? result = null;
        void Finish(int index)
        {
            result = index;
            dialog.RequestStop();
        }

        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Text = buttons[i], IsDefault = i == 0 };
            button.Accepting += (_, e) =>
            {
                e.Handled = true;
                Finish(index);
            };
            dialog.AddButton(button);
        }
        foreach (var field in submitOnEnter)
            field.Accepting += (_, e) =>
            {
                e.Handled = true;
                Finish(0);
            };
        dialog.KeyDown += (_, key) =>
        {
            if (key != Key.S.WithCtrl) return;
            key.Handled = true;
            Finish(0);
        };

        app.Run(dialog);
        return result;
    }

    // ---------- タスクの追加・編集 ----------

    /// <summary>優先度の選択肢（表示順）。</summary>
    private static readonly TaskPriority[] Priorities = [TaskPriority.High, TaskPriority.Normal, TaskPriority.Low];

    public static string PriorityLabel(TaskPriority p) => p switch
    {
        TaskPriority.High => "高",
        TaskPriority.Low => "低",
        _ => "普通",
    };

    /// <summary>タスクの追加・編集。<paramref name="initial"/> が null なら空の状態から。</summary>
    public static TaskInput? EditTask(IApplication app, string title, TaskInput? initial, IReadOnlyList<string> projects)
    {
        var taskTitle = initial?.Title;
        var project = initial?.Project;
        var memo = initial?.Memo;
        var dueText = initial?.Due is { } due0 ? BusinessDayCalendar.Key(due0) : "";
        var priority = initial?.Priority ?? TaskPriority.Normal;
        while (true)
        {
            using var d = new Dialog { Title = title, Width = Dim.Percent(80), Height = 24 };

            var titleField = new TextField { X = 14, Y = 0, Width = Dim.Fill(1), Text = taskTitle ?? "" };
            var projectField = new TextField { X = 14, Y = 2, Width = Dim.Fill(1), Text = project ?? "" };
            var hint = new Label
            {
                X = 14,
                Y = 3,
                Width = Dim.Fill(1),
                Text = projects.Count > 0 ? "↓: 既存から選ぶ（入力中の文字で絞り込み）" : "（新しいプロジェクト名を入力。必須）",
            };
            projectField.KeyDown += (_, key) =>
            {
                if (key != Key.CursorDown || projects.Count == 0) return;
                key.Handled = true;
                if (ChooseProject(app, projects, projectField.Text) is not { } chosen) return;
                projectField.Text = chosen;
                projectField.MoveEnd();
            };
            var dueField = new TextField { X = 14, Y = 5, Width = 12, Text = dueText };
            var dueHint = new Label { X = 27, Y = 5, Text = "yyyy-MM-dd（空: 期限なし）" };
            var prioritySelector = new OptionSelector
            {
                X = 14,
                Y = 7,
                Orientation = Orientation.Horizontal,
                Labels = Priorities.Select(PriorityLabel).ToList(),
                Value = Array.IndexOf(Priorities, priority),
            };
            var memoView = new TextView
            {
                X = 14,
                Y = 9,
                Width = Dim.Fill(1),
                Height = Dim.Fill(1),
                Text = memo ?? "",
                WordWrap = true,
                TabKeyAddsTab = false,
            };
            d.Add(
                new Label { X = 1, Y = 0, Text = "タイトル" }, titleField,
                new Label { X = 1, Y = 2, Text = "プロジェクト" }, projectField, hint,
                new Label { X = 1, Y = 5, Text = "期限" }, dueField, dueHint,
                new Label { X = 1, Y = 7, Text = "優先度" }, prioritySelector,
                new Label { X = 1, Y = 9, Text = "メモ" }, memoView,
                new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter/Ctrl+S: OK  Tab: 次の欄  Esc: キャンセル" });
            titleField.SetFocus();

            if (Show(app, d, [Ok, Cancel], titleField, projectField, dueField) != 0) return null;

            taskTitle = titleField.Text;
            project = projectField.Text;
            memo = memoView.Text;
            dueText = dueField.Text.Trim();
            priority = prioritySelector.Value is int pi && pi >= 0 && pi < Priorities.Length ? Priorities[pi] : TaskPriority.Normal;

            if (string.IsNullOrWhiteSpace(taskTitle))
            {
                Error(app, "タイトルを入力してください。");
                continue;
            }
            DateOnly? due = null;
            if (dueText.Length > 0)
            {
                if (!DateOnly.TryParseExact(dueText, "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    Error(app, "期限は yyyy-MM-dd の形で入力してください（例: 2026-11-10）。空なら期限なし。");
                    continue;
                }
                due = parsed;
            }
            return new TaskInput(taskTitle, project, memo, due, priority);
        }
    }

    /// <summary>
    /// 既存のプロジェクトから1つ選ぶ。<paramref name="filter"/> を含むものだけ出す（該当なしなら全部）。
    /// キャンセルなら null。
    /// </summary>
    private static string? ChooseProject(IApplication app, IReadOnlyList<string> projects, string? filter)
    {
        var candidates = string.IsNullOrWhiteSpace(filter)
            ? projects.ToList()
            : projects.Where(p => p.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) candidates = projects.ToList();

        using var d = new Dialog
        {
            Title = "プロジェクトを選ぶ",
            Width = Dim.Percent(50),
            // 枠2 + ボタン(影込み)2 + ダイアログの影1 + 説明ラベル1 = 約6行がリスト以外に使われる。
            // 足りないとリストの高さが0になり何も出ないので、1行余裕を持たせる
            Height = Math.Min(candidates.Count, 15) + 7,
        };
        var list = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
        list.SetSource(new ObservableCollection<string>(candidates));
        list.Value = 0;
        d.Add(list, new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "↑↓: 移動  Enter: 決定  Esc: キャンセル" });
        list.SetFocus();

        if (Show(app, d, [Ok, Cancel], list) != 0) return null;
        return list.Value is int i && i >= 0 && i < candidates.Count ? candidates[i] : null;
    }

    // ---------- 作業記録の修正（F-W-05） ----------

    private const string WorkLogHelp =
        "# 1行に1つの作業記録。保存すると今の内容で置き換えます。\n" +
        "#   2026-11-06 10:00 - 12:30            （終了が開始より前の時刻なら翌日）\n" +
        "#   2026-11-06 23:00 - 2026-11-07 01:00\n" +
        "#   2026-11-06 13:00 -                  （計測中）\n";

    public static void EditWorkLogs(IApplication app, TaskItem task, Action<List<WorkLog>> apply)
    {
        var text = WorkLogHelp + WorkLogText.Format(task.WorkLogs);
        while (true)
        {
            if (EditText(app, $"作業記録: {task.Title}", text) is not { } edited) return;
            text = edited;
            try
            {
                apply(WorkLogText.Parse(text, dt => TimeZoneInfo.Local.GetUtcOffset(dt)));
                return;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                Error(app, ex.Message);
            }
        }
    }

    // ---------- 複数行テキスト（所感など） ----------

    public static string? EditText(IApplication app, string title, string? text)
    {
        using var d = new Dialog { Title = title, Width = Dim.Percent(85), Height = Dim.Percent(70) };
        var view = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Text = text ?? "",
            TabKeyAddsTab = false,
        };
        d.Add(view, new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Ctrl+S: 保存  Esc: キャンセル" });
        view.SetFocus();
        return Show(app, d, ["保存", Cancel]) == 0 ? view.Text : null;
    }

    // ---------- 日時の入力 ----------

    public static void AskDateTime(IApplication app, string title, string prompt, DateTimeOffset initial,
        Action<DateTimeOffset> apply)
    {
        var text = initial.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        while (true)
        {
            using var d = new Dialog { Title = title, Width = 50, Height = 10 };
            var field = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = text };
            d.Add(new Label { X = 1, Y = 0, Text = prompt }, field);
            field.SetFocus();
            if (Show(app, d, [Ok, Cancel], field) != 0) return;

            text = field.Text;
            if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd H:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt))
            {
                Error(app, "yyyy-MM-dd HH:mm の形で入力してください。");
                continue;
            }
            try
            {
                apply(new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)));
                return;
            }
            catch (ArgumentException ex)
            {
                Error(app, ex.Message);
            }
        }
    }

    // ---------- 月の入力（月報） ----------

    /// <summary>年月（yyyy-MM）を入れてもらう。その月の1日を返す。キャンセルなら null。</summary>
    public static DateOnly? AskMonth(IApplication app, string title, DateOnly initial)
    {
        var text = initial.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        while (true)
        {
            using var d = new Dialog { Title = title, Width = 50, Height = 10 };
            var field = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = text };
            d.Add(new Label { X = 1, Y = 0, Text = "対象の月（yyyy-MM）" }, field);
            field.SetFocus();
            if (Show(app, d, ["出力", Cancel], field) != 0) return null;

            text = field.Text;
            if (DateOnly.TryParseExact(text.Trim(), "yyyy-M", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                return m;
            Error(app, "yyyy-MM の形で入力してください（例: 2026-11）。");
        }
    }

    // ---------- 1行の入力 ----------

    /// <summary>1行の文字を入れてもらう。キャンセルなら null。</summary>
    public static string? AskLine(IApplication app, string title, string prompt, string? initial, string help)
    {
        using var d = new Dialog { Title = title, Width = Dim.Percent(60), Height = 10 };
        var field = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = initial ?? "" };
        d.Add(
            new Label { X = 1, Y = 0, Text = prompt }, field,
            new Label { X = 1, Y = 3, Width = Dim.Fill(1), Text = help });
        field.SetFocus();
        field.MoveEnd();
        return Show(app, d, [Ok, Cancel], field) == 0 ? field.Text : null;
    }

    // ---------- 業務日の入力（過去の所感・日報） ----------

    /// <summary>業務日（yyyy-MM-dd）を入れてもらう。<paramref name="max"/> より後は受け付けない。キャンセルなら null。</summary>
    public static DateOnly? AskDay(IApplication app, string title, DateOnly initial, DateOnly max,
        string prompt = "対象の業務日（yyyy-MM-dd）")
    {
        var text = BusinessDayCalendar.Key(initial);
        while (true)
        {
            using var d = new Dialog { Title = title, Width = 50, Height = 10 };
            var field = new TextField { X = 1, Y = 1, Width = Dim.Fill(1), Text = text };
            d.Add(new Label { X = 1, Y = 0, Text = prompt }, field);
            field.SetFocus();
            if (Show(app, d, [Ok, Cancel], field) != 0) return null;

            text = field.Text;
            if (!DateOnly.TryParseExact(text.Trim(), "yyyy-M-d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                Error(app, "yyyy-MM-dd の形で入力してください（例: 2026-11-06）。");
            else if (day > max)
                Error(app, "未来の日付は指定できません。");
            else
                return day;
        }
    }

    // ---------- 明日の予定（F-R-02〜04） ----------

    /// <summary>明日の予定を選ぶ。キャンセルなら null。</summary>
    public static List<Guid>? ChooseTomorrow(IApplication app, TaskService service, DateOnly day)
    {
        var tasks = service.OpenTasks().ToList();
        var chosen = new HashSet<Guid>(service.GetDay(day)?.TomorrowTaskIds ?? []);
        var rows = new ObservableCollection<string>();

        using var d = new Dialog
        {
            Title = $"日報を出力（{BusinessDayCalendar.Key(day)}）: 明日の予定を選ぶ",
            Width = Dim.Percent(80),
            Height = Dim.Percent(80),
        };
        var list = new ShortcutListView
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = true,
            MarkMultiple = true,
        };
        list.Shortcut = key =>
        {
            if (key != Key.A) return false;
            AddFromDialog();
            return true;
        };
        list.SetSource(rows);
        d.Add(new Label { X = 0, Y = 0, Text = "Space: 選ぶ/外す  a: タスクを追加  Ctrl+S: 出力  Esc: キャンセル" }, list);

        void Reload()
        {
            rows.Clear();
            foreach (var t in tasks) rows.Add(t.Project is { } p ? $"{t.Title}  #{p}" : t.Title);
            for (var i = 0; i < tasks.Count; i++)
                list.Source!.SetMark(i, chosen.Contains(tasks[i].Id));
            list.SetNeedsDraw();
        }

        void AddFromDialog()
        {
            if (EditTask(app, "明日の予定にタスクを追加", null, service.Projects()) is not { } r) return;
            // 印を今の状態から引き継ぐ
            Collect();
            var t = service.Add(r.Title, r.Project, r.Memo, r.Due, r.Priority);
            tasks.Add(t);
            chosen.Add(t.Id);
            Reload();
            list.Value = tasks.Count - 1;
        }

        void Collect()
        {
            for (var i = 0; i < tasks.Count; i++)
                if (list.Source!.IsMarked(i)) chosen.Add(tasks[i].Id);
                else chosen.Remove(tasks[i].Id);
        }

        Reload();
        list.SetFocus();
        if (Show(app, d, ["出力", Cancel]) != 0) return null;

        Collect();
        // 並びは一覧の順
        return tasks.Where(t => chosen.Contains(t.Id)).Select(t => t.Id).ToList();
    }
}
