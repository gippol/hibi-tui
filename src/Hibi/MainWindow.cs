using System.Collections.ObjectModel;
using Hibi.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Hibi;

/// <summary>メイン画面（要件 5.1）。</summary>
internal sealed class MainWindow : Window
{
    private const string AppTitle = "hibi";

    private readonly IApplication _app;
    private readonly TaskService _service;
    private readonly TaskStore _store;
    private readonly AppConfig _config;

    private readonly FrameView _listFrame;
    private readonly ShortcutListView _list;
    private readonly Label _detail;
    private readonly ObservableCollection<string> _rows = [];
    private readonly List<TaskItem?> _rowTasks = []; // 見出し行は null

    private DateOnly _shownDay;
    private object? _timer;

    /// <summary>一覧の絞り込み（<see cref="TaskFilter"/>）。null なら全部出す。</summary>
    private string? _filter;

    /// <summary>今 [今日の予定] に出しているタスク。並べ替えを区切りの中に閉じるのに使う。</summary>
    private HashSet<Guid> _plannedIds = [];

    /// <summary>[保留] を開いて出すか。</summary>
    private bool _showStale;

    public MainWindow(IApplication app, TaskService service, TaskStore store, AppConfig config)
    {
        _app = app;
        _service = service;
        _store = store;
        _config = config;

        Title = AppTitle;

        var listFrame = _listFrame = new FrameView
        {
            Title = "タスク",
            X = 0,
            Y = 0,
            Width = Dim.Percent(55),
            Height = Dim.Fill(2),
        };
        _list = new ShortcutListView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Shortcut = HandleKey,
        };
        _list.SetSource(_rows);
        _list.ValueChanged += (_, _) => UpdateDetail();
        listFrame.Add(_list);

        var detailFrame = new FrameView
        {
            Title = "詳細",
            X = Pos.Right(listFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _detail = new Label { Width = Dim.Fill(), Height = Dim.Fill() };
        detailFrame.Add(_detail);

        var footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 2,
            Text = "a:追加  Enter:開始/停止  d:完了/取消  e:編集  t:時間修正  x:削除  /:絞り込み  Shift+↑↓:並べ替え\n" +
                   "n:所感  r:日報出力  N/R:過去の日を選んで  w:週報出力  m:月報出力  p:保留にする/戻す  h:保留を開く/たたむ  q:終了",
        };

        Add(listFrame, detailFrame, footer);

        _service.Changed += Save;
        Refresh();

        // 毎秒: ヘッダーと詳細の時間を更新し、業務日の切り替わりを見る（F-D-03）
        _timer = _app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            if (_service.Today != _shownDay) Refresh();
            else
            {
                UpdateTitle();
                UpdateDetail();
            }
            return true;
        });

        // 起動直後に止め忘れを確認する（F-W-07）
        _app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            CheckForgottenTimer();
            return false;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.Changed -= Save;
            if (_timer is not null) _app.RemoveTimeout(_timer);
            _timer = null;
        }
        base.Dispose(disposing);
    }

    // ---------- 表示 ----------

    private TaskItem? Selected =>
        _list.Value is int i && i >= 0 && i < _rowTasks.Count ? _rowTasks[i] : null;

    private void Refresh()
    {
        var keep = Selected?.Id;
        _shownDay = _service.Today;

        _rows.Clear();
        _rowTasks.Clear();

        // 前の業務日の日報で選んだ「明日の予定」を上に出す。下の未完了には重ねて出さない
        var planned = _service.PlannedToday();
        _plannedIds = planned.Select(t => t.Id).ToHashSet();
        var shownPlanned = planned.Where(IsShown).ToList();
        if (shownPlanned.Count > 0)
        {
            AddHeader("[今日の予定]");
            foreach (var t in shownPlanned) AddTask(t, OpenMark(t));
        }

        // 手で保留にしたもの・長く触っていないものは [保留] に分ける。ふだんはたたんでおき、件数だけ出す
        var open = _service.OpenTasks().Where(t => !_plannedIds.Contains(t.Id) && IsShown(t)).ToList();
        var held = open.Where(IsHeld).ToList();

        AddHeader("[未完了]");
        foreach (var t in open.Where(t => !IsHeld(t)))
            AddTask(t, OpenMark(t));

        if (held.Count > 0)
        {
            // 絞り込み中は探しているものが隠れないように開いて出す
            var expanded = _showStale || _filter is not null;
            var why = _config.StaleTaskDays > 0 ? $"手で保留・{_config.StaleTaskDays}日以上触っていない" : "手で保留";
            AddHeader($"[保留 {held.Count}件（{why}）]" +
                      (_filter is not null ? "" : expanded ? "  h: たたむ" : "  h: 開く"));
            if (expanded)
                foreach (var t in held) AddTask(t, OpenMark(t));
        }

        AddHeader("[今日の完了]");
        foreach (var t in _service.CompletedOn(_shownDay).Where(IsShown))
            AddTask(t, "✓");

        _listFrame.Title = _filter is null ? "タスク" : $"タスク（絞り込み: {_filter}　Esc: 解除）";

        // 選択を戻す。なければ最初のタスク行
        var idx = keep is { } id ? _rowTasks.FindIndex(t => t?.Id == id) : -1;
        if (idx < 0) idx = _rowTasks.FindIndex(t => t is not null);
        if (idx < 0) idx = 0;
        _list.Value = idx;

        UpdateTitle();
        UpdateDetail();
    }

    private bool IsShown(TaskItem t) => TaskFilter.Matches(t, _filter);

    private bool IsHeld(TaskItem t) => _service.IsOnHold(t, _config.StaleTaskDays);

    private static string OpenMark(TaskItem t) => t.IsRunning ? "▶" : t.WorkLogs.Count > 0 ? "・" : " ";

    private void AddHeader(string text)
    {
        _rows.Add(text);
        _rowTasks.Add(null);
    }

    /// <summary>1行: 印、優先度（! 高 / ↓ 低）、タイトル、#プロジェクト、〆期限。</summary>
    private void AddTask(TaskItem t, string mark)
    {
        var priority = t.Priority switch { TaskPriority.High => "!", TaskPriority.Low => "↓", _ => "" };
        var project = t.Project is { } p ? $"  #{p}" : "";
        var due = t.IsCompleted || t.Due is not { } d ? ""
            : _service.IsOverdue(t) ? $"  〆{d:MM-dd} 期限切れ"
            : d == _shownDay ? "  〆今日"
            : $"  〆{d:MM-dd}";
        _rows.Add($" {mark} {priority}{t.Title}{project}{due}");
        _rowTasks.Add(t);
    }

    private void UpdateTitle()
    {
        Title = _service.Running is { } r
            ? $"{AppTitle}  ▶ 作業中: {r.Task.Title} {DurationFormat.Clock(r.Log.DurationAt(_service.Now))}"
            : $"{AppTitle}  {BusinessDayCalendar.Key(_shownDay)}（{BusinessDayCalendar.JaWeekday(_shownDay)}）";
    }

    private void UpdateDetail()
    {
        if (Selected is not { } t)
        {
            _detail.Text = "";
            return;
        }
        var state = t.State switch
        {
            TaskState.Running => "作業中",
            TaskState.Completed => $"完了（{t.CompletedAt:MM-dd HH:mm}）",
            _ when t.OnHold => "保留（手で保留）",
            _ when IsHeld(t) => "保留（長く触っていない）",
            _ => t.WorkLogs.Count > 0 ? "未完了" : "未着手",
        };
        var last = _service.LastActivityDay(t);
        var ago = _shownDay.DayNumber - last.DayNumber;
        var due = t.Due is { } d
            ? $"{BusinessDayCalendar.Key(d)}（{BusinessDayCalendar.JaWeekday(d)}）{(_service.IsOverdue(t) ? " 期限切れ" : "")}"
            : "-";
        _detail.Text =
            $"タイトル: {t.Title}\n" +
            $"プロジェクト: {t.Project ?? "-"}\n" +
            $"状態: {state}\n" +
            $"優先度: {Dialogs.PriorityLabel(t.Priority)}  期限: {due}\n" +
            $"最後に触った日: {BusinessDayCalendar.Key(last)}（{(ago <= 0 ? "今日" : $"{ago}日前")}）\n" +
            $"今日: {DurationFormat.Short(_service.DurationOn(t, _shownDay))} / 累計: {DurationFormat.Short(_service.TotalDuration(t))}\n" +
            $"\nメモ:\n{t.Memo ?? "-"}";
    }

    // ---------- キー操作（要件 5.2） ----------

    private bool HandleKey(Key key)
    {
        // 見出し行は飛ばして移動する
        if (key == Key.CursorUp || key == Key.CursorDown)
        {
            var step = key == Key.CursorUp ? -1 : 1;
            for (var i = (_list.Value ?? 0) + step; i >= 0 && i < _rowTasks.Count; i += step)
                if (_rowTasks[i] is not null)
                {
                    _list.Value = i;
                    break;
                }
            return true;
        }

        if (key == Key.CursorUp.WithShift || key == Key.CursorDown.WithShift)
        {
            Run(() => MoveSelected(key == Key.CursorUp.WithShift ? -1 : 1));
            return true;
        }

        // 絞り込み中の Esc は解除。絞り込んでいなければ今までどおり（アプリの既定の動き）に任せる
        if (key == Key.Esc && _filter is not null)
        {
            _filter = null;
            Refresh();
            return true;
        }

        Action? action =
            key.AsRune.Value == '/' ? AskFilter
            :
            key == Key.A ? AddTask
            : key == Key.Enter ? ToggleTimer
            : key == Key.D ? ToggleComplete
            : key == Key.E ? EditTask
            : key == Key.T ? EditWorkLogs
            : key == Key.X ? DeleteTask
            : key == Key.N ? () => EditNote(_service.Today)
            : key == Key.N.WithShift ? () => WithPastDay("所感を書く", EditNote)
            : key == Key.R ? () => OutputReport(_service.Today)
            : key == Key.R.WithShift ? () => WithPastDay("日報を出力", OutputReport)
            : key == Key.M ? OutputMonthlyReport
            : key == Key.W ? OutputWeeklyReport
            : key == Key.H ? () => _showStale = !_showStale
            : key == Key.P ? ToggleHold
            : key == Key.Q ? ConfirmQuit
            : null;
        if (action is null) return false;

        Run(action);
        return true;
    }

    /// <summary>操作を実行し、入力エラーはメッセージで知らせる。</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            Dialogs.Error(_app, ex.Message);
        }
        Refresh();
    }

    private void AddTask()
    {
        if (Dialogs.EditTask(_app, "タスクを追加", null, _service.Projects()) is not { } r) return;
        var t = _service.Add(r.Title, r.Project, r.Memo, r.Due, r.Priority);
        Refresh();
        if (_rowTasks.IndexOf(t) is var i and >= 0) _list.Value = i; // 絞り込みで見えないときは選択を動かさない
    }

    private void ConfirmQuit()
    {
        var running = _service.Running is { } r ? $"\n「{r.Task.Title}」のタイマーは動いたままです。" : "";
        if (Dialogs.Confirm(_app, "終了", $"hibi を終了しますか？{running}", "終了"))
            _app.RequestStop();
    }

    /// <summary>
    /// メイン画面では Esc で終了させない（Terminal.Gui の既定は Esc = 終了）。
    /// ダイアログを閉じた直後の Esc（日本語入力の取り消しなど）で、うっかり終わってしまうため。
    /// 絞り込みの解除は一覧のキー操作（<see cref="HandleKey"/>）で先に処理している。
    /// </summary>
    protected override bool OnKeyDown(Key key) => key == Key.Esc || base.OnKeyDown(key);

    private void AskFilter()
    {
        var q = Dialogs.AskLine(_app, "絞り込み", "タイトル・プロジェクトに含む文字", _filter,
            "スペース区切り: すべてを含む  #名前: プロジェクトだけ  空: 解除");
        if (q is null) return;
        _filter = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
    }

    /// <summary>
    /// 選んでいるタスクを、同じ区切り（[未完了] か [保留]）の中で見えている隣と入れ替える。
    /// [今日の予定] の並びは日報で選んだ順なので、ここでは動かさない。
    /// </summary>
    private void MoveSelected(int delta)
    {
        if (Selected is not { } t || t.IsCompleted || _plannedIds.Contains(t.Id)) return;
        var held = IsHeld(t);
        _service.MoveOpen(t.Id, delta, o => !_plannedIds.Contains(o.Id) && IsShown(o) && IsHeld(o) == held);
    }

    /// <summary>保留（手で・自動どちらも）なら戻し、そうでなければ手で保留にする。</summary>
    private void ToggleHold()
    {
        if (Selected is not { } t || t.IsCompleted) return;
        if (t.IsRunning)
            throw new InvalidOperationException("計測中のタスクは保留にできません。先にタイマーを止めてください。");
        _service.SetOnHold(t.Id, !IsHeld(t));
    }

    private void ToggleTimer()
    {
        if (Selected is { } t) _service.Toggle(t.Id);
    }

    private void ToggleComplete()
    {
        if (Selected is not { } t) return;
        if (!t.IsCompleted)
        {
            _service.Complete(t.Id);
            return;
        }
        if (Dialogs.Confirm(_app, "完了の取り消し", $"「{t.Title}」を未完了に戻しますか？", "戻す"))
            _service.Uncomplete(t.Id);
    }

    private void EditTask()
    {
        if (Selected is not { } t) return;
        if (Dialogs.EditTask(_app, "タスクを編集", new TaskInput(t.Title, t.Project, t.Memo, t.Due, t.Priority), _service.Projects()) is { } r)
            _service.Edit(t.Id, r.Title, r.Project, r.Memo, r.Due, r.Priority);
    }

    private void EditWorkLogs()
    {
        if (Selected is not { } t) return;
        Dialogs.EditWorkLogs(_app, t, logs => _service.ReplaceWorkLogs(t.Id, logs));
    }

    private void DeleteTask()
    {
        if (Selected is not { } t) return;
        if (Dialogs.Confirm(_app, "削除", $"「{t.Title}」を削除しますか？\n作業記録も消えます。", "削除"))
            _service.Delete(t.Id);
    }

    /// <summary>業務日を選んでから実行する。最初は前の業務日が入っている。</summary>
    private void WithPastDay(string title, Action<DateOnly> action)
    {
        var today = _service.Today;
        if (Dialogs.AskDay(_app, title, today.AddDays(-1), today) is { } day) action(day);
    }

    private void EditNote(DateOnly day)
    {
        if (Dialogs.EditText(_app, $"所感 {BusinessDayCalendar.Key(day)}", _service.GetDay(day)?.Note) is { } note)
            _service.SetNote(day, note);
    }

    private void OutputReport(DateOnly day)
    {
        var tomorrow = Dialogs.ChooseTomorrow(_app, _service, day);
        if (tomorrow is null) return;
        var previous = _service.GetDay(day)?.TomorrowTaskIds?.ToList();
        _service.SetTomorrow(day, tomorrow);

        var markdown = ReportGenerator.Generate(_service, day);
        // ファイルに書けなくても、貼り付けには使えるように先にコピーしておく。
        var copied = CopyToClipboard(markdown);
        var copyNote = copied ? "クリップボードにもコピーしました" : "クリップボードにはコピーできませんでした";
        try
        {
            var path = ReportWriter.Write(_config.ReportDirectoryPath, day, markdown);
            Dialogs.Info(_app, "日報を出力しました", $"{path}\n{copyNote}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ファイルにもクリップボードにも出せなかったなら日報は出ていないので、選んだ予定も元に戻す。
            if (!copied) _service.SetTomorrow(day, previous);
            Dialogs.Error(_app, $"日報を保存できませんでした: {ex.Message}\n{copyNote}");
        }
    }

    private bool CopyToClipboard(string text)
    {
        try
        {
            return _app.Clipboard is { IsSupported: true } clipboard && clipboard.TrySetClipboardData(text);
        }
        catch (Exception)
        {
            // クリップボードは他のアプリが掴んでいることがある。日報の出力は止めない。
            return false;
        }
    }

    private void OutputMonthlyReport()
    {
        if (Dialogs.AskMonth(_app, "月報を出力", _service.Today) is not { } month) return;

        var markdown = ReportGenerator.GenerateMonthly(_service, month);
        try
        {
            var path = ReportWriter.WriteMonthly(_config.ReportDirectoryPath, month, markdown);
            Dialogs.Info(_app, "月報を出力しました", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(_app, $"月報を保存できませんでした: {ex.Message}");
        }
    }

    private void OutputWeeklyReport()
    {
        var today = _service.Today;
        if (Dialogs.AskDay(_app, "週報を出力", today, today, "その週の中の業務日（yyyy-MM-dd）。週は月曜〜日曜") is not { } day)
            return;

        var week = ReportPeriod.WeekOf(day);
        var markdown = ReportGenerator.GenerateWeekly(_service, day);
        try
        {
            var path = ReportWriter.WriteWeekly(_config.ReportDirectoryPath, week, markdown);
            Dialogs.Info(_app, "週報を出力しました",
                $"{BusinessDayCalendar.Key(week.From)} 〜 {BusinessDayCalendar.Key(week.To)}\n{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(_app, $"週報を保存できませんでした: {ex.Message}");
        }
    }

    // ---------- 止め忘れ（F-W-07） ----------

    private void CheckForgottenTimer()
    {
        if (_service.Running is not { } r) return;
        var elapsed = r.Log.DurationAt(_service.Now);
        if (elapsed < _config.ForgottenTimerWarning) return;

        var stop = Dialogs.Confirm(_app, "止め忘れていませんか？",
            $"「{r.Task.Title}」のタイマーが {DurationFormat.Short(elapsed)} 動いています。\n" +
            $"開始: {r.Log.Start:yyyy-MM-dd HH:mm}",
            "終了時刻を入れて止める", "続ける");
        if (!stop) return;

        Dialogs.AskDateTime(_app, "終了時刻", "作業を終えた時刻（yyyy-MM-dd HH:mm）", r.Log.Start.AddHours(1),
            end => _service.StopAt(end));
        Refresh();
    }

    // ---------- 保存（N-R-01） ----------

    private void Save()
    {
        try
        {
            _store.Save(_service.Data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(_app, $"データを保存できませんでした: {ex.Message}");
        }
    }
}

/// <summary>キー操作を先に受け取れる ListView（文字キーでの項目ジャンプより優先する）。</summary>
internal sealed class ShortcutListView : ListView
{
    public Func<Key, bool>? Shortcut { get; set; }

    protected override bool OnKeyDown(Key key) => Shortcut?.Invoke(key) == true || base.OnKeyDown(key);
}
