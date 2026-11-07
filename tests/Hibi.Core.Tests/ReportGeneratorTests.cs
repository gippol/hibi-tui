namespace Hibi.Core.Tests;

public class ReportGeneratorTests
{
    private static void Work(TaskService svc, FakeClock clock, TaskItem t, DateTimeOffset start, TimeSpan d)
    {
        clock.Set(start);
        svc.Start(t.Id);
        clock.Advance(d);
        svc.Stop();
    }

    [Fact]
    public void テンプレートどおりに出力する()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 9, 0));
        var migration = svc.Add("DBマイグレーション", "backend");
        var api = svc.Add("API修正", "backend");
        var header = svc.Add("ヘッダー修正", "frontend");
        var login = svc.Add("ログイン画面", "frontend");
        var misc = svc.Add("問い合わせ対応");
        var untouched = svc.Add("触っていないタスク");

        // 前日の作業（累計にだけ入る）
        Work(svc, clock, migration, FakeClock.At(2026, 11, 5, 10, 0), TimeSpan.FromMinutes(90));
        Work(svc, clock, api, FakeClock.At(2026, 11, 5, 13, 0), TimeSpan.FromMinutes(115));

        // 当日
        Work(svc, clock, migration, FakeClock.At(2026, 11, 6, 9, 0), TimeSpan.FromMinutes(90));
        svc.Complete(migration.Id);
        Work(svc, clock, header, FakeClock.At(2026, 11, 6, 11, 0), TimeSpan.FromMinutes(40) + TimeSpan.FromSeconds(59));
        svc.Complete(header.Id);
        Work(svc, clock, misc, FakeClock.At(2026, 11, 6, 12, 0), TimeSpan.FromMinutes(5));
        Work(svc, clock, api, FakeClock.At(2026, 11, 6, 13, 0), TimeSpan.FromMinutes(135));

        svc.SetTomorrow(svc.Today, [api.Id, login.Id]);
        svc.SetNote(svc.Today, "マイグレーションが無事終わった。\r\n明日はAPIを仕上げる。");

        var md = ReportGenerator.Generate(svc, new DateOnly(2026, 11, 6));

        Assert.Equal(
            """
            # 日報 2026-11-06（金）

            稼働合計: 4h30m

            ## 完了
            ### backend
            - DBマイグレーション — 1h30m（累計 3h00m）
            ### frontend
            - ヘッダー修正 — 0h40m（累計 0h40m）

            ## 進行中
            ### backend
            - API修正 — 2h15m（累計 4h10m）
            ### その他
            - 問い合わせ対応 — 0h05m（累計 0h05m）

            ## 明日の予定
            - API修正
            - ログイン画面

            ## 所感
            マイグレーションが無事終わった。
            明日はAPIを仕上げる。

            """.ReplaceLineEndings("\n"),
            md);
        Assert.DoesNotContain(untouched.Title, md);
    }

    [Fact]
    public void 空の欄はなしと書く()
    {
        var (svc, _) = Fixture.Create();

        var md = ReportGenerator.Generate(svc, svc.Today);

        Assert.Equal(
            """
            # 日報 2026-11-06（金）

            稼働合計: 0h00m

            ## 完了
            なし

            ## 進行中
            なし

            ## 明日の予定
            なし

            ## 所感
            なし

            """.ReplaceLineEndings("\n"),
            md);
    }

    [Theory]
    [InlineData(0, "0h00m")]
    [InlineData(59, "0h00m")]
    [InlineData(60 * 61 + 59, "1h01m")]
    [InlineData(60 * 60 * 12 + 60 * 5, "12h05m")]
    public void 時間は分未満を切り捨てる(int seconds, string expected) =>
        Assert.Equal(expected, DurationFormat.Short(TimeSpan.FromSeconds(seconds)));
}
