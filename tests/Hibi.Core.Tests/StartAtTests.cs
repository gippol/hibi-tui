namespace Hibi.Core.Tests;

/// <summary>さかのぼって開始する（開始の押し忘れの修正）。</summary>
public class StartAtTests
{
    private static readonly DateTimeOffset Now = FakeClock.At(2026, 11, 26, 10, 20, 30);

    // ---------- 時刻の入力 ----------

    [Theory]
    [InlineData("-15", 10, 5)]      // 秒は切り捨ててから15分前
    [InlineData("9:30", 9, 30)]
    [InlineData(" 09:30 ", 9, 30)]
    [InlineData("10:20", 10, 20)]
    public void 時刻を読む(string text, int hour, int minute)
    {
        Assert.Equal(FakeClock.At(2026, 11, 26, hour, minute), StartTimeText.Parse(text, Now));
    }

    [Fact]
    public void 今より後の時刻は前の日にする()
    {
        var now = FakeClock.At(2026, 11, 27, 0, 30);
        Assert.Equal(FakeClock.At(2026, 11, 26, 23, 50), StartTimeText.Parse("23:50", now));
    }

    [Fact]
    public void 日付つきでも書ける()
    {
        Assert.Equal(FakeClock.At(2026, 11, 25, 18, 0), StartTimeText.Parse("2026-11-25 18:00", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-0")]
    [InlineData("-abc")]
    [InlineData("25:00")]
    [InlineData("930")]
    public void 読めない形はエラー(string text)
    {
        Assert.Throws<FormatException>(() => StartTimeText.Parse(text, Now));
    }

    // ---------- StartAt ----------

    [Fact]
    public void 止まっているときはその時刻から計測する_保留も外れる()
    {
        var (svc, _) = Fixture.Create(Now);
        var t = svc.Add("A");
        svc.SetOnHold(t.Id, true);

        svc.StartAt(t.Id, FakeClock.At(2026, 11, 26, 9, 30));

        var log = Assert.Single(t.WorkLogs);
        Assert.Equal(FakeClock.At(2026, 11, 26, 9, 30), log.Start);
        Assert.Null(log.End);
        Assert.False(t.OnHold);
    }

    [Fact]
    public void 別のタスクが計測中ならその時刻で止めて切り替える()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 26, 9, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Start(a.Id);
        clock.Set(Now);

        svc.StartAt(b.Id, FakeClock.At(2026, 11, 26, 9, 45));

        Assert.Equal(FakeClock.At(2026, 11, 26, 9, 45), Assert.Single(a.WorkLogs).End);
        Assert.Equal(FakeClock.At(2026, 11, 26, 9, 45), Assert.Single(b.WorkLogs).Start);
        Assert.Equal(b, svc.Running?.Task);
    }

    [Fact]
    public void 同じタスクが計測中なら開始時刻を直す()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 26, 10, 0));
        var a = svc.Add("A");
        svc.Start(a.Id);
        clock.Set(Now);

        svc.StartAt(a.Id, FakeClock.At(2026, 11, 26, 9, 40));

        var log = Assert.Single(a.WorkLogs);
        Assert.Equal(FakeClock.At(2026, 11, 26, 9, 40), log.Start);
        Assert.Null(log.End);
    }

    [Fact]
    public void ほかの作業記録と重なる時刻はエラーで何も変えない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 26, 9, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Start(a.Id);
        clock.Set(FakeClock.At(2026, 11, 26, 10, 0));
        svc.Stop();
        clock.Set(Now);

        var ex = Assert.Throws<ArgumentException>(() => svc.StartAt(b.Id, FakeClock.At(2026, 11, 26, 9, 50)));

        Assert.Contains("10:00", ex.Message);
        Assert.Empty(b.WorkLogs);
        // 止めたちょうどの時刻からならいい
        svc.StartAt(b.Id, FakeClock.At(2026, 11, 26, 10, 0));
        Assert.Single(b.WorkLogs);
    }

    [Fact]
    public void 計測中の別のタスクの開始より前はエラー()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 26, 10, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.Start(a.Id);
        clock.Set(Now);

        Assert.Throws<ArgumentException>(() => svc.StartAt(b.Id, FakeClock.At(2026, 11, 26, 10, 0)));
        Assert.Null(Assert.Single(a.WorkLogs).End);
        Assert.Empty(b.WorkLogs);
    }

    [Fact]
    public void 未来の時刻と完了したタスクはエラー()
    {
        var (svc, _) = Fixture.Create(Now);
        var a = svc.Add("A");
        Assert.Throws<ArgumentException>(() => svc.StartAt(a.Id, Now.AddMinutes(1)));

        svc.Complete(a.Id);
        Assert.Throws<InvalidOperationException>(() => svc.StartAt(a.Id, Now.AddMinutes(-5)));
    }

    [Fact]
    public void 最後に止めた時刻は計測中の記録を除いて探す()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 26, 9, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        Assert.Null(svc.LatestEnd());

        svc.Start(a.Id);
        clock.Set(FakeClock.At(2026, 11, 26, 9, 30));
        svc.Start(b.Id);   // A は 9:30 に止まる
        clock.Set(Now);

        var latest = svc.LatestEnd(svc.Running?.Log);
        Assert.Equal(a, latest?.Task);
        Assert.Equal(FakeClock.At(2026, 11, 26, 9, 30), latest?.At);
    }
}
