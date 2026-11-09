namespace Hibi.Core.Tests;

/// <summary>schemaVersion 4: 明日の予定を「選んだ」かを、リストが空かどうかと別に持つ。</summary>
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
}

public sealed class SchemaV4StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void 選んだ印を保存して読み込める()
    {
        var store = new TaskStore(_dir);
        var (svc, _) = Fixture.Create();
        svc.SetTomorrow(svc.Today, []);

        store.Save(svc.Data);
        var json = File.ReadAllText(store.DataPath);
        var day = store.Load().Days[BusinessDayCalendar.Key(svc.Today)];

        Assert.Contains("\"schemaVersion\": 4", json);
        Assert.Contains("\"tomorrowChosen\": true", json);
        Assert.True(day.TomorrowChosen);
    }

    [Fact]
    public void バージョン3のデータは予定が空でない日だけ選んだことにする()
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, """
            {
              "schemaVersion": 3,
              "tasks": [],
              "days": {
                "2026-11-05": { "note": null, "tomorrowTaskIds": ["00000000-0000-0000-0000-000000000001"] },
                "2026-11-06": { "note": "所感だけ", "tomorrowTaskIds": [] }
              }
            }
            """);

        var data = store.Load();

        Assert.Equal(4, data.SchemaVersion);
        Assert.True(data.Days["2026-11-05"].TomorrowChosen);
        Assert.False(data.Days["2026-11-06"].TomorrowChosen);
    }

    [Fact]
    public void バージョン4のデータは選んだ印をそのまま使う()
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, """
            {
              "schemaVersion": 4,
              "tasks": [],
              "days": {
                "2026-11-06": { "note": null, "tomorrowTaskIds": [], "tomorrowChosen": true }
              }
            }
            """);

        Assert.True(store.Load().Days["2026-11-06"].TomorrowChosen);
    }
}
