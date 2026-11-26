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

internal sealed record ReportInput(List<Guid> Tomorrow, DayNotes Notes);

internal sealed record TaskInput(string Title, string? Project, string? Activity, string? Memo, DateOnly? Due, TaskPriority Priority);

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

    /// <summary>
    /// タスクの追加・編集。<paramref name="initial"/> が null なら空の状態から。
    /// <paramref name="quickAddToday"/> を渡すと（追加のとき）、タイトルの一行記法（<see cref="QuickAdd"/>）を読む。
    /// 記法で書いたものは、ほかの欄より優先する。<paramref name="activities"/> は既存の作業分類（全プロジェクトで共通）。
    /// </summary>
    public static TaskInput? EditTask(IApplication app, string title, TaskInput? initial, IReadOnlyList<string> projects,
        IReadOnlyList<string> activities, DateOnly? quickAddToday = null)
    {
        var taskTitle = initial?.Title;
        var project = initial?.Project;
        var activity = initial?.Activity;
        var memo = initial?.Memo;
        var dueText = initial?.Due is { } due0 ? BusinessDayCalendar.Key(due0) : "";
        var priority = initial?.Priority ?? TaskPriority.Normal;
        while (true)
        {
            using var d = new Dialog { Title = title, Width = Dim.Percent(80), Height = 26 };

            var titleField = new TextField { X = 14, Y = 0, Width = Dim.Fill(1), Text = taskTitle ?? "" };
            var projectField = new TextField { X = 14, Y = 2, Width = Dim.Fill(1), Text = project ?? "" };
            var hint = new Label
            {
                X = 14,
                Y = 3,
                Width = Dim.Fill(1),
                Text = projects.Count > 0 ? "↓: 既存から選ぶ（入力中の文字で絞り込み）" : "（新しいプロジェクト名を入力。空でもOK）",
            };
            projectField.KeyDown += (_, key) =>
            {
                if (key != Key.CursorDown || projects.Count == 0) return;
                key.Handled = true;
                if (Choose(app, "プロジェクトを選ぶ", projects, projectField.Text) is not { } chosen) return;
                projectField.Text = chosen;
                projectField.MoveEnd();
            };
            var activityField = new TextField { X = 14, Y = 4, Width = Dim.Fill(1), Text = activity ?? "" };
            var activityHint = new Label
            {
                X = 14,
                Y = 5,
                Width = Dim.Fill(1),
                Text = activities.Count > 0 ? "設計・会議など（空でもOK）。↓: 既存から選ぶ（最近使った順）" : "設計・会議など（新しい名前を入力。空でもOK）",
            };
            activityField.KeyDown += (_, key) =>
            {
                if (key != Key.CursorDown || activities.Count == 0) return;
                key.Handled = true;
                if (Choose(app, "作業分類を選ぶ", activities, activityField.Text) is not { } chosen) return;
                activityField.Text = chosen;
                activityField.MoveEnd();
            };
            var dueField = new TextField { X = 14, Y = 7, Width = 12, Text = dueText };
            var dueHint = new Label { X = 27, Y = 7, Text = "yyyy-MM-dd（空: 期限なし）" };
            var prioritySelector = new OptionSelector
            {
                X = 14,
                Y = 9,
                Orientation = Orientation.Horizontal,
                Labels = Priorities.Select(PriorityLabel).ToList(),
                Value = Array.IndexOf(Priorities, priority),
            };
            var memoView = new TextView
            {
                X = 14,
                Y = 11,
                Width = Dim.Fill(1),
                Height = Dim.Fill(1),
                Text = memo ?? "",
                WordWrap = true,
                TabKeyAddsTab = false,
            };
            if (quickAddToday is not null)
                d.Add(new Label
                {
                    X = 14,
                    Y = 1,
                    Width = Dim.Fill(1),
                    Text = "タイトルに書いても入る:  #プロジェクト/作業分類  !高 !低  @期限（11-25, 明日）",
                });
            d.Add(
                new Label { X = 1, Y = 0, Text = "タイトル" }, titleField,
                new Label { X = 1, Y = 2, Text = "プロジェクト" }, projectField, hint,
                new Label { X = 1, Y = 4, Text = "作業分類" }, activityField, activityHint,
                new Label { X = 1, Y = 7, Text = "期限" }, dueField, dueHint,
                new Label { X = 1, Y = 9, Text = "優先度" }, prioritySelector,
                new Label { X = 1, Y = 11, Text = "メモ" }, memoView,
                new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter/Ctrl+S: OK  Tab: 次の欄  Esc: キャンセル" });
            titleField.SetFocus();

            if (Show(app, d, [Ok, Cancel], titleField, projectField, activityField, dueField) != 0) return null;

            taskTitle = titleField.Text;
            project = projectField.Text;
            activity = activityField.Text;
            memo = memoView.Text;
            dueText = dueField.Text.Trim();
            priority = prioritySelector.Value is int pi && pi >= 0 && pi < Priorities.Length ? Priorities[pi] : TaskPriority.Normal;

            if (quickAddToday is { } today)
            {
                // 読み取った結果を各欄の値にする（エラーで出し直したときも、記法ではなく中身で見える）
                var q = QuickAdd.Parse(taskTitle, today);
                taskTitle = q.Title;
                if (q.Project is not null)
                {
                    // `#開発` だけなら作業分類の欄はそのまま、`#開発/設計` なら作業分類も記法のほう
                    project = q.Project;
                    activity = q.Activity ?? activity;
                }
                priority = q.Priority ?? priority;
                if (q.Due is { } qd) dueText = BusinessDayCalendar.Key(qd);
            }

            if (string.IsNullOrWhiteSpace(taskTitle))
            {
                Error(app, "タイトルを入力してください。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(activity))
            {
                Error(app, "作業分類を付けるときは、プロジェクトも入力してください。");
                continue;
            }
            if (ConfirmNewActivity(app, activities, activity) is not { } confirmed) continue;
            activity = confirmed;
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
            return new TaskInput(taskTitle, project, activity, memo, due, priority);
        }
    }

    /// <summary>
    /// 作業分類が既存にない名前なら、表記ゆれを防ぐために確認する。使う名前を返す（戻るなら null）。
    /// 全角半角・大文字小文字・空白だけ違う既存の名前があれば、そちらを使うか聞く。
    /// </summary>
    private static string? ConfirmNewActivity(IApplication app, IReadOnlyList<string> activities, string? activity)
    {
        if (string.IsNullOrWhiteSpace(activity)) return "";
        var name = activity.Trim();
        if (activities.Contains(name, StringComparer.Ordinal)) return name;

        if (TaskService.FindSimilarName(activities, name) is { } similar)
            return MessageBox.Query(app, "新しい作業分類",
                    $"「{name}」は、既存の「{similar}」と表記だけ違います。", $"「{similar}」を使う", $"「{name}」を作る", "戻る") switch
                {
                    0 => similar,
                    1 => name,
                    _ => null,
                };
        return Confirm(app, "新しい作業分類", $"新しい作業分類「{name}」を作りますか？", "作る", "戻る") ? name : null;
    }

    /// <summary>
    /// 既存のプロジェクト（や作業分類）から1つ選ぶ。<paramref name="filter"/> を含むものだけ出す（該当なしなら全部）。
    /// キャンセルなら null。
    /// </summary>
    /// <summary>一覧から1つ選んでもらい、その番号を返す（キャンセルなら null）。</summary>
    public static int? Pick(IApplication app, string title, IReadOnlyList<string> items) =>
        Choose(app, title, items, null) is { } chosen ? items.ToList().IndexOf(chosen) : null;

    private static string? Choose(IApplication app, string title, IReadOnlyList<string> names, string? filter)
    {
        var candidates = string.IsNullOrWhiteSpace(filter)
            ? names.ToList()
            : names.Where(p => p.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) candidates = names.ToList();

        using var d = new Dialog
        {
            Title = title,
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
        "#   2026-11-06 10:00 - 12:30            （終了が開始より前の時刻なら翌日。12時間まで）\n" +
        "#   2026-11-06 23:00 - 2026-11-07 01:00\n" +
        "#   2026-11-06 13:00 -                  （計測中）\n";

    /// <summary>作業記録をテキストで直してもらう。保存できたら true（キャンセルなら false）。</summary>
    public static bool EditWorkLogs(IApplication app, string title, IEnumerable<WorkLog> logs, Action<List<WorkLog>> apply)
    {
        var text = WorkLogHelp + WorkLogText.Format(logs);
        while (true)
        {
            if (EditText(app, title, text) is not { } edited) return false;
            text = edited;
            try
            {
                apply(WorkLogText.Parse(text, dt => TimeZoneInfo.Local.GetUtcOffset(dt)));
                return true;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                Error(app, ex.Message);
            }
        }
    }

    // ---------- 複数行テキスト（作業記録など） ----------

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

    // ---------- ふり返り（課題・学び・所感） ----------

    /// <summary>ふり返りの3つの欄を書いてもらう。キャンセルなら null。</summary>
    public static DayNotes? EditNotes(IApplication app, string title, DayNotes notes)
    {
        using var d = new Dialog { Title = title, Width = Dim.Percent(85), Height = Dim.Percent(85) };
        var fields = AddNoteFields(d, 0, notes, Dim.Percent(10), Dim.Fill(1));
        d.Add(new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "Tab: 欄を切り替え  Ctrl+S: 保存  Esc: キャンセル" });
        fields.Problem.SetFocus();
        return Show(app, d, ["保存", Cancel]) == 0 ? fields.Value : null;
    }

    private sealed record NoteFields(TextView Problem, TextView Try, TextView Note)
    {
        public DayNotes Value => new(Problem.Text, Try.Text, Note.Text);
    }

    /// <summary>
    /// 課題・学び・所感の欄を <paramref name="top"/> から縦に並べる。
    /// 課題と学びの高さは <paramref name="height"/>、所感は <paramref name="lastHeight"/>（残りを埋める）。
    /// </summary>
    private static NoteFields AddNoteFields(View parent, Pos top, DayNotes notes, Dim height, Dim lastHeight)
    {
        TextView Field(string label, Pos y, string? text, Dim h)
        {
            var l = new Label { X = 0, Y = y, Text = label };
            var v = new TextView
            {
                X = 0,
                Y = Pos.Bottom(l),
                Width = Dim.Fill(),
                Height = h,
                Text = text ?? "",
                TabKeyAddsTab = false,
            };
            parent.Add(l, v);
            return v;
        }

        var problem = Field("課題・困ったこと（Problem）", top, notes.Problem, height);
        var @try = Field("学び・改善（Try）", Pos.Bottom(problem), notes.Try, height);
        var note = Field("所感", Pos.Bottom(@try), notes.Note, lastHeight);
        return new NoteFields(problem, @try, note);
    }

    // ---------- 日時の入力 ----------

    /// <summary>
    /// さかのぼって開始する時刻を聞く（<see cref="StartTimeText"/> の形）。<paramref name="apply"/> がエラーなら出し直す。
    /// </summary>
    public static void AskStartTime(IApplication app, string taskTitle, string initial, string? hint,
        Func<DateTimeOffset> now, Action<DateTimeOffset> apply)
    {
        var text = initial;
        while (true)
        {
            using var d = new Dialog { Title = "さかのぼって開始", Width = 64, Height = 12 };
            var field = new TextField { X = 1, Y = 2, Width = 24, Text = text };
            d.Add(
                new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = $"「{taskTitle}」をいつから始めた？" },
                new Label { X = 1, Y = 1, Text = "9:30 / -15（15分前）/ 2026-11-26 09:30" },
                field,
                new Label { X = 1, Y = 4, Width = Dim.Fill(1), Text = hint ?? "" });
            field.SetFocus();
            if (Show(app, d, ["開始", Cancel], field) != 0) return;

            text = field.Text;
            try
            {
                apply(StartTimeText.Parse(text, now()));
                return;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                Error(app, ex.Message);
            }
        }
    }

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

    // ---------- 設定（config.json） ----------

    /// <summary>
    /// 設定を画面で変える。<paramref name="current"/> は変えずに、直した写しを返す（キャンセルなら null）。
    /// 値がおかしければ知らせて、入れたままの状態で出し直す。
    /// </summary>
    public static AppConfig? EditConfig(IApplication app, AppConfig current, string configPath)
    {
        var dataText = current.DataDirectory;
        var reportText = current.ReportDirectory;
        var boundaryText = current.DayBoundary;
        var hoursText = current.ForgottenTimerWarningHours.ToString(CultureInfo.InvariantCulture);
        var staleText = current.StaleTaskDays.ToString(CultureInfo.InvariantCulture);
        while (true)
        {
            using var d = new Dialog { Title = "設定", Width = Dim.Percent(80), Height = 21 };
            const int x = 22;
            TextField Field(int y, string label, string text, Dim width, string hint)
            {
                var f = new TextField { X = x, Y = y, Width = width, Text = text };
                d.Add(new Label { X = 1, Y = y, Text = label }, f,
                    new Label { X = x, Y = y + 1, Width = Dim.Fill(1), Text = hint });
                return f;
            }

            var dataField = Field(0, "データのフォルダ", dataText, Dim.Fill(1),
                "tasks.json とバックアップ。次の起動から（今のデータは移しません）");
            var reportField = Field(3, "日報のフォルダ", reportText, Dim.Fill(1), "日報・週報・月報の出力先");
            var boundaryField = Field(6, "日の切り替わり", boundaryText, 6, "HH:mm（例: 04:00）。この時刻より前は前の業務日");
            var hoursField = Field(9, "止め忘れの警告", hoursText, 6, "時間（起動したとき、これより長く動いていたら確認する）");
            var staleField = Field(12, "自動で保留", staleText, 6, "業務日（これだけ触っていない未完了タスク。0: 使わない）");
            d.Add(
                new Label { X = 1, Y = 15, Width = Dim.Fill(1), Text = $"保存先: {configPath}　%APPDATA% などの環境変数も使える" },
                new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "Enter/Ctrl+S: 保存  Tab: 次の欄  Esc: キャンセル" });
            dataField.SetFocus();

            if (Show(app, d, ["保存", Cancel], dataField, reportField, boundaryField, hoursField, staleField) != 0)
                return null;

            dataText = dataField.Text.Trim();
            reportText = reportField.Text.Trim();
            boundaryText = boundaryField.Text.Trim();
            hoursText = hoursField.Text.Trim();
            staleText = staleField.Text.Trim();

            if (!double.TryParse(hoursText, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            {
                Error(app, "止め忘れの警告は時間を数で入力してください（例: 8、1.5）。");
                continue;
            }
            if (!int.TryParse(staleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stale))
            {
                Error(app, "自動で保留は日数を整数で入力してください（0 なら使わない）。");
                continue;
            }
            // 「4:00」も「04:00」として受け付ける
            if (TimeOnly.TryParseExact(boundaryText, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
                boundaryText = b.ToString("HH:mm", CultureInfo.InvariantCulture);

            var edited = current.Clone();
            edited.DataDirectory = dataText;
            edited.ReportDirectory = reportText;
            edited.DayBoundary = boundaryText;
            edited.ForgottenTimerWarningHours = hours;
            edited.StaleTaskDays = stale;
            try
            {
                edited.Validate();
                return edited;
            }
            catch (ConfigException ex)
            {
                Error(app, ex.Message);
            }
        }
    }

    // ---------- ヘルプ ----------

    /// <summary>キー操作の一覧を見せる。せまい端末でも読めるようにスクロールできる欄に出す。</summary>
    public static void Help(IApplication app, string text)
    {
        using var d = new Dialog { Title = "キー操作（?）", Width = Dim.Percent(80), Height = Dim.Percent(90) };
        var view = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Text = text,
            ReadOnly = true,
        };
        ScrollOnArrows(view);
        // 読むだけの欄なので Enter は改行ではなく閉じるにする
        view.KeyDown += (_, key) =>
        {
            if (key != Key.Enter) return;
            key.Handled = true;
            d.RequestStop();
        };
        d.Add(view, new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "↑↓: スクロール  Enter/Esc: 閉じる" });
        view.SetFocus();
        Show(app, d, ["閉じる"]);
    }

    /// <summary>
    /// 読むだけの欄で ↑↓ / PageUp・PageDown を押したら、すぐに表示をずらす。
    /// TextView の既定はカーソルを動かすだけで、カーソルが端に届くまで表示が動かず、スクロールしないように見えるため。
    /// </summary>
    public static void ScrollOnArrows(TextView view)
    {
        view.KeyDown += (_, key) =>
        {
            var page = Math.Max(1, view.Viewport.Height - 1);
            int? delta =
                key == Key.CursorDown ? 1
                : key == Key.CursorUp ? -1
                : key == Key.PageDown ? page
                : key == Key.PageUp ? -page
                : null;
            if (delta is not { } d) return;
            key.Handled = true;

            var top = Math.Clamp(view.Viewport.Y + d, 0, Math.Max(0, view.Lines - view.Viewport.Height));
            // カーソルを見えている先頭の行に置いてから動かす（描き直しでカーソルの位置へ戻されないように）
            view.InsertionPoint = new System.Drawing.Point(0, top);
            view.ScrollTo(new System.Drawing.Point(0, top));
            view.SetNeedsDraw();
        };
    }

    // ---------- 業務日の入力（過去のふり返り・日報） ----------

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

    // ---------- 日報の出力: 明日の予定とふり返り（F-R-02〜04, F-R-08） ----------

    /// <summary>明日の予定を選び、ふり返り（課題・学び・所感）を見直す。キャンセルなら null。</summary>
    public static ReportInput? PrepareReport(IApplication app, TaskService service, DateOnly day)
    {
        var tasks = service.OpenTasks().ToList();
        var worked = new List<TaskItem>();
        var workedRows = new ObservableCollection<string>();
        var record = service.GetDay(day);
        var chosen = new HashSet<Guid>(record?.TomorrowTaskIds ?? []);
        var rows = new ObservableCollection<string>();

        using var d = new Dialog
        {
            Title = $"日報を出力（{BusinessDayCalendar.Key(day)}）",
            Width = Dim.Percent(80),
            Height = Dim.Percent(90),
        };
        var workedLabel = new Label { X = 0, Y = 3 };
        var workedList = new ShortcutListView
        {
            X = 0,
            Y = Pos.Bottom(workedLabel),
            Width = Dim.Fill(),
            Height = Dim.Percent(20),
        };
        workedList.Shortcut = key =>
        {
            if (key != Key.T) return false;
            EditWorked();
            return true;
        };
        workedList.SetSource(workedRows);
        var tomorrowLabel = new Label { X = 0, Y = Pos.Bottom(workedList), Text = "明日の予定" };
        var list = new ShortcutListView
        {
            X = 0,
            Y = Pos.Bottom(tomorrowLabel),
            Width = Dim.Fill(),
            Height = Dim.Percent(25),
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
        d.Add(
            new Label { X = 0, Y = 0, Text = "t: 作業記録を修正（その日の分）  Space: 選ぶ/外す  a: タスクを追加" },
            new Label { X = 0, Y = 1, Text = "Tab: 作業/予定/課題/学び/所感を切り替え  Ctrl+S: 出力  Esc: キャンセル" },
            workedLabel, workedList, tomorrowLabel, list);
        var fields = AddNoteFields(d, Pos.Bottom(list), service.GetNotes(day), Dim.Percent(10), Dim.Fill());

        void Reload()
        {
            rows.Clear();
            foreach (var t in tasks) rows.Add(t.ProjectLabel is { } p ? $"{t.Title}  #{p}" : t.Title);
            for (var i = 0; i < tasks.Count; i++)
                list.Source!.SetMark(i, chosen.Contains(tasks[i].Id));
            list.SetNeedsDraw();
        }

        // その日の作業: 日報の完了・進行中と同じ順。作業記録のない完了タスクも、時間を足せるように出す
        void ReloadWorked()
        {
            var selected = workedList.Value;
            worked.Clear();
            worked.AddRange(service.CompletedOn(day).Concat(service.InProgressOn(day)));
            workedRows.Clear();
            foreach (var t in worked)
            {
                var title = t.ProjectLabel is { } p ? $"{t.Title}  #{p}" : t.Title;
                var done = t.CompletedAt is { } c && service.Calendar.DayOf(c) == day ? "完了 " : "";
                var slots = ReportGenerator.TimeSlots(service.LogsIn(t, day, day));
                workedRows.Add($"{done}{title} — {DurationFormat.Short(service.DurationOn(t, day))}{(slots.Length > 0 ? $"（{slots}）" : "")}");
            }
            workedLabel.Text = $"その日の作業（稼働合計 {DurationFormat.Short(service.TotalDurationOn(day))}）";
            if (worked.Count > 0) workedList.Value = Math.Clamp(selected ?? 0, 0, worked.Count - 1);
            workedList.SetNeedsDraw();
        }

        void EditWorked()
        {
            if (workedList.Value is not int i || i < 0 || i >= worked.Count) return;
            var t = worked[i];
            if (EditWorkLogs(app, $"作業記録: {t.Title}（{BusinessDayCalendar.Key(day)} の分）", service.WholeLogsOn(t, day),
                    logs => service.ReplaceWorkLogsOn(t.Id, day, logs)))
                ReloadWorked();
        }

        void AddFromDialog()
        {
            if (EditTask(app, "明日の予定にタスクを追加", null, service.Projects(), service.Activities(), service.Today) is not { } r) return;
            // 印を今の状態から引き継ぐ
            Collect();
            var t = service.Add(r.Title, r.Project, r.Memo, r.Due, r.Priority, r.Activity);
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

        ReloadWorked();
        Reload();
        list.SetFocus();
        if (Show(app, d, ["出力", Cancel]) != 0) return null;

        Collect();
        // 並びは一覧の順
        return new ReportInput(tasks.Where(t => chosen.Contains(t.Id)).Select(t => t.Id).ToList(), fields.Value);
    }
}
