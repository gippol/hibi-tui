namespace Hibi.Core.Tests;

public class WeeklyReportTests
{
    private static void Work(TaskService svc, FakeClock clock, TaskItem t, DateTimeOffset start, TimeSpan d)
    {
        clock.Set(start);
        svc.Start(t.Id);
        clock.Advance(d);
        svc.Stop();
    }

    [Theory]
    [InlineData(2026, 11, 4, 2026, 11, 2, "2026-W45")]  // 水 → 月曜始まり
    [InlineData(2026, 11, 2, 2026, 11, 2, "2026-W45")]  // 月
    [InlineData(2026, 11, 8, 2026, 11, 2, "2026-W45")]  // 日は前の月曜の週
    [InlineData(2027, 1, 1, 2026, 12, 28, "2026-W53")]  // 年をまたぐ週は ISO 週番号
    [InlineData(2025, 12, 31, 2025, 12, 29, "2026-W01")]
    public void 週は月曜始まりでISO週番号で呼ぶ(int y, int m, int d, int sy, int sm, int sd, string name)
    {
        var week = ReportPeriod.WeekOf(new DateOnly(y, m, d));
        Assert.Equal(new DateOnly(sy, sm, sd), week.From);
        Assert.Equal(new DateOnly(sy, sm, sd).AddDays(6), week.To);
        Assert.Equal(name, week.Name);
    }

    [Fact]
    public void 週の業務日の範囲で集計する()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 9, 0));
        var api = svc.Add("API修正", "backend");
        var header = svc.Add("ヘッダー修正", "frontend");
        svc.Add("触っていないタスク");

        // 11/2（月）3:00 は 11/1（日）の業務日なので前の週
        Work(svc, clock, header, FakeClock.At(2026, 11, 2, 3, 0), TimeSpan.FromMinutes(20));
        Work(svc, clock, header, FakeClock.At(2026, 11, 2, 10, 0), TimeSpan.FromMinutes(40));
        svc.Complete(header.Id);
        Work(svc, clock, api, FakeClock.At(2026, 11, 4, 13, 0), TimeSpan.FromMinutes(90));
        // 11/9（月）3:00 は 11/8（日）の業務日なので入る
        Work(svc, clock, api, FakeClock.At(2026, 11, 9, 3, 0), TimeSpan.FromMinutes(30));
        // 翌週
        Work(svc, clock, api, FakeClock.At(2026, 11, 10, 10, 0), TimeSpan.FromMinutes(60));

        svc.SetNote(new DateOnly(2026, 11, 4), "API を進めた");
        svc.SetNote(new DateOnly(2026, 11, 1), "前の週の所感");

        var md = ReportGenerator.GenerateWeekly(svc, new DateOnly(2026, 11, 6));

        Assert.Equal(
            """
            # 週報 2026-W45

            期間: 2026-11-02（月） 〜 2026-11-08（日）
            稼働合計: 2h40m（稼働 3日）

            ## プロジェクト別
            - backend — 2h00m
            - frontend — 0h40m

            ## 完了
            ### frontend
            - ヘッダー修正 — 0h40m（累計 1h00m）

            ## 進行中
            ### backend
            - API修正 — 2h00m（累計 3h00m）

            ## 日別
            - 11-02（月） — 0h40m
            - 11-04（水） — 1h30m
            - 11-08（日） — 0h30m

            ## 所感
            ### 11-04（水）
            API を進めた

            """.ReplaceLineEndings("\n"), md);
    }

    [Fact]
    public void 週報は日報フォルダにISO週の名前で保存する()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = ReportWriter.WriteWeekly(dir, ReportPeriod.WeekOf(new DateOnly(2026, 11, 6)), "x");
            Assert.Equal(Path.Combine(dir, "2026-W45.md"), path);
            Assert.Equal("x", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
