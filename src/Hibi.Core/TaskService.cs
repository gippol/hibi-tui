namespace Hibi.Core;

/// <summary>
/// タスク・タイマー・業務日ごとの記録に対する操作。変更のたびに <see cref="Changed"/> を呼ぶ（N-R-01）。
/// </summary>
public sealed class TaskService(TaskData data, BusinessDayCalendar calendar, TimeProvider clock)
{
    /// <summary>この時間未満の計測は捨てる（F-W-04）。</summary>
    public static readonly TimeSpan MinimumWorkLog = TimeSpan.FromMinutes(1);

    public event Action? Changed;

    public TaskData Data { get; } = data;
    public BusinessDayCalendar Calendar { get; } = calendar;

    public DateTimeOffset Now => clock.GetLocalNow();
    public DateOnly Today => Calendar.DayOf(Now);

    // ---------- 参照 ----------

    public TaskItem? Find(Guid id) => Data.Tasks.FirstOrDefault(t => t.Id == id);

    public (TaskItem Task, WorkLog Log)? Running
    {
        get
        {
            foreach (var t in Data.Tasks)
                foreach (var l in t.WorkLogs)
                    if (l.End is null) return (t, l);
            return null;
        }
    }

    /// <summary>未完了タスク（並び順）。</summary>
    public IReadOnlyList<TaskItem> OpenTasks() =>
        InOrder(Data.Tasks.Where(t => !t.IsCompleted)).ToList();

    private static IEnumerable<TaskItem> InOrder(IEnumerable<TaskItem> tasks) =>
        tasks.OrderBy(t => t.Order).ThenBy(t => t.CreatedAt);

    /// <summary>指定業務日に完了したタスク（完了順）。</summary>
    public IReadOnlyList<TaskItem> CompletedOn(DateOnly day) =>
        Data.Tasks
            .Where(t => t.CompletedAt is { } c && Calendar.DayOf(c) == day)
            .OrderBy(t => t.CompletedAt)
            .ToList();

    /// <summary>指定業務日に開始した作業記録の合計（F-W-08）。</summary>
    public TimeSpan DurationOn(TaskItem task, DateOnly day) =>
        Sum(task.WorkLogs.Where(l => Calendar.DayOf(l.Start) == day));

    public TimeSpan TotalDuration(TaskItem task) => Sum(task.WorkLogs);

    /// <summary>最後に触った業務日: 作成・手での変更（UpdatedAt）・作業記録の開始と終了のうち、いちばん新しい日。</summary>
    public DateOnly LastActivityDay(TaskItem task)
    {
        var last = task.UpdatedAt is { } u && u > task.CreatedAt ? u : task.CreatedAt;
        foreach (var l in task.WorkLogs)
        {
            var t = l.End ?? Now;
            if (t > last) last = t;
            if (l.Start > last) last = l.Start;
        }
        return Calendar.DayOf(last);
    }

    /// <summary>
    /// 保留: 未完了で、最後に触ってから <paramref name="days"/> 業務日以上たったもの。
    /// 計測中は保留にしない。<paramref name="days"/> が 0 なら保留は使わない。
    /// </summary>
    public bool IsStale(TaskItem task, int days) =>
        days > 0 && !task.IsCompleted && !task.IsRunning
        && Today.DayNumber - LastActivityDay(task).DayNumber >= days;

    /// <summary>保留として扱うか: 手で保留にしたもの、または日数による自動の保留（<see cref="IsStale"/>）。</summary>
    public bool IsOnHold(TaskItem task, int staleDays) =>
        !task.IsCompleted && !task.IsRunning && (task.OnHold || IsStale(task, staleDays));

    /// <summary>期限切れ: 未完了で、期限が今の業務日より前。</summary>
    public bool IsOverdue(TaskItem task) => !task.IsCompleted && task.Due is { } d && d < Today;

    public bool WorkedOn(TaskItem task, DateOnly day) =>
        task.WorkLogs.Any(l => Calendar.DayOf(l.Start) == day);

    // ---------- 期間（月報など）。from と to はどちらも含む ----------

    /// <summary>期間内の業務日に完了したタスク（完了順）。</summary>
    public IReadOnlyList<TaskItem> CompletedIn(DateOnly from, DateOnly to) =>
        Data.Tasks
            .Where(t => t.CompletedAt is { } c && InRange(Calendar.DayOf(c), from, to))
            .OrderBy(t => t.CompletedAt)
            .ToList();

    /// <summary>期間内の業務日に開始した作業記録。</summary>
    public IEnumerable<WorkLog> LogsIn(TaskItem task, DateOnly from, DateOnly to) =>
        task.WorkLogs.Where(l => InRange(Calendar.DayOf(l.Start), from, to));

    public TimeSpan DurationIn(TaskItem task, DateOnly from, DateOnly to) => Sum(LogsIn(task, from, to));

    public bool WorkedIn(TaskItem task, DateOnly from, DateOnly to) => LogsIn(task, from, to).Any();

    private static bool InRange(DateOnly d, DateOnly from, DateOnly to) => d >= from && d <= to;

    private TimeSpan Sum(IEnumerable<WorkLog> logs)
    {
        var now = Now;
        return logs.Aggregate(TimeSpan.Zero, (acc, l) => acc + l.DurationAt(now));
    }

    /// <summary>
    /// プロジェクト名の並び順。大文字小文字を区別せずに並べ、それだけ違うものは文字コード順（大文字が先）。
    /// 漢字・かなは文字コード順のまま。
    /// </summary>
    public static readonly IComparer<string?> ProjectOrder = Comparer<string?>.Create((a, b) =>
    {
        var c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return c != 0 ? c : StringComparer.Ordinal.Compare(a, b);
    });

    /// <summary>使われているプロジェクト名。大文字小文字だけ違うものは別のプロジェクトとして扱う。</summary>
    public IReadOnlyList<string> Projects() =>
        Data.Tasks
            .Select(t => t.Project)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(ProjectOrder)
            .ToList();

    // ---------- タスク ----------

    public TaskItem Add(string title, string? project = null, string? memo = null,
        DateOnly? due = null, TaskPriority priority = TaskPriority.Normal)
    {
        var task = new TaskItem
        {
            Title = RequireTitle(title),
            Project = Normalize(project),
            Memo = Normalize(memo),
            Due = due,
            Priority = priority,
            Order = Data.Tasks.Count == 0 ? 0 : Data.Tasks.Max(t => t.Order) + 1,
            CreatedAt = Now,
        };
        Data.Tasks.Add(task);
        OnChanged();
        return task;
    }

    /// <summary>タスクの中身を置き換える。期限と優先度も渡したもので置き換える（null の期限は「期限なし」）。</summary>
    public void Edit(Guid id, string title, string? project, string? memo, DateOnly? due, TaskPriority priority)
    {
        var task = Get(id);
        task.Title = RequireTitle(title);
        task.Project = Normalize(project);
        task.Memo = Normalize(memo);
        task.Due = due;
        task.Priority = priority;
        task.UpdatedAt = Now;
        OnChanged();
    }

    /// <summary>
    /// 手で保留にする（true）・保留から戻す（false）。戻すときは「触った」ことにするので、
    /// 日数による自動の保留からも外れる。完了したタスクには使えない。
    /// </summary>
    public void SetOnHold(Guid id, bool onHold)
    {
        var task = Get(id);
        if (task.IsCompleted)
            throw new InvalidOperationException("完了したタスクは保留にできません。");
        task.OnHold = onHold;
        task.UpdatedAt = Now;
        OnChanged();
    }

    public void Delete(Guid id)
    {
        var task = Get(id);
        Data.Tasks.Remove(task);
        foreach (var day in Data.Days.Values)
            day.TomorrowTaskIds?.Remove(id);
        OnChanged();
    }

    /// <summary>
    /// 未完了タスクを1つ上（<paramref name="delta"/> = -1）か下（+1）に動かす。
    /// <paramref name="among"/> を渡すと、それに当たるタスクの中での隣と入れ替える（絞り込み中や一覧の区切りごと）。
    /// 動かせなければ false。
    /// </summary>
    public bool MoveOpen(Guid id, int delta, Func<TaskItem, bool>? among = null)
    {
        if (delta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(delta));
        var task = Get(id);
        if (task.IsCompleted) return false;

        var visible = OpenTasks().Where(t => t == task || among?.Invoke(t) != false).ToList();
        var i = visible.IndexOf(task);
        if (i + delta < 0 || i + delta >= visible.Count) return false;
        var neighbor = visible[i + delta];

        // 全タスクの並びの中で、隣の前（上へ）か後ろ（下へ）に入れて番号を振り直す
        var all = InOrder(Data.Tasks).ToList();
        all.Remove(task);
        var at = all.IndexOf(neighbor) + (delta > 0 ? 1 : 0);
        all.Insert(at, task);
        for (var n = 0; n < all.Count; n++) all[n].Order = n;

        OnChanged();
        return true;
    }

    /// <summary>
    /// 未完了タスクをプロジェクトごとにまとめる。プロジェクトは <see cref="ProjectOrder"/> の順、プロジェクトなしは最後。
    /// 同じプロジェクトの中は今の並びのまま。<paramref name="among"/> を渡すと、それに当たるタスクだけを
    /// それらが今いる位置の中で並べ替え、ほかのタスクの位置は変えない。並びが変わらなければ false。
    /// </summary>
    public bool GroupOpenByProject(Func<TaskItem, bool>? among = null)
    {
        var all = InOrder(Data.Tasks).ToList();
        var slots = new List<int>();
        for (var i = 0; i < all.Count; i++)
            if (!all[i].IsCompleted && among?.Invoke(all[i]) != false) slots.Add(i);

        var current = slots.Select(i => all[i]).ToList();
        var grouped = ByProject(current);
        if (grouped.SequenceEqual(current)) return false;

        for (var n = 0; n < slots.Count; n++) all[slots[n]] = grouped[n];
        for (var n = 0; n < all.Count; n++) all[n].Order = n;

        OnChanged();
        return true;
    }

    /// <summary>プロジェクト名の順、プロジェクトなしは最後。OrderBy は安定なので、同じプロジェクトの中は今の並びが残る。</summary>
    private static List<TaskItem> ByProject(IEnumerable<TaskItem> tasks) =>
        tasks.OrderBy(t => t.Project is null).ThenBy(t => t.Project, ProjectOrder).ToList();

    public void Complete(Guid id)
    {
        var task = Get(id);
        if (task.IsCompleted) return;
        if (task.IsRunning) StopCore();
        task.CompletedAt = Now;
        OnChanged();
    }

    public void Uncomplete(Guid id)
    {
        var task = Get(id);
        if (!task.IsCompleted) return;
        task.CompletedAt = null;
        task.UpdatedAt = Now;
        OnChanged();
    }

    // ---------- タイマー ----------

    /// <summary>計測を開始する。別のタスクが計測中なら先に止める（F-W-02）。完了済みには開始できない。</summary>
    public void Start(Guid id)
    {
        var task = Get(id);
        if (task.IsCompleted)
            throw new InvalidOperationException("完了したタスクのタイマーは開始できません。");
        if (task.IsRunning) return;
        StopCore();
        task.OnHold = false; // 作業を始めたら保留ではない
        task.WorkLogs.Add(new WorkLog { Start = Now });
        OnChanged();
    }

    public void Stop()
    {
        if (StopCore()) OnChanged();
    }

    /// <summary>そのタスクが計測中なら止め、そうでなければ開始する。</summary>
    public void Toggle(Guid id)
    {
        if (Get(id).IsRunning) Stop();
        else Start(id);
    }

    /// <summary>計測中のタイマーを指定時刻で止める（止め忘れの修正, F-W-07）。</summary>
    public void StopAt(DateTimeOffset end)
    {
        if (Running is not { } r) return;
        if (end <= r.Log.Start)
            throw new ArgumentException("終了時刻は開始時刻より後にしてください。");
        if (end > Now)
            throw new ArgumentException("終了時刻に未来の時刻は指定できません。");
        FinishLog(r.Task, r.Log, end);
        OnChanged();
    }

    private bool StopCore()
    {
        if (Running is not { } r) return false;
        FinishLog(r.Task, r.Log, Now);
        return true;
    }

    private static void FinishLog(TaskItem task, WorkLog log, DateTimeOffset end)
    {
        log.End = end;
        if (log.DurationAt(end) < MinimumWorkLog)
            task.WorkLogs.Remove(log);
    }

    /// <summary>作業記録を丸ごと置き換える（手修正, F-W-05）。</summary>
    public void ReplaceWorkLogs(Guid id, IReadOnlyList<WorkLog> logs)
    {
        var task = Get(id);
        var openCount = logs.Count(l => l.End is null);
        if (openCount > 1)
            throw new ArgumentException("計測中（終了時刻なし）の記録は1つまでです。");
        if (openCount == 1)
        {
            if (task.IsCompleted)
                throw new ArgumentException("完了したタスクに計測中の記録は置けません。");
            if (Running is { } r && r.Task != task)
                throw new ArgumentException($"「{r.Task.Title}」が計測中です。先に止めてください。");
        }
        // 編集画面は分単位でしか見せないので、触っていない行は元の記録（秒・オフセット付き）に戻し、検証もしない。
        // そうしないと同じ分に始まって終わった記録や、時計の都合で未来になった記録があるだけで保存できなくなる。
        var untouched = task.WorkLogs.ToList();
        var result = new List<WorkLog>(logs.Count);
        foreach (var l in logs)
        {
            var original = untouched.FirstOrDefault(o => SameMinute(o.Start, l.Start) && SameMinute(o.End, l.End));
            if (original is not null)
            {
                untouched.Remove(original);
                result.Add(original);
                continue;
            }
            if (l.Start > Now)
                throw new ArgumentException($"未来の開始時刻は指定できません: {l.Start:MM-dd HH:mm}");
            if (l.End is { } e)
            {
                if (e <= l.Start)
                    throw new ArgumentException($"終了時刻が開始時刻以前の記録があります: {l.Start:MM-dd HH:mm}");
                if (e > Now)
                    throw new ArgumentException($"未来の終了時刻は指定できません: {e:MM-dd HH:mm}");
            }
            result.Add(l);
        }
        task.WorkLogs = result.OrderBy(l => l.Start).ToList();
        OnChanged();
    }

    /// <summary>画面に出る時刻（その記録のオフセットでの壁時計、分まで）が同じか。</summary>
    private static bool SameMinute(DateTimeOffset? a, DateTimeOffset? b) =>
        (a, b) switch
        {
            (null, null) => true,
            ({ } x, { } y) => TruncateToMinute(x.DateTime) == TruncateToMinute(y.DateTime),
            _ => false,
        };

    private static DateTime TruncateToMinute(DateTime d) => d.AddTicks(-(d.Ticks % TimeSpan.TicksPerMinute));

    // ---------- 業務日ごとの記録 ----------

    public DayRecord? GetDay(DateOnly day) =>
        Data.Days.GetValueOrDefault(BusinessDayCalendar.Key(day));

    /// <summary>
    /// 今日の予定: 今日より前で、明日の予定を選んだいちばん新しい業務日の選択（土日などをはさんでもよい）。
    /// その日に何も選ばなかった・選んだものを全部消したなら、それより前の日にはさかのぼらず予定なし。
    /// 完了したもの・消したもの・手で保留にしたものは除く。並びは選んだときの順。
    /// </summary>
    public IReadOnlyList<TaskItem> PlannedToday() =>
        (PlanSource() ?? []).Select(Find).OfType<TaskItem>().Where(IsPlannedNow).ToList();

    private static bool IsPlannedNow(TaskItem t) => !t.IsCompleted && !t.OnHold;

    /// <summary>今日の予定のもとになる、前の業務日に選んだ id の並び（<see cref="PlannedToday"/>）。なければ null。</summary>
    private List<Guid>? PlanSource()
    {
        var today = BusinessDayCalendar.Key(Today);
        // キーは yyyy-MM-dd なので文字列の順 = 日付の順
        return Data.Days
            .Where(kv => string.CompareOrdinal(kv.Key, today) < 0 && kv.Value.TomorrowTaskIds is not null)
            .OrderByDescending(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value.TomorrowTaskIds)
            .FirstOrDefault();
    }

    /// <summary>
    /// 今日の予定のタスクを1つ上（-1）か下（+1）に動かす。選んだ日の記録（TomorrowTaskIds）の順を変える。
    /// <paramref name="among"/> を渡すと、それに当たる予定の中での隣と入れ替える。動かせなければ false。
    /// </summary>
    public bool MovePlanned(Guid id, int delta, Func<TaskItem, bool>? among = null)
    {
        if (delta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (PlanSource() is not { } ids) return false;
        var visible = PlannedToday().Where(t => t.Id == id || among?.Invoke(t) != false).ToList();
        var i = visible.FindIndex(t => t.Id == id);
        if (i < 0 || i + delta < 0 || i + delta >= visible.Count) return false;
        var neighbor = visible[i + delta].Id;

        ids.Remove(id);
        ids.Insert(ids.IndexOf(neighbor) + (delta > 0 ? 1 : 0), id);
        OnChanged();
        return true;
    }

    /// <summary>
    /// 今日の予定をプロジェクトごとにまとめる（並べ方は <see cref="GroupOpenByProject"/> と同じ）。
    /// <paramref name="among"/> に当たらない予定や、完了・保留で今は出ていないものの位置は変えない。変わらなければ false。
    /// </summary>
    public bool GroupPlannedByProject(Func<TaskItem, bool>? among = null)
    {
        if (PlanSource() is not { } ids) return false;
        var slots = new List<int>();
        for (var i = 0; i < ids.Count; i++)
            if (Find(ids[i]) is { } t && IsPlannedNow(t) && among?.Invoke(t) != false) slots.Add(i);

        var current = slots.Select(i => Get(ids[i])).ToList();
        var grouped = ByProject(current);
        if (grouped.SequenceEqual(current)) return false;

        for (var n = 0; n < slots.Count; n++) ids[slots[n]] = grouped[n].Id;
        OnChanged();
        return true;
    }

    public void SetNote(DateOnly day, string? note)
    {
        GetOrCreateDay(day).Note = Normalize(note);
        OnChanged();
    }

    /// <summary>その日のふり返り（課題・学び・所感）。記録がなければ全部 null。</summary>
    public DayNotes GetNotes(DateOnly day) =>
        GetDay(day) is { } r ? new DayNotes(r.Problem, r.Try, r.Note) : DayNotes.Empty;

    /// <summary>ふり返りの3つの欄をまとめて書き換える。空白だけの欄は null にする。</summary>
    public void SetNotes(DateOnly day, DayNotes notes)
    {
        var record = GetOrCreateDay(day);
        record.Problem = Normalize(notes.Problem);
        record.Try = Normalize(notes.Try);
        record.Note = Normalize(notes.Note);
        OnChanged();
    }

    /// <summary>明日の予定を決める。null なら「選んでいない」に戻す。</summary>
    public void SetTomorrow(DateOnly day, IEnumerable<Guid>? taskIds)
    {
        var record = GetOrCreateDay(day);
        record.TomorrowTaskIds = taskIds?.Distinct().ToList();
        OnChanged();
    }

    private DayRecord GetOrCreateDay(DateOnly day)
    {
        var key = BusinessDayCalendar.Key(day);
        if (!Data.Days.TryGetValue(key, out var record))
            Data.Days[key] = record = new DayRecord();
        return record;
    }

    // ---------- helpers ----------

    private TaskItem Get(Guid id) =>
        Find(id) ?? throw new KeyNotFoundException($"タスクが見つかりません: {id}");

    private static string RequireTitle(string title)
    {
        var t = title.Trim();
        if (t.Length == 0) throw new ArgumentException("タイトルを入力してください。");
        return t;
    }

    private static string? Normalize(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private void OnChanged() => Changed?.Invoke();
}
