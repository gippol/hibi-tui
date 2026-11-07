namespace Hibi.Core.Tests;

public class TaskServiceTests
{
    [Fact]
    public void 業務日は切り替わり時刻で区切る()
    {
        var cal = new BusinessDayCalendar(TimeSpan.FromHours(4));
        Assert.Equal(new DateOnly(2026, 11, 6), cal.DayOf(FakeClock.At(2026, 11, 7, 2, 0)));
        Assert.Equal(new DateOnly(2026, 11, 6), cal.DayOf(FakeClock.At(2026, 11, 7, 3, 59)));
        Assert.Equal(new DateOnly(2026, 11, 7), cal.DayOf(FakeClock.At(2026, 11, 7, 4, 0)));
    }

    [Fact]
    public void 追加すると変更通知が来てタイトルとプロジェクトが整形される()
    {
        var (svc, _) = Fixture.Create();
        var changed = 0;
        svc.Changed += () => changed++;

        var t = svc.Add("  API修正 ", "  ", null);

        Assert.Equal("API修正", t.Title);
        Assert.Null(t.Project);
        Assert.Equal(1, changed);
        Assert.Throws<ArgumentException>(() => svc.Add("   "));
    }

    [Fact]
    public void 別のタスクを開始すると前のタイマーは止まる()
    {
        var (svc, clock) = Fixture.Create();
        var a = svc.Add("A");
        var b = svc.Add("B");

        svc.Start(a.Id);
        clock.Advance(TimeSpan.FromMinutes(30));
        svc.Start(b.Id);

        Assert.False(a.IsRunning);
        Assert.True(b.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(30), svc.TotalDuration(a));
        Assert.Equal(b, svc.Running!.Value.Task);
    }

    [Fact]
    public void 一分未満の計測は記録しない()
    {
        var (svc, clock) = Fixture.Create();
        var a = svc.Add("A");

        svc.Start(a.Id);
        clock.Advance(TimeSpan.FromSeconds(59));
        svc.Stop();

        Assert.Empty(a.WorkLogs);
    }

    [Fact]
    public void 完了するとタイマーが止まり完了を取り消せる()
    {
        var (svc, clock) = Fixture.Create();
        var a = svc.Add("A");
        svc.Start(a.Id);
        clock.Advance(TimeSpan.FromMinutes(10));

        svc.Complete(a.Id);

        Assert.Equal(TaskState.Completed, a.State);
        Assert.Null(svc.Running);
        Assert.Single(a.WorkLogs);
        Assert.Throws<InvalidOperationException>(() => svc.Start(a.Id));

        svc.Uncomplete(a.Id);
        Assert.Equal(TaskState.Open, a.State);
        Assert.Null(a.CompletedAt);
    }

    [Fact]
    public void 完了したタスクは翌業務日に今日の完了から消える()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 23, 0));
        var a = svc.Add("A");
        clock.Set(FakeClock.At(2026, 11, 7, 2, 0)); // まだ 11/6 の業務日
        svc.Complete(a.Id);

        Assert.Contains(a, svc.CompletedOn(svc.Today));
        clock.Set(FakeClock.At(2026, 11, 7, 4, 0));
        Assert.DoesNotContain(a, svc.CompletedOn(svc.Today));
        Assert.Contains(a, svc.Data.Tasks); // データには残る
    }

    [Fact]
    public void 作業時間は開始時刻の業務日に入る()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 7, 3, 0));
        var a = svc.Add("A");
        svc.Start(a.Id);
        clock.Advance(TimeSpan.FromHours(2)); // 5:00 まで（業務日をまたぐ）
        svc.Stop();

        Assert.Equal(TimeSpan.FromHours(2), svc.DurationOn(a, new DateOnly(2026, 11, 6)));
        Assert.Equal(TimeSpan.Zero, svc.DurationOn(a, new DateOnly(2026, 11, 7)));
    }

    [Fact]
    public void 止め忘れたタイマーを指定時刻で止められる()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 9, 0));
        var a = svc.Add("A");
        svc.Start(a.Id);
        clock.Set(FakeClock.At(2026, 11, 7, 9, 0));

        Assert.Throws<ArgumentException>(() => svc.StopAt(FakeClock.At(2026, 11, 6, 8, 0)));
        svc.StopAt(FakeClock.At(2026, 11, 6, 18, 0));

        Assert.Null(svc.Running);
        Assert.Equal(TimeSpan.FromHours(9), svc.TotalDuration(a));
    }

    [Fact]
    public void 作業記録の置き換えは検証される()
    {
        var (svc, _) = Fixture.Create(FakeClock.At(2026, 11, 6, 18, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Start(b.Id);

        var open = new WorkLog { Start = FakeClock.At(2026, 11, 6, 10, 0) };
        Assert.Throws<ArgumentException>(() => svc.ReplaceWorkLogs(a.Id, [open])); // B が計測中
        Assert.Throws<ArgumentException>(() => svc.ReplaceWorkLogs(a.Id,
            [new WorkLog { Start = FakeClock.At(2026, 11, 6, 10, 0), End = FakeClock.At(2026, 11, 6, 9, 0) }]));

        svc.ReplaceWorkLogs(a.Id,
        [
            new WorkLog { Start = FakeClock.At(2026, 11, 6, 13, 0), End = FakeClock.At(2026, 11, 6, 14, 0) },
            new WorkLog { Start = FakeClock.At(2026, 11, 6, 10, 0), End = FakeClock.At(2026, 11, 6, 11, 30) },
        ]);
        Assert.Equal(TimeSpan.FromMinutes(150), svc.TotalDuration(a));
        Assert.Equal(FakeClock.At(2026, 11, 6, 10, 0), a.WorkLogs[0].Start);
    }

    [Fact]
    public void 削除すると明日の予定からも消える()
    {
        var (svc, _) = Fixture.Create();
        var a = svc.Add("A");
        svc.SetTomorrow(svc.Today, [a.Id]);

        svc.Delete(a.Id);

        Assert.Empty(svc.GetDay(svc.Today)!.TomorrowTaskIds);
    }
}
