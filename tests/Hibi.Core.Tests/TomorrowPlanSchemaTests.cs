namespace Hibi.Core.Tests;

/// <summary>明日の予定を「選んだ」かを、リストが空かどうかと別に扱う（schemaVersion 4 から）。</summary>
public class SchemaV4Tests
{
    [Fact]
    public void 予定のタスクを消してもさらに前の日の予定は出ない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0)); // 木
        var old = svc.Add("木曜に選んだ");
        svc.SetTomorrow(svc.Today, [old.Id]);
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0)); // 金
        var a = svc.Add("金曜に選んだ");
        svc.SetTomorrow(svc.Today, [a.Id]);

        clock.Set(FakeClock.At(2026, 11, 9, 9, 0)); // 月
        svc.Delete(a.Id);

        Assert.Empty(svc.PlannedToday());
    }

    [Fact]
    public void 何も選ばずに出力した日のあとはさらに前の日の予定は出ない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0));
        var old = svc.Add("木曜に選んだ");
        svc.SetTomorrow(svc.Today, [old.Id]);
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0));
        svc.SetTomorrow(svc.Today, []);

        clock.Set(FakeClock.At(2026, 11, 9, 9, 0));

        Assert.Empty(svc.PlannedToday());
    }

    [Fact]
    public void 所感だけ書いた日は予定を選んだ日に数えない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0));
        var a = svc.Add("A");
        svc.SetTomorrow(svc.Today, [a.Id]);
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0));
        svc.SetNote(svc.Today, "所感だけ");

        clock.Set(FakeClock.At(2026, 11, 9, 9, 0));

        Assert.Equal([a], svc.PlannedToday());
    }

    [Fact]
    public void 選んだ予定を取り消すと前の日の予定に戻る()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0));
        var a = svc.Add("A");
        svc.SetTomorrow(svc.Today, [a.Id]);
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0));
        svc.SetTomorrow(svc.Today, []);
        svc.SetTomorrow(svc.Today, null); // 日報を出せなかったので元に戻した

        clock.Set(FakeClock.At(2026, 11, 9, 9, 0));

        Assert.Equal([a], svc.PlannedToday());
    }
}

/// <summary>schemaVersion 5: 選んでいない日は tomorrowTaskIds が null。</summary>
public sealed class SchemaV5StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private TaskData LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, json);
        return store.Load();
    }

    [Fact]
    public void 何も選ばなかった日と選んでいない日を保存して読み込める()
    {
        var store = new TaskStore(_dir);
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 5, 18, 0));
        svc.SetTomorrow(svc.Today, []);
        clock.Set(FakeClock.At(2026, 11, 6, 18, 0));
        svc.SetNote(svc.Today, "所感だけ");

        store.Save(svc.Data);
        var json = File.ReadAllText(store.DataPath);
        var days = store.Load().Days;

        Assert.Contains("\"schemaVersion\": 5", json);
        Assert.DoesNotContain("tomorrowChosen", json);
        Assert.Empty(days["2026-11-05"].TomorrowTaskIds!);
        Assert.Null(days["2026-11-06"].TomorrowTaskIds);
    }

    [Fact]
    public void バージョン3のデータは所感があって予定が空の日だけ選んでいない扱い()
    {
        var data = LoadJson("""
            {
              "schemaVersion": 3,
              "tasks": [],
              "days": {
                "2026-11-04": { "note": "所感と予定", "tomorrowTaskIds": ["00000000-0000-0000-0000-000000000001"] },
                "2026-11-05": { "note": null, "tomorrowTaskIds": [] },
                "2026-11-06": { "note": "所感だけ", "tomorrowTaskIds": [] }
              }
            }
            """);

        Assert.Equal(5, data.SchemaVersion);
        Assert.Single(data.Days["2026-11-04"].TomorrowTaskIds!);
        Assert.Empty(data.Days["2026-11-05"].TomorrowTaskIds!); // 何も選ばずに出力した日
        Assert.Null(data.Days["2026-11-06"].TomorrowTaskIds);
    }

    [Fact]
    public void バージョン4のデータは選んだ印を移す()
    {
        var data = LoadJson("""
            {
              "schemaVersion": 4,
              "tasks": [],
              "days": {
                "2026-11-03": { "note": "所感", "tomorrowTaskIds": [], "tomorrowChosen": true },
                "2026-11-04": { "note": "所感だけ", "tomorrowTaskIds": [], "tomorrowChosen": false },
                "2026-11-05": { "note": null, "tomorrowTaskIds": [], "tomorrowChosen": false },
                "2026-11-06": { "note": "手で書いた", "tomorrowTaskIds": ["00000000-0000-0000-0000-000000000001"] }
              }
            }
            """);

        Assert.Empty(data.Days["2026-11-03"].TomorrowTaskIds!);
        Assert.Null(data.Days["2026-11-04"].TomorrowTaskIds);
        Assert.Empty(data.Days["2026-11-05"].TomorrowTaskIds!); // v3 から移したときに落としていた日
        Assert.Single(data.Days["2026-11-06"].TomorrowTaskIds!);
    }
}
