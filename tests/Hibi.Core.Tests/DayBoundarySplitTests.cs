namespace Hibi.Core.Tests;

/// <summary>日の切り替わり（テストでは 04:00）をまたぐ作業記録は、それぞれの業務日に分けて数える（F-W-08）。</summary>
public class DayBoundarySplitTests
{
    private static readonly DateOnly Day21 = new(2026, 11, 21);
    private static readonly DateOnly Day22 = new(2026, 11, 22);

    /// <summary>22日 03:00 に始めて 07:30 に止めたタスク（切り替わりの 04:00 をまたぐ）。</summary>
    private static (TaskService Service, TaskItem Task) StoppedAcrossBoundary()
    {
        var (service, clock) = Fixture.Create(FakeClock.At(2026, 11, 22, 3, 0));
        var t = service.Add("A");
        service.Toggle(t.Id);
        clock.Set(FakeClock.At(2026, 11, 22, 7, 30));
        service.Toggle(t.Id);
        return (service, t);
    }

    [Fact]
    public void またいだ記録は切り替わりの前と後に分ける()
    {
        var (service, t) = StoppedAcrossBoundary();

        Assert.Equal(TimeSpan.FromHours(1), service.DurationOn(t, Day21));
        Assert.Equal(TimeSpan.FromHours(3.5), service.DurationOn(t, Day22));
        Assert.Equal(TimeSpan.FromHours(3.5), service.TotalDurationOn(Day22));
        Assert.Equal(TimeSpan.FromHours(4.5), service.TotalDuration(t));
        Assert.True(service.WorkedOn(t, Day22));
    }

    [Fact]
    public void 切り替わり時刻を変えると記録はそのまま新しい区切りで数え直す()
    {
        var (service, t) = StoppedAcrossBoundary();

        service.Calendar = new BusinessDayCalendar(TimeSpan.FromHours(6));

        Assert.Equal(TimeSpan.FromHours(3), service.DurationOn(t, Day21));
        Assert.Equal(TimeSpan.FromHours(1.5), service.DurationOn(t, Day22));
        Assert.Equal(Day22, service.Today); // 07:30 は 06:00 より後
    }

    [Fact]
    public void 計測中の記録も今日の分は今日に入れる()
    {
        var (service, clock) = Fixture.Create(FakeClock.At(2026, 11, 22, 3, 0));
        var t = service.Add("A");
        service.Toggle(t.Id);
        clock.Set(FakeClock.At(2026, 11, 22, 6, 0));

        Assert.Equal(Day22, service.Today);
        Assert.Equal(TimeSpan.FromHours(2), service.DurationOn(t, Day22));
        Assert.Equal(TimeSpan.FromHours(1), service.DurationOn(t, Day21));
        Assert.Null(Assert.Single(service.LogsIn(t, Day22, Day22)).End); // 今日の分はまだ動いている
    }

    [Fact]
    public void 日報の時間帯も切り替わりで分ける()
    {
        var (service, _) = StoppedAcrossBoundary();

        var today = ReportGenerator.Generate(service, Day22);
        Assert.Contains("稼働合計: 3h30m", today);
        Assert.Contains("04:00〜07:30", today);
        Assert.DoesNotContain("03:00", today);

        var before = ReportGenerator.Generate(service, Day21);
        Assert.Contains("稼働合計: 1h00m", before);
        Assert.Contains("03:00〜04:00", before);
    }

    [Fact]
    public void 期間ではまたいでいても分けた分を日ごとに数え_合計は変わらない()
    {
        var (service, t) = StoppedAcrossBoundary();

        Assert.Equal(TimeSpan.FromHours(4.5), service.DurationIn(t, Day21, Day22));
        Assert.Equal(2, service.LogsIn(t, Day21, Day22).Count());
        Assert.Equal(TimeSpan.FromHours(1), service.DurationIn(t, DateOnly.MinValue, Day21));
    }

    [Fact]
    public void 分けても元の作業記録はそのまま()
    {
        var (service, t) = StoppedAcrossBoundary();
        _ = service.DurationOn(t, Day22);

        var log = Assert.Single(t.WorkLogs);
        Assert.Equal(FakeClock.At(2026, 11, 22, 3, 0), log.Start);
        Assert.Equal(FakeClock.At(2026, 11, 22, 7, 30), log.End);
    }
}
