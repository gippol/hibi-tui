namespace Hibi.Core.Tests;

public class MonthlyReportTests
{
    private static void Work(TaskService svc, FakeClock clock, TaskItem t, DateTimeOffset start, TimeSpan d)
    {
        clock.Set(start);
        svc.Start(t.Id);
        clock.Advance(d);
        svc.Stop();
    }

    [Fact]
    public void 月の業務日の範囲で集計する()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 9, 0));
        var migration = svc.Add("DBマイグレーション", "backend");
        var api = svc.Add("API修正", "backend");
        var header = svc.Add("ヘッダー修正", "frontend");
        var misc = svc.Add("問い合わせ対応");
        var untouched = svc.Add("触っていないタスク");

        // 11/1 3:00 は 10/31 の業務日なので入らない
        Work(svc, clock, header, FakeClock.At(2026, 11, 1, 3, 0), TimeSpan.FromMinutes(20));
        Work(svc, clock, header, FakeClock.At(2026, 11, 1, 10, 0), TimeSpan.FromMinutes(40) + TimeSpan.FromSeconds(59));
        svc.Complete(header.Id);

        Work(svc, clock, migration, FakeClock.At(2026, 10, 30, 10, 0), TimeSpan.FromMinutes(60)); // 前月
        Work(svc, clock, migration, FakeClock.At(2026, 11, 5, 10, 0), TimeSpan.FromMinutes(90));
        svc.Complete(migration.Id);

        Work(svc, clock, misc, FakeClock.At(2026, 11, 6, 12, 0), TimeSpan.FromMinutes(5));
        Work(svc, clock, api, FakeClock.At(2026, 11, 6, 13, 0), TimeSpan.FromMinutes(135));
        // 12/1 3:00 は 11/30 の業務日なので入る
        Work(svc, clock, api, FakeClock.At(2026, 12, 1, 3, 0), TimeSpan.FromMinutes(30));
        // 翌月の作業と完了（月報では進行中のまま）
        Work(svc, clock, api, FakeClock.At(2026, 12, 2, 10, 0), TimeSpan.FromMinutes(60));
        svc.Complete(api.Id);

        svc.SetNote(new DateOnly(2026, 10, 31), "前月の所感");
        svc.SetNote(new DateOnly(2026, 11, 6), "マイグレーションが無事終わった。\r\n明日はAPIを仕上げる。");
        svc.SetNote(new DateOnly(2026, 11, 1), "月初");

        var md = ReportGenerator.GenerateMonthly(svc, new DateOnly(2026, 11, 15));

        Assert.Equal(
            """
            # 月報 2026-11

            期間: 2026-11-01（日） 〜 2026-11-30（月）
            稼働合計: 5h00m（稼働 4日）

            ## プロジェクト別
            - backend — 4h15m
            - frontend — 0h40m
            - その他 — 0h05m

            ## 完了
            ### backend
            - DBマイグレーション — 1h30m（累計 2h30m）
            ### frontend
            - ヘッダー修正 — 0h40m（累計 1h00m）

            ## 進行中
            ### backend
            - API修正 — 2h45m（累計 2h45m）
            ### その他
            - 問い合わせ対応 — 0h05m（累計 0h05m）

            ## 日別
            - 11-01（日） — 0h40m
            - 11-05（木） — 1h30m
            - 11-06（金） — 2h20m
            - 11-30（月） — 0h30m

            ## 所感
            ### 11-01（日）
            月初

            ### 11-06（金）
            マイグレーションが無事終わった。
            明日はAPIを仕上げる。

            """.ReplaceLineEndings("\n"),
            md);
        Assert.DoesNotContain(untouched.Title, md);
        Assert.DoesNotContain("前月の所感", md);
    }

    [Fact]
    public void 空の月はなしと書きふり返りの欄は出さない()
    {
        var (svc, _) = Fixture.Create();

        var md = ReportGenerator.GenerateMonthly(svc, new DateOnly(2026, 2, 1));

        Assert.Equal(
            """
            # 月報 2026-02

            期間: 2026-02-01（日） 〜 2026-02-28（土）
            稼働合計: 0h00m（稼働 0日）

            ## プロジェクト別
            なし

            ## 完了
            なし

            ## 進行中
            なし

            ## 日別
            なし

            """.ReplaceLineEndings("\n"),
            md);
    }

    [Fact]
    public void 月報はYYYY_MMの名前で保存する()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hibi-test-" + Guid.NewGuid());
        try
        {
            var path = ReportWriter.WriteMonthly(dir, new DateOnly(2026, 11, 1), "x");
            Assert.Equal(Path.Combine(dir, "2026-11.md"), path);
            Assert.Equal("x", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
