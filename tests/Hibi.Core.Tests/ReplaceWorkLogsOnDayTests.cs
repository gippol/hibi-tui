namespace Hibi.Core.Tests;

/// <summary>日報を出すダイアログから、その業務日の作業記録だけを直す（ReplaceWorkLogsOn）。切り替わりはテストでは 04:00。</summary>
public class ReplaceWorkLogsOnDayTests
{
    private static readonly DateOnly Day5 = new(2026, 11, 5);
    private static readonly DateOnly Day6 = new(2026, 11, 6);

    private static WorkLog Log(int d, int h1, int m1, int d2, int h2, int m2) =>
        new() { Start = FakeClock.At(2026, 11, d, h1, m1), End = FakeClock.At(2026, 11, d2, h2, m2) };

    /// <summary>5日 10:00-12:00、5日 23:00-6日 05:00（切り替わりをまたぐ）、6日 13:00-15:00 の記録を持つタスク。</summary>
    private static (TaskService Service, TaskItem Task) WithLogs()
    {
        var (service, _) = Fixture.Create(FakeClock.At(2026, 11, 6, 20, 0));
        var t = service.Add("A");
        service.ReplaceWorkLogs(t.Id, [Log(5, 10, 0, 5, 12, 0), Log(5, 23, 0, 6, 5, 0), Log(6, 13, 0, 6, 15, 0)]);
        return (service, t);
    }

    [Fact]
    public void その日に一部でも入る記録を切り分けずに返す()
    {
        var (service, t) = WithLogs();

        var logs = service.WholeLogsOn(t, Day6);

        Assert.Equal(2, logs.Count);
        Assert.Equal(FakeClock.At(2026, 11, 5, 23, 0), logs[0].Start); // またぐ記録は元のまま
        Assert.Equal(FakeClock.At(2026, 11, 6, 5, 0), logs[0].End);
        Assert.Equal(FakeClock.At(2026, 11, 6, 13, 0), logs[1].Start);
    }

    [Fact]
    public void その日の記録だけを置き換え_ほかの日の記録は残す()
    {
        var (service, t) = WithLogs();

        service.ReplaceWorkLogsOn(t.Id, Day6, [Log(6, 13, 0, 6, 14, 30)]);

        Assert.Equal(TimeSpan.FromHours(1.5), service.DurationOn(t, Day6));
        Assert.Equal(TimeSpan.FromHours(2), service.DurationOn(t, Day5)); // またいでいた記録を消したので前の日の分も減る
        var log = Assert.Single(t.WorkLogs, l => l.Start.Day == 5);
        Assert.Equal(FakeClock.At(2026, 11, 5, 10, 0), log.Start);
    }

    [Fact]
    public void 日報の稼働合計と時間帯に反映される()
    {
        var (service, t) = WithLogs();

        service.ReplaceWorkLogsOn(t.Id, Day6, [Log(6, 9, 0, 6, 12, 0)]);

        var report = ReportGenerator.Generate(service, Day6);
        Assert.Contains("稼働合計: 3h00m", report);
        Assert.Contains("09:00〜12:00", report);
        Assert.DoesNotContain("13:00", report);
    }

    [Fact]
    public void 記録のない完了タスクにも足せる()
    {
        var (service, _) = Fixture.Create(FakeClock.At(2026, 11, 6, 20, 0));
        var t = service.Add("B");
        service.Complete(t.Id);
        Assert.Empty(service.WholeLogsOn(t, Day6));

        service.ReplaceWorkLogsOn(t.Id, Day6, [Log(6, 10, 0, 6, 11, 0)]);

        Assert.Equal(TimeSpan.FromHours(1), service.TotalDurationOn(Day6));
    }

    [Fact]
    public void 正しくない記録なら何も変えない()
    {
        var (service, t) = WithLogs();
        var before = t.WorkLogs.ToList();

        Assert.Throws<ArgumentException>(() => service.ReplaceWorkLogsOn(t.Id, Day6, [Log(6, 21, 0, 6, 22, 0)])); // 未来

        Assert.Equal(before, t.WorkLogs);
    }

    [Fact]
    public void 進行中は日報と同じくその日の時点で完了していないもの()
    {
        var (service, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 10, 0));
        var t = service.Add("A");
        service.Toggle(t.Id);
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0)); // 5日 10:00 から 6日 10:00 まで（またぐ）
        service.Toggle(t.Id);
        service.Complete(t.Id);

        Assert.Equal([t], service.InProgressOn(Day5)); // 5日の時点ではまだ完了していない
        Assert.Empty(service.InProgressOn(Day6));
        Assert.Equal([t], service.CompletedOn(Day6));
    }
}
