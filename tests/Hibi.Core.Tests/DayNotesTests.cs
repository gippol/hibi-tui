namespace Hibi.Core.Tests;

/// <summary>schemaVersion 6: ふり返り（課題・学び・所感）。</summary>
public sealed class DayNotesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void 課題と学びは明日の予定の前に所感は最後に出す()
    {
        var (svc, _) = Fixture.Create();
        var retry = svc.Add("リトライ処理");
        svc.SetTomorrow(svc.Today, [retry.Id]);
        svc.SetNotes(svc.Today, new DayNotes(
            "仕様書が古く、\r\nデバッグに時間がかかった。",
            "早い段階でモックで疎通確認する。",
            "おつかれさま"));

        var md = ReportGenerator.Generate(svc, svc.Today);

        Assert.EndsWith(
            """
            ## 課題・困ったこと
            仕様書が古く、
            デバッグに時間がかかった。

            ## 学び・改善
            早い段階でモックで疎通確認する。

            ## 明日の予定
            - リトライ処理

            ## 所感
            おつかれさま

            """.ReplaceLineEndings("\n"),
            md);
    }

    [Fact]
    public void 書かなかった欄は出さない()
    {
        var (svc, _) = Fixture.Create();
        svc.SetNotes(svc.Today, new DayNotes(null, "  学び  ", "   "));

        var md = ReportGenerator.Generate(svc, svc.Today);

        Assert.DoesNotContain("## 課題・困ったこと", md);
        Assert.Contains("## 学び・改善\n学び\n", md);
        Assert.DoesNotContain("## 所感", md);
    }

    [Fact]
    public void 空白だけの欄はnullで保存する()
    {
        var (svc, _) = Fixture.Create();
        svc.SetNotes(svc.Today, new DayNotes(" 課題 ", "\r\n", ""));

        Assert.Equal(new DayNotes("課題", null, null), svc.GetNotes(svc.Today));
    }

    [Fact]
    public void 記録のない日のふり返りは全部null()
    {
        var (svc, _) = Fixture.Create();

        Assert.Equal(DayNotes.Empty, svc.GetNotes(svc.Today));
    }

    [Fact]
    public void 週報は日ごとに課題と学びを集める()
    {
        var (svc, _) = Fixture.Create();
        svc.SetNotes(new DateOnly(2026, 11, 3), new DayNotes("火の課題", null, null));
        svc.SetNotes(new DateOnly(2026, 11, 5), new DayNotes("木の課題", "木の学び", null));
        svc.SetNotes(new DateOnly(2026, 11, 1), new DayNotes("前の週の課題", null, null));

        var md = ReportGenerator.GenerateWeekly(svc, new DateOnly(2026, 11, 6));

        Assert.EndsWith(
            """
            ## 日別
            なし

            ## 課題・困ったこと
            ### 11-03（火）
            火の課題

            ### 11-05（木）
            木の課題

            ## 学び・改善
            ### 11-05（木）
            木の学び

            """.ReplaceLineEndings("\n"),
            md);
        Assert.DoesNotContain("前の週の課題", md);
    }

    [Fact]
    public void 課題と学びを保存して読み込める()
    {
        var store = new TaskStore(_dir);
        var (svc, _) = Fixture.Create();
        svc.SetNotes(svc.Today, new DayNotes("課題", "学び", "所感"));

        store.Save(svc.Data);
        var json = File.ReadAllText(store.DataPath);
        var loaded = store.Load();

        Assert.Contains("\"schemaVersion\": 6", json);
        Assert.Contains("\"problem\": \"課題\"", json);
        Assert.Contains("\"try\": \"学び\"", json);
        var day = loaded.Days["2026-11-06"];
        Assert.Equal(("課題", "学び", "所感"), (day.Problem, day.Try, day.Note));
    }

    [Fact]
    public void バージョン5のデータは課題と学びなしで読む()
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, """
            {
              "schemaVersion": 5,
              "tasks": [],
              "days": {
                "2026-11-06": { "note": "所感", "tomorrowTaskIds": null }
              }
            }
            """);

        var data = store.Load();

        Assert.Equal(6, data.SchemaVersion);
        var day = data.Days["2026-11-06"];
        Assert.Null(day.Problem);
        Assert.Null(day.Try);
        Assert.Equal("所感", day.Note);
    }
}
