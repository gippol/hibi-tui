namespace Hibi.Core.Tests;

public class PastDayReportTests
{
    [Fact]
    public void 過去の日報ではその後に完了したタスクも進行中に出て累計はその日まで()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 9, 0));
        var api = svc.Add("API修正", "backend");

        clock.Set(FakeClock.At(2026, 11, 5, 10, 0));
        svc.Start(api.Id);
        clock.Advance(TimeSpan.FromMinutes(60));
        svc.Stop();

        // 翌日にも作業して完了
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));
        svc.Start(api.Id);
        clock.Advance(TimeSpan.FromMinutes(30));
        svc.Complete(api.Id);

        var md = ReportGenerator.Generate(svc, new DateOnly(2026, 11, 5));

        Assert.Contains("稼働合計: 1h00m", md);
        Assert.Contains("## 完了\nなし\n", md);
        Assert.Contains("## 進行中\n### backend\n- API修正 — 1h00m（累計 1h00m）10:00〜11:00\n", md);
    }

    [Fact]
    public void 当日の日報の累計はこれまでどおり全期間()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 10, 0));
        var api = svc.Add("API修正");
        svc.Start(api.Id);
        clock.Advance(TimeSpan.FromMinutes(60));
        svc.Stop();
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));
        svc.Start(api.Id);
        clock.Advance(TimeSpan.FromMinutes(30));
        svc.Stop();

        var md = ReportGenerator.Generate(svc, svc.Today);

        Assert.Contains("- API修正 — 0h30m（累計 1h30m）10:00〜10:30\n", md);
    }
}
