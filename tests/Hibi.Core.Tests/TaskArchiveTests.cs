namespace Hibi.Core.Tests;

public sealed class TaskArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));
    private readonly TaskStore _store;
    private readonly TaskService _svc;
    private readonly FakeClock _clock;

    public TaskArchiveTests()
    {
        _store = new TaskStore(_dir);
        (_svc, _clock) = Fixture.Create(FakeClock.At(2025, 12, 1, 9, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>その日に作業して、<paramref name="complete"/> なら続けて完了にする。</summary>
    private TaskItem Done(string title, DateTimeOffset at, bool complete = true)
    {
        _clock.Set(at);
        var t = _svc.Add(title, "backend");
        _svc.Start(t.Id);
        _clock.Advance(TimeSpan.FromMinutes(30));
        _svc.Stop();
        if (complete) _svc.Complete(t.Id);
        return t;
    }

    private void Today(int y, int m, int d) => _clock.Set(FakeClock.At(y, m, d, 9, 0));

    [Fact]
    public void 完了して90日以上たったものだけを最後に触った年のファイルに移す()
    {
        var old2025 = Done("去年の完了", FakeClock.At(2025, 12, 10, 10, 0));
        var old2026 = Done("今年の古い完了", FakeClock.At(2026, 1, 20, 10, 0));
        var recent = Done("最近の完了", FakeClock.At(2026, 4, 1, 10, 0));
        var open = Done("古い未完了", FakeClock.At(2025, 12, 11, 10, 0), complete: false);
        Today(2026, 4, 30);

        Assert.Equal([(2025, 1), (2026, 1)], TaskArchive.Plan(_svc, 90));
        Assert.Equal(2, TaskArchive.Archive(_svc, _store, 90));

        Assert.Equal(new[] { recent.Id, open.Id }.Order(), _svc.Data.Tasks.Select(t => t.Id).Order());
        Assert.Equal([2025, 2026], _store.ArchiveYears());
        Assert.Equal(old2025.Id, Assert.Single(_store.LoadArchive(2025).Tasks).Id);
        var archived = Assert.Single(_store.LoadArchive(2026).Tasks);
        Assert.Equal(old2026.Id, archived.Id);
        Assert.Single(archived.WorkLogs);
        Assert.Empty(TaskArchive.Plan(_svc, 90));
    }

    [Fact]
    public void 完了のあとに触ったものは触った日から数える()
    {
        var t = Done("完了のあとに編集", FakeClock.At(2026, 1, 5, 10, 0));
        _clock.Set(FakeClock.At(2026, 3, 1, 10, 0));
        _svc.Edit(t.Id, "完了のあとに編集した", "backend", null, "メモを足した", null, TaskPriority.Normal);
        Today(2026, 5, 1); // 完了から116日、編集から61日

        Assert.Empty(TaskArchive.Plan(_svc, 90));
    }

    [Fact]
    public void もう一度移すと同じ年のファイルに足していき重ならない()
    {
        var first = Done("1つ目", FakeClock.At(2026, 1, 5, 10, 0));
        Today(2026, 4, 30);
        TaskArchive.Archive(_svc, _store, 90);

        var second = Done("2つ目", FakeClock.At(2026, 2, 5, 10, 0));
        // 途中で落ちて tasks.json にも残っていた、という状況
        var copy = _store.LoadArchive(2026).Tasks[0];
        _svc.Data.Tasks.Add(copy);
        Today(2026, 6, 1);
        Assert.Equal(2, TaskArchive.Archive(_svc, _store, 90));

        Assert.Equal([first.Id, second.Id], _store.LoadArchive(2026).Tasks.Select(t => t.Id));
        Assert.Empty(_svc.Data.Tasks);
    }

    [Fact]
    public void 移したあとも過去の月報と日報は同じ内容になる()
    {
        Done("DBマイグレーション", FakeClock.At(2026, 1, 13, 10, 0));
        var plan = Done("翌日の予定にしたタスク", FakeClock.At(2026, 1, 14, 10, 0), complete: false);
        _svc.SetTomorrow(new DateOnly(2026, 1, 14), [plan.Id]);
        _clock.Set(FakeClock.At(2026, 1, 15, 10, 0));
        _svc.Complete(plan.Id);
        Done("今月の作業", FakeClock.At(2026, 5, 10, 10, 0), complete: false);
        Today(2026, 5, 20);

        var month = new DateOnly(2026, 1, 1);
        var day = new DateOnly(2026, 1, 14);
        var monthly = ReportGenerator.GenerateMonthly(_svc, month);
        var daily = ReportGenerator.Generate(_svc, day);
        var current = ReportGenerator.GenerateMonthly(_svc, new DateOnly(2026, 5, 1));

        Assert.Equal(2, TaskArchive.Archive(_svc, _store, 90));

        Assert.Equal(monthly, ReportGenerator.GenerateMonthly(TaskArchive.ViewFrom(_svc, _store, month), month));
        Assert.Equal(daily, ReportGenerator.Generate(TaskArchive.ViewFrom(_svc, _store, day), day));
        // 90日以内の期間は読み足さない
        Assert.Same(_svc, TaskArchive.ViewFrom(_svc, _store, new DateOnly(2026, 5, 1)));
        Assert.Equal(current, ReportGenerator.GenerateMonthly(_svc, new DateOnly(2026, 5, 1)));
        // 読み足しても今のデータには入らない
        Assert.Single(_svc.Data.Tasks);
    }

    [Fact]
    public void 日数を短くして移したものも最近の報告に入る()
    {
        Done("先月の完了", FakeClock.At(2026, 4, 10, 10, 0));
        Today(2026, 5, 20);
        var month = new DateOnly(2026, 4, 1);
        var before = ReportGenerator.GenerateMonthly(_svc, month);

        Assert.Empty(TaskArchive.Plan(_svc, 90));
        Assert.Equal(1, TaskArchive.Archive(_svc, _store, 30));

        Assert.Equal(new DateOnly(2026, 4, 10), _svc.Data.ArchivedThrough);
        Assert.Equal(before, ReportGenerator.GenerateMonthly(TaskArchive.ViewFrom(_svc, _store, month), month));
        Assert.Same(_svc, TaskArchive.ViewFrom(_svc, _store, new DateOnly(2026, 4, 11)));
    }

    [Fact]
    public void 移した日の記録はtasks_jsonに保存され新しい日だけに進む()
    {
        Done("新しいほう", FakeClock.At(2026, 3, 1, 10, 0));
        Today(2026, 4, 1);
        TaskArchive.Archive(_svc, _store, 30);
        Done("古いほう", FakeClock.At(2026, 1, 1, 10, 0));
        TaskArchive.Archive(_svc, _store, 30);
        _store.Save(_svc.Data);

        Assert.Equal(new DateOnly(2026, 3, 1), _store.Load().ArchivedThrough);
        Assert.Contains("\"archivedThrough\": \"2026-03-01\"", File.ReadAllText(_store.DataPath));
    }

    [Fact]
    public void 何も移していなければ読み足さない()
    {
        Directory.CreateDirectory(_store.ArchiveDirectory);
        File.WriteAllText(_store.ArchivePath(2025), "{ 壊れていても読まない");

        Assert.Same(_svc, TaskArchive.ViewFrom(_svc, _store, new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void 前の年の期間には後の年のファイルも読み足す()
    {
        // 2025年12月に作業して2026年1月に完了 → 2026 のファイルに入る
        var t = Done("年をまたいだタスク", FakeClock.At(2025, 12, 20, 10, 0), complete: false);
        _clock.Set(FakeClock.At(2026, 1, 8, 10, 0));
        _svc.Complete(t.Id);
        Today(2026, 6, 1);
        var month = new DateOnly(2025, 12, 1);
        var before = ReportGenerator.GenerateMonthly(_svc, month);

        TaskArchive.Archive(_svc, _store, 90);

        Assert.Equal([2026], _store.ArchiveYears());
        Assert.Equal(before, ReportGenerator.GenerateMonthly(TaskArchive.ViewFrom(_svc, _store, month), month));
    }

    [Fact]
    public void 読めないアーカイブがあれば何も移さない()
    {
        Done("古い完了", FakeClock.At(2026, 1, 5, 10, 0));
        Today(2026, 6, 1);
        Directory.CreateDirectory(_store.ArchiveDirectory);
        File.WriteAllText(_store.ArchivePath(2026), "{ 壊れている");

        Assert.Throws<DataLoadException>(() => TaskArchive.Archive(_svc, _store, 90));
        Assert.Single(_svc.Data.Tasks);
    }

    [Fact]
    public void アーカイブでないファイルは年として数えない()
    {
        Directory.CreateDirectory(_store.ArchiveDirectory);
        File.WriteAllText(Path.Combine(_store.ArchiveDirectory, "tasks-2026.json"), "{}");
        File.WriteAllText(Path.Combine(_store.ArchiveDirectory, "tasks-old.json"), "{}");
        File.WriteAllText(Path.Combine(_store.ArchiveDirectory, "tasks-12345.json"), "{}");

        Assert.Equal([2026], _store.ArchiveYears());
    }
}
