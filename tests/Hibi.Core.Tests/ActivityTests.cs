namespace Hibi.Core.Tests;

/// <summary>プロジェクト > 作業分類 > タスク（schemaVersion 7）。</summary>
public class ActivityTests
{
    private static void Work(TaskService svc, FakeClock clock, TaskItem t, DateTimeOffset start, TimeSpan d)
    {
        clock.Set(start);
        svc.Start(t.Id);
        clock.Advance(d);
        svc.Stop();
    }

    private static List<string> Titles(IEnumerable<TaskItem> tasks) => tasks.Select(t => t.Title).ToList();

    // ---------- 追加・編集 ----------

    [Fact]
    public void プロジェクトがなければ作業分類は捨てる()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A", null, activity: "設計");

        Assert.Null(t.Activity);

        svc.Edit(t.Id, "A", "開発", " 設計 ", null, null, TaskPriority.Normal);
        Assert.Equal("設計", t.Activity);
        Assert.Equal("開発/設計", t.ProjectLabel);

        svc.Edit(t.Id, "A", " ", "設計", null, null, TaskPriority.Normal);
        Assert.Null(t.Project);
        Assert.Null(t.Activity);
        Assert.Null(t.ProjectLabel);
    }

    [Fact]
    public void 作業分類の候補は全プロジェクトで共通()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A", "開発", activity: "設計");
        svc.Add("B", "開発", activity: "実装");
        svc.Add("C", "開発", activity: "設計");
        svc.Add("D", "開発");
        svc.Add("E", "営業", activity: "見積");

        svc.Add("F", "営業", activity: "設計");

        Assert.Equal(["実装", "見積", "設計"], svc.Activities());
    }

    // ---------- 一行記法 ----------

    [Fact]
    public void 一行記法でプロジェクトと作業分類を書ける()
    {
        var r = QuickAdd.Parse("画面設計 #開発/設計 !高", new DateOnly(2026, 11, 26));

        Assert.Equal("画面設計", r.Title);
        Assert.Equal("開発", r.Project);
        Assert.Equal("設計", r.Activity);
    }

    [Theory]
    [InlineData("#開発", "開発", null)]
    [InlineData("#開発/", "開発", null)]
    [InlineData("#開発/設計/詳細", "開発", "設計/詳細")]
    public void 作業分類は最初のスラッシュの後ろ(string word, string project, string? activity)
    {
        var r = QuickAdd.Parse($"作業 {word}", new DateOnly(2026, 11, 26));

        Assert.Equal("作業", r.Title);
        Assert.Equal(project, r.Project);
        Assert.Equal(activity, r.Activity);
    }

    [Fact]
    public void プロジェクト名がなければ記法とみない()
    {
        var r = QuickAdd.Parse("作業 #/設計", new DateOnly(2026, 11, 26));

        Assert.Equal("作業 #/設計", r.Title);
        Assert.Null(r.Project);
        Assert.Null(r.Activity);
    }

    // ---------- 絞り込み ----------

    [Fact]
    public void 絞り込みは作業分類にも当たる()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("画面", "開発", activity: "設計");

        Assert.True(TaskFilter.Matches(t, "設計"));
        Assert.True(TaskFilter.Matches(t, "#設計"));
        Assert.True(TaskFilter.Matches(t, "#開発/設"));
        Assert.False(TaskFilter.Matches(t, "#開発/実装"));
    }

    // ---------- まとめる（s） ----------

    [Fact]
    public void プロジェクトの中を作業分類でまとめる_作業分類なしは最後()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("1", "開発", activity: "実装");
        svc.Add("2", "開発");
        svc.Add("3", "開発", activity: "設計");
        svc.Add("4", "営業");
        svc.Add("5", "開発", activity: "実装");
        svc.Add("6");

        Assert.True(svc.GroupOpenByProject());
        // 営業 → 開発（実装 → 設計 → なし）→ プロジェクトなし。同じ作業分類の中は元の並び
        Assert.Equal(["4", "1", "5", "3", "2", "6"], Titles(svc.OpenTasks()));
    }

    // ---------- 日報・月報 ----------

    [Fact]
    public void 日報は作業分類のあるプロジェクトだけ見出しを2段にする()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 9, 0));
        var design = svc.Add("画面設計", "開発", activity: "設計");
        var misc = svc.Add("雑務", "開発");
        var impl = svc.Add("API実装", "開発", activity: "実装");
        var sales = svc.Add("見積", "営業");

        Work(svc, clock, design, FakeClock.At(2026, 11, 6, 9, 0), TimeSpan.FromMinutes(60));
        Work(svc, clock, misc, FakeClock.At(2026, 11, 6, 10, 0), TimeSpan.FromMinutes(10));
        Work(svc, clock, impl, FakeClock.At(2026, 11, 6, 11, 0), TimeSpan.FromMinutes(30));
        Work(svc, clock, sales, FakeClock.At(2026, 11, 6, 13, 0), TimeSpan.FromMinutes(20));

        var md = ReportGenerator.Generate(svc, new DateOnly(2026, 11, 6));

        Assert.Contains(
            """
            ## 進行中
            ### 営業
            - 見積 — 0h20m（累計 0h20m）13:00〜13:20
            ### 開発
            #### 実装
            - API実装 — 0h30m（累計 0h30m）11:00〜11:30
            #### 設計
            - 画面設計 — 1h00m（累計 1h00m）09:00〜10:00
            #### その他
            - 雑務 — 0h10m（累計 0h10m）10:00〜10:10

            """.ReplaceLineEndings("\n"), md);
    }

    [Fact]
    public void 月報のプロジェクト別に作業分類の内訳を出す()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 9, 0));
        var design = svc.Add("画面設計", "開発", activity: "設計");
        var misc = svc.Add("雑務", "開発");
        var sales = svc.Add("見積", "営業");
        var other = svc.Add("問い合わせ");

        Work(svc, clock, design, FakeClock.At(2026, 11, 6, 9, 0), TimeSpan.FromMinutes(60));
        Work(svc, clock, misc, FakeClock.At(2026, 11, 6, 10, 0), TimeSpan.FromMinutes(10));
        Work(svc, clock, sales, FakeClock.At(2026, 11, 6, 13, 0), TimeSpan.FromMinutes(20));
        Work(svc, clock, other, FakeClock.At(2026, 11, 6, 14, 0), TimeSpan.FromMinutes(5));

        var md = ReportGenerator.GenerateMonthly(svc, new DateOnly(2026, 11, 6));

        Assert.Contains(
            """
            ## プロジェクト別
            - 営業 — 0h20m
            - 開発 — 1h10m
              - 設計 — 1h00m
              - その他 — 0h10m
            - その他 — 0h05m

            """.ReplaceLineEndings("\n"), md);
    }

    // ---------- 保存 ----------

    [Fact]
    public void 作業分類を保存して読み直せる_古いデータは作業分類なし()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hibi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(dir);
            var store = new TaskStore(dir);
            File.WriteAllText(store.DataPath, """
                {
                  "schemaVersion": 6,
                  "tasks": [ { "id": "11111111-1111-1111-1111-111111111111", "title": "A", "project": "開発" } ],
                  "days": {}
                }
                """);

            var data = store.Load();
            Assert.Equal(7, data.SchemaVersion);
            Assert.Null(data.Tasks[0].Activity);

            data.Tasks[0].Activity = "設計";
            store.Save(data);
            Assert.Equal("設計", store.Load().Tasks[0].Activity);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
