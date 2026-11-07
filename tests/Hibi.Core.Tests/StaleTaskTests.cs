namespace Hibi.Core.Tests;

public class StaleTaskTests
{
    [Fact]
    public void 作ってから指定の日数たつと保留()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 1, 10, 0));
        var t = svc.Add("A");

        clock.Set(FakeClock.At(2026, 11, 14, 23, 0)); // 13日後
        Assert.False(svc.IsStale(t, 14));
        clock.Set(FakeClock.At(2026, 11, 15, 4, 0));  // 14日後（業務日で数える）
        Assert.True(svc.IsStale(t, 14));
    }

    [Fact]
    public void 業務日で数える()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 2, 2, 0)); // 11-01 の業務日
        var t = svc.Add("A");
        clock.Set(FakeClock.At(2026, 11, 15, 4, 0)); // 11-15 の業務日 = 14日後
        Assert.True(svc.IsStale(t, 14));
    }

    [Fact]
    public void 作業すると保留から戻る()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var t = svc.Add("A");
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));
        Assert.True(svc.IsStale(t, 14));

        svc.Start(t.Id);
        Assert.False(svc.IsStale(t, 14)); // 計測中は保留にしない
        clock.Advance(TimeSpan.FromMinutes(5));
        svc.Stop();
        Assert.False(svc.IsStale(t, 14));
        Assert.Equal(new DateOnly(2026, 11, 6), svc.LastActivityDay(t));
    }

    [Fact]
    public void 日をまたいだ作業記録は終了の日を最後に触った日にする()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var t = svc.Add("A");
        clock.Set(FakeClock.At(2026, 10, 3, 10, 0));
        svc.ReplaceWorkLogs(t.Id, [new WorkLog { Start = FakeClock.At(2026, 10, 1, 23, 0), End = FakeClock.At(2026, 10, 2, 5, 0) }]);
        Assert.Equal(new DateOnly(2026, 10, 2), svc.LastActivityDay(t));
    }

    [Fact]
    public void 完了したものと日数0は保留にしない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Complete(a.Id);
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));

        Assert.False(svc.IsStale(a, 14));
        Assert.False(svc.IsStale(b, 0));
    }
}
