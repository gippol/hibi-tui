namespace Hibi.Core.Tests;

public class PlannedTodayTests
{
    [Fact]
    public void 前の業務日に選んだ明日の予定が今日の予定になる()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 18, 0)); // 金
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Add("C");
        svc.SetTomorrow(svc.Today, [b.Id, a.Id]);

        clock.Set(FakeClock.At(2026, 11, 9, 9, 0)); // 月（土日をはさむ）

        Assert.Equal([b, a], svc.PlannedToday());
    }

    [Fact]
    public void 完了したものと消したものは今日の予定に出さない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 18, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        var c = svc.Add("C");
        svc.SetTomorrow(svc.Today, [a.Id, b.Id, c.Id]);

        clock.Set(FakeClock.At(2026, 11, 7, 9, 0));
        svc.Complete(a.Id);
        svc.Delete(c.Id);

        Assert.Equal([b], svc.PlannedToday());
    }

    [Fact]
    public void 今日の記録や予定のない日は使わない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.SetTomorrow(svc.Today, [a.Id]);   // 11-05 の予定
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0));
        svc.SetNote(svc.Today, "所感だけ書いた"); // 11-06 は予定なし
        clock.Set(FakeClock.At(2026, 11, 7, 9, 0));
        svc.SetTomorrow(svc.Today, [b.Id]);   // 今日（11-07）に選んだのは明日の分

        Assert.Equal([a], svc.PlannedToday());
    }

    [Fact]
    public void 予定がなければ空()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A");
        Assert.Empty(svc.PlannedToday());
    }
}
