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

    private readonly ShortcutListView _list;
    private readonly Label _detail;
    private readonly ObservableCollection<string> _rows = [];
    private readonly List<TaskItem?> _rowTasks = []; // 見出し行は null

    private DateOnly _shownDay;
    private object? _timer;

    public MainWindow(IApplication app, TaskService service, TaskStore store, AppConfig config)
    {
        _app = app;
        _service = service;
        _store = store;
        _config = config;

        Title = AppTitle;

        var listFrame = new FrameView
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
            Text = "a:追加  Enter:開始/停止  d:完了/取消  e:編集  t:時間修正  x:削除\n" +
                   "n:所感  r:日報出力  m:月報出力  q:終了",
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

        AddHeader("[未完了]");
        foreach (var t in _service.OpenTasks())
            AddTask(t, t.IsRunning ? "▶" : t.WorkLogs.Count > 0 ? "・" : " ");

        AddHeader("[今日の完了]");
        foreach (var t in _service.CompletedOn(_shownDay))
            AddTask(t, "✓");

        // 選択を戻す。なければ最初のタスク行
        var idx = keep is { } id ? _rowTasks.FindIndex(t => t?.Id == id) : -1;
        if (idx < 0) idx = _rowTasks.FindIndex(t => t is not null);
        if (idx < 0) idx = 0;
        _list.Value = idx;

        UpdateTitle();
        UpdateDetail();
    }

    private void AddHeader(string text)
    {
        _rows.Add(text);
        _rowTasks.Add(null);
    }

    private void AddTask(TaskItem t, string mark)
    {
        var project = t.Project is { } p ? $"  #{p}" : "";
        _rows.Add($" {mark} {t.Title}{project}");
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
            _ => t.WorkLogs.Count > 0 ? "未完了" : "未着手",
        };
        _detail.Text =
            $"タイトル: {t.Title}\n" +
            $"プロジェクト: {t.Project ?? "-"}\n" +
            $"状態: {state}\n" +
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

        Action? action =
            key == Key.A ? AddTask
            : key == Key.Enter ? ToggleTimer
            : key == Key.D ? ToggleComplete
            : key == Key.E ? EditTask
            : key == Key.T ? EditWorkLogs
            : key == Key.X ? DeleteTask
            : key == Key.N ? EditNote
            : key == Key.R ? OutputReport
            : key == Key.M ? OutputMonthlyReport
            : key == Key.Q ? () => _app.RequestStop()
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
        if (Dialogs.EditTask(_app, "タスクを追加", null, null, null, _service.Projects()) is not { } r) return;
        var t = _service.Add(r.Title, r.Project, r.Memo);
        Refresh();
        _list.Value = _rowTasks.IndexOf(t);
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
        if (Dialogs.EditTask(_app, "タスクを編集", t.Title, t.Project, t.Memo, _service.Projects()) is { } r)
            _service.Edit(t.Id, r.Title, r.Project, r.Memo);
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

    private void EditNote()
    {
        var day = _service.Today;
        if (Dialogs.EditText(_app, $"所感 {BusinessDayCalendar.Key(day)}", _service.GetDay(day)?.Note) is { } note)
            _service.SetNote(day, note);
    }

    private void OutputReport()
    {
        var day = _service.Today;
        var tomorrow = Dialogs.ChooseTomorrow(_app, _service, day);
        if (tomorrow is null) return;
        _service.SetTomorrow(day, tomorrow);

        var markdown = ReportGenerator.Generate(_service, day);
        try
        {
            var path = ReportWriter.Write(_config.ReportDirectoryPath, day, markdown);
            Dialogs.Info(_app, "日報を出力しました", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error(_app, $"日報を保存できませんでした: {ex.Message}");
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
