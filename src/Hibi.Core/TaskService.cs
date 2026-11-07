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

    /// <summary>未完了タスク（作成順）。</summary>
    public IReadOnlyList<TaskItem> OpenTasks() =>
        Data.Tasks.Where(t => !t.IsCompleted).OrderBy(t => t.CreatedAt).ToList();

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

    public IReadOnlyList<string> Projects() =>
        Data.Tasks
            .Select(t => t.Project)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    // ---------- タスク ----------

    public TaskItem Add(string title, string? project = null, string? memo = null)
    {
        var task = new TaskItem
        {
            Title = RequireTitle(title),
            Project = Normalize(project),
            Memo = Normalize(memo),
            CreatedAt = Now,
        };
        Data.Tasks.Add(task);
        OnChanged();
        return task;
    }

    public void Edit(Guid id, string title, string? project, string? memo)
    {
        var task = Get(id);
        task.Title = RequireTitle(title);
        task.Project = Normalize(project);
        task.Memo = Normalize(memo);
        OnChanged();
    }

    public void Delete(Guid id)
    {
        var task = Get(id);
        Data.Tasks.Remove(task);
        foreach (var day in Data.Days.Values)
            day.TomorrowTaskIds.Remove(id);
        OnChanged();
    }

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
        foreach (var l in logs)
        {
            if (l.End is { } e && e <= l.Start)
                throw new ArgumentException($"終了時刻が開始時刻以前の記録があります: {l.Start:MM-dd HH:mm}");
            if (l.Start > Now)
                throw new ArgumentException($"未来の開始時刻は指定できません: {l.Start:MM-dd HH:mm}");
        }
        task.WorkLogs = logs.OrderBy(l => l.Start).ToList();
        OnChanged();
    }

    // ---------- 業務日ごとの記録 ----------

    public DayRecord? GetDay(DateOnly day) =>
        Data.Days.GetValueOrDefault(BusinessDayCalendar.Key(day));

    /// <summary>
    /// 今日の予定: 今日より前で、明日の予定を選んだいちばん新しい業務日の選択（土日などをはさんでもよい）。
    /// 完了したもの・消したものは除く。並びは選んだときの順。
    /// </summary>
    public IReadOnlyList<TaskItem> PlannedToday()
    {
        var today = BusinessDayCalendar.Key(Today);
        // キーは yyyy-MM-dd なので文字列の順 = 日付の順
        var ids = Data.Days
            .Where(kv => string.CompareOrdinal(kv.Key, today) < 0 && kv.Value.TomorrowTaskIds.Count > 0)
            .OrderByDescending(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value.TomorrowTaskIds)
            .FirstOrDefault() ?? [];
        return ids.Select(Find).OfType<TaskItem>().Where(t => !t.IsCompleted).ToList();
    }

    public void SetNote(DateOnly day, string? note)
    {
        GetOrCreateDay(day).Note = Normalize(note);
        OnChanged();
    }

    public void SetTomorrow(DateOnly day, IEnumerable<Guid> taskIds)
    {
        GetOrCreateDay(day).TomorrowTaskIds = taskIds.Distinct().ToList();
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
