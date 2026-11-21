using System.Collections.ObjectModel;
using Hibi.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView は v2.5 で非推奨（別パッケージの EditorView が後継）だが、読むだけの欄には標準の TextView で足りる
#pragma warning disable CS0618

namespace Hibi;

/// <summary>メイン画面（要件 5.1）。</summary>
internal sealed class MainWindow : Window
{
    private const string AppTitle = "hibi";

    /// <summary>今日の作業がこれを超えたら、タイトル行にこっそり「おつかれさま」と出す（隠し機能。ヘルプには書かない）。</summary>
    private static readonly TimeSpan WellDoneAfter = TimeSpan.FromHours(8);

    private const string FooterText = "?:ヘルプ  a:追加  Enter:開始/停止  d:完了  /:絞込  r:日報  ^O:日報を見る  q:終了";

    private readonly IApplication _app;
    private readonly TaskService _service;
    private readonly TaskStore _store;
    private readonly AppConfig _config;

    private readonly FrameView _listFrame;
    private readonly FrameView _detailFrame;
    private readonly TextView _preview;
    private readonly ShortcutListView _list;
    private readonly Label _detail;
    private readonly Label _footer;
    private readonly Label _filterPrompt;
    private readonly TextField _filterField;
    private readonly ObservableCollection<string> _rows = [];
    private readonly List<Span[]> _rowSpans = []; // _rows と同じ行を部分ごとの色で
    private readonly List<TaskItem?> _rowTasks = []; // 見出し行は null

    private DateOnly _shownDay;
    private object? _timer;

    /// <summary>一覧の絞り込み（<see cref="TaskFilter"/>）。null なら全部出す。</summary>
    private string? _filter;

    /// <summary>今 [今日の予定] に出しているタスク。並べ替えを区切りの中に閉じるのに使う。</summary>
    private HashSet<Guid> _plannedIds = [];

    /// <summary>[保留] を開いて出すか。</summary>
    private bool _showStale;

    /// <summary>詳細欄を出すか（i で切り替え）。隠すと一覧が横いっぱいに広がる。起動のたびに出す側に戻る。</summary>
    private bool _showDetail = true;

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
            Height = Dim.Fill(1),
        };
        _list = new ShortcutListView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Shortcut = HandleKey,
        };
        _list.Source = new TaskListSource(_rows, _rowSpans);
        _list.ValueChanged += (_, _) => UpdateDetail();
        listFrame.Add(_list);

        var detailFrame = _detailFrame = new FrameView
        {
            Title = "詳細",
            X = Pos.Right(listFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            // 詳細は見るだけ。FrameView は既定でフォーカスを取り（TabGroup）、クリックで移ると矢印キーで一覧に戻れないので取らせない
            CanFocus = false,
        };
        _detail = new Label { Width = Dim.Fill(), Height = Dim.Fill() };
        detailFrame.Add(_detail);

        // よく使うものだけ。全部は ? で出す（幅のせまい端末でも切れにくいように1行に収める）
        _footer = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = FooterText,
        };

        // 絞り込みの入力欄。/ で出してフッターと入れ替える。打つたびに一覧を絞る
        _filterPrompt = new Label { X = 1, Y = Pos.AnchorEnd(1), Text = "絞り込み（#名前: プロジェクト  Enter: 決定  Esc: 解除）:", Visible = false };
        _filterField = new TextField
        {
            X = Pos.Right(_filterPrompt) + 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Visible = false,
        };
        _filterField.TextChanged += (_, _) =>
        {
            if (!_filterField.Visible) return;
            _filter = string.IsNullOrWhiteSpace(_filterField.Text) ? null : _filterField.Text.Trim();
            Refresh();
        };
        _filterField.KeyDown += (_, key) =>
        {
            if (key == Key.Esc) CloseFilter(clear: true);
            else if (key == Key.Enter) CloseFilter(clear: false);
            else if (key == Key.CursorUp || key == Key.CursorDown)
            {
                CloseFilter(clear: false);
                HandleKey(key); // そのまま一覧の選択を動かす
            }
            else return;
            key.Handled = true;
        };
        // マウスで一覧をクリックしたときなど、ほかへ移ったら絞り込みを残して閉じる
        _filterField.HasFocusChanged += (_, e) =>
        {
            if (!e.NewValue && _filterField.Visible) CloseFilter(clear: false);
        };

        // Ctrl+O で一覧と詳細を隠して、今日の日報を画面いっぱいに出す（Norton Commander の Ctrl+O 風）
        _preview = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            ReadOnly = true,
            WordWrap = true,
            Visible = false,
        };
        _preview.KeyDown += (_, key) =>
        {
            if (key != Key.O.WithCtrl && key != Key.Esc && key != Key.Q) return;
            key.Handled = true;
            ClosePreview();
        };

        Add(listFrame, detailFrame, _preview, _footer, _filterPrompt, _filterField);

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
        _rowSpans.Clear();
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

    private void AddHeader(string text) => AddRow(null, new Span(text, Tone.Header));

    /// <summary>
    /// 1行: 印、優先度（! 高 / ↓ 低）、タイトル、#プロジェクト、〆期限。
    /// 色: 作業中は緑、! は黄、#プロジェクトと期限は灰色（今日は黄、期限切れは赤）、完了した行は全部暗い灰色。
    /// </summary>
    private void AddTask(TaskItem t, string mark)
    {
        var done = t.IsCompleted;
        Tone Or(Tone tone) => done ? Tone.Done : tone;

        var main = Or(t.IsRunning ? Tone.Running : Tone.Normal);
        var priority = t.Priority switch
        {
            TaskPriority.High => new Span("!", Or(Tone.High)),
            TaskPriority.Low => new Span("↓", Or(Tone.Muted)),
            _ => new Span(""),
        };
        var project = new Span(t.Project is { } p ? $"  #{p}" : "", Or(Tone.Muted));
        var due = done || t.Due is not { } d ? new Span("")
            : _service.IsOverdue(t) ? new Span($"  〆{d:MM-dd} 期限切れ", Tone.Overdue)
            : d == _shownDay ? new Span("  〆今日", Tone.DueToday)
            : new Span($"  〆{d:MM-dd}", Tone.Muted);

        AddRow(t, new Span($" {mark} ", main), priority, new Span(t.Title, main), project, due);
    }

    private void AddRow(TaskItem? task, params Span[] spans)
    {
        // 色の情報を先に入れる: _rows を変えると一覧が描き直されるので、そのときに揃っているように
        _rowSpans.Add(spans);
        _rowTasks.Add(task);
        _rows.Add(string.Concat(spans.Select(s => s.Text)));
    }

    /// <summary>
    /// 作業中はタスクのその日の合計（今までに止めた分も入れる）と、今回の計測を出す。
    /// 今回の分だけだと、止めて開き直すたびに 0 から数え直しに見えるため。
    /// 日の切り替わりをまたいで動かしていたら、合計は今の業務日に入る分だけ（今回の分は始めてからずっと）。
    /// </summary>
    private void UpdateTitle()
    {
        var title = _service.Running is { } r
            ? $"{AppTitle}  ▶ 作業中: {r.Task.Title} " +
              $"{DurationFormat.Clock(_service.DurationOn(r.Task, _service.Today))}" +
              $"（今回 {DurationFormat.Clock(r.Log.DurationAt(_service.Now))}）"
            : $"{AppTitle}  {BusinessDayCalendar.Key(_shownDay)}（{BusinessDayCalendar.JaWeekday(_shownDay)}）";
        if (_service.TotalDurationOn(_shownDay) > WellDoneAfter) title += "  おつかれさま";
        Title = title;
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
            key.AsRune.Value == '/' ? OpenFilter
            : key.AsRune.Value == '?' ? () => Dialogs.Help(_app, HelpText)
            : key == Key.O.WithCtrl ? OpenPreview
            : key.AsRune.Value == '+' ? () => ChangePriority(1)
            : key.AsRune.Value == '-' ? () => ChangePriority(-1)
            : key == Key.A ? AddTask
            : key == Key.Enter ? ToggleTimer
            : key == Key.D ? ToggleComplete
            : key == Key.E ? EditTask
            : key == Key.T ? EditWorkLogs
            : key == Key.X ? DeleteTask
            : key == Key.N ? () => EditNote(_service.Today)
            : key == Key.N.WithShift ? () => WithPastDay("ふり返りを書く", EditNote)
            : key == Key.R ? () => OutputReport(_service.Today)
            : key == Key.R.WithShift ? () => WithPastDay("日報を出力", OutputReport)
            : key == Key.M ? OutputMonthlyReport
            : key == Key.W ? OutputWeeklyReport
            : key == Key.H ? () => _showStale = !_showStale
            : key == Key.I ? ToggleDetail
            : key == Key.P ? ToggleHold
            : key == Key.S ? GroupByProject
            : key == Key.Q ? ConfirmQuit
            : null;
        if (action is null) return false;

        Run(action);
        return true;
    }

    /// <summary>? で出すキー操作の一覧。</summary>
    private const string HelpText =
        """
        [タスク]
          ↑ / ↓          タスクを選ぶ
          a              追加
          e              編集（タイトル・プロジェクト・期限・優先度・メモ）
          Enter          タイマーを開始 / 停止
          d              完了にする / 完了を取り消す
          t              作業記録を修正
          x              削除
          p              保留にする / 保留から戻す
          + / -          優先度を1つ上げる / 下げる（低 ↔ 普通 ↔ 高）

        [一覧]
          /              絞り込み（スペース区切り: すべてを含む  #名前: プロジェクト）
          Esc            絞り込みを解除
          Shift+↑ / ↓    区切りの中で並べ替える
          s              区切りごとにプロジェクトでまとめる
          h              [保留] を開く / たたむ
          i              詳細欄を隠す / 出す（隠すと一覧が横いっぱいになる）

        [ふり返り・日報]
          n              今日のふり返りを書く
          r              明日の予定を選んで日報を出力（クリップボードにもコピー）
          Shift+N / R    過去の業務日を選んで、ふり返り / 日報
          w              週報を出力
          m              月報を出力

        [その他]
          Ctrl+O         今日の日報を画面いっぱいに出す（もう一度 Ctrl+O か Esc で戻る）
          ?              このヘルプ
          q              終了

        [ダイアログの中]
          Enter          1行の欄で OK
          Ctrl+S         保存（複数行の欄でも）
          Esc            キャンセル
        """;

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
        if (Dialogs.EditTask(_app, "タスクを追加", null, _service.Projects(), _service.Today) is not { } r) return;
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

    /// <summary>フッターを絞り込みの入力欄に替える。今の絞り込みの続きから打てる。</summary>
    private void OpenFilter()
    {
        _footer.Visible = false;
        _filterPrompt.Visible = true;
        _filterField.Visible = true;
        _filterField.Text = _filter ?? "";
        _filterField.SetFocus();
        _filterField.MoveEnd();
    }

    /// <summary>
    /// 一覧と詳細を隠して、今日の日報をそのまま見せる。開いたときの内容で、保存も出力もしない
    /// （明日の予定とふり返りは、今保存されているものが入る）。
    /// </summary>
    private void OpenPreview()
    {
        _preview.Text = ReportGenerator.Generate(_service, _service.Today);
        _listFrame.Visible = false;
        _detailFrame.Visible = false;
        _preview.Visible = true;
        _footer.Text = "日報のプレビュー（まだ出力していません）  ↑↓: スクロール  Ctrl+O/Esc: 一覧に戻る";
        _preview.SetFocus();
    }

    /// <summary>詳細欄を隠す / 出す。せまい画面で一覧を広く使うため。</summary>
    private void ToggleDetail()
    {
        _showDetail = !_showDetail;
        ApplyDetailVisibility();
    }

    private void ApplyDetailVisibility()
    {
        _detailFrame.Visible = _showDetail;
        _listFrame.Width = _showDetail ? Dim.Percent(55) : Dim.Fill();
    }

    private void ClosePreview()
    {
        _preview.Visible = false;
        _listFrame.Visible = true;
        ApplyDetailVisibility();
        _footer.Text = FooterText;
        _list.SetFocus();
        Refresh();
    }

    /// <summary>入力欄を閉じて一覧に戻る。<paramref name="clear"/> なら絞り込みも解除する。</summary>
    private void CloseFilter(bool clear)
    {
        // 先に隠す: Text を消したときの TextChanged と、フォーカスが外れたときの呼び直しを止める
        _filterField.Visible = false;
        _filterPrompt.Visible = false;
        _footer.Visible = true;
        if (clear)
        {
            _filterField.Text = "";
            _filter = null;
        }
        _list.SetFocus();
        Refresh();
    }

    /// <summary>
    /// 選んでいるタスクを、同じ区切り（[今日の予定]・[未完了]・[保留]）の中で見えている隣と入れ替える。
    /// [今日の予定] は前の業務日の日報で選んだ予定の順を変える。
    /// </summary>
    private void MoveSelected(int delta)
    {
        if (Selected is not { } t || t.IsCompleted) return;
        if (_plannedIds.Contains(t.Id))
        {
            _service.MovePlanned(t.Id, delta, IsShown);
            return;
        }
        var held = IsHeld(t);
        _service.MoveOpen(t.Id, delta, o => !_plannedIds.Contains(o.Id) && IsShown(o) && IsHeld(o) == held);
    }

    /// <summary>
    /// 今見えている未完了タスクを、区切りごとにプロジェクトでまとめる。[今日の予定] は日報で選んだ予定の順を変える。
    /// たたんでいる [保留] は見えていないので動かさない。
    /// </summary>
    private void GroupByProject()
    {
        _service.GroupPlannedByProject(IsShown);
        var heldShown = _showStale || _filter is not null;
        _service.GroupOpenByProject(t => !_plannedIds.Contains(t.Id) && IsShown(t) && (heldShown || !IsHeld(t)));
    }

    /// <summary>選んでいる未完了タスクの優先度を1段上げ下げする。端なら何もしない。</summary>
    private void ChangePriority(int delta)
    {
        if (Selected is { IsCompleted: false } t) _service.ChangePriority(t.Id, delta);
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
        if (Dialogs.EditNotes(_app, $"ふり返り {BusinessDayCalendar.Key(day)}", _service.GetNotes(day)) is { } notes)
            _service.SetNotes(day, notes);
    }

    private void OutputReport(DateOnly day)
    {
        if (Dialogs.PrepareReport(_app, _service, day) is not { } input) return;
        var previous = _service.GetDay(day);
        var previousTomorrow = previous?.TomorrowTaskIds?.ToList();
        var previousNotes = _service.GetNotes(day);
        _service.SetTomorrow(day, input.Tomorrow);
        _service.SetNotes(day, input.Notes);

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
            // ファイルにもクリップボードにも出せなかったなら日報は出ていないので、選んだ予定とふり返りも元に戻す。
            if (!copied)
            {
                _service.SetTomorrow(day, previousTomorrow);
                _service.SetNotes(day, previousNotes);
            }
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
