namespace Hibi.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void 保存して読み込むと同じ内容になり日本語はエスケープしない()
    {
        var store = new TaskStore(_dir);
        var (svc, clock) = Fixture.Create();
        var t = svc.Add("API修正", "backend", "メモ");
        svc.Start(t.Id);
        clock.Advance(TimeSpan.FromMinutes(5));
        svc.SetNote(svc.Today, "所感");

        store.Save(svc.Data);
        var json = File.ReadAllText(store.DataPath);
        var loaded = store.Load();

        Assert.Contains("\"title\": \"API修正\"", json);
        Assert.DoesNotContain("isRunning", json);
        Assert.False(File.Exists(store.DataPath + ".tmp"));
        var lt = Assert.Single(loaded.Tasks);
        Assert.Equal(t.Id, lt.Id);
        Assert.True(lt.IsRunning);
        Assert.Equal(t.WorkLogs[0].Start, lt.WorkLogs[0].Start);
        Assert.Equal("所感", loaded.Days["2026-11-06"].Note);
    }

    [Fact]
    public void ファイルがなければ空のデータ()
    {
        Assert.Empty(new TaskStore(_dir).Load().Tasks);
    }

    [Fact]
    public void 壊れたファイルは例外にして上書きしない()
    {
        var store = new TaskStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.DataPath, "{ broken");

        Assert.Throws<DataLoadException>(store.Load);
        Assert.Equal("{ broken", File.ReadAllText(store.DataPath));
    }

    [Fact]
    public void バックアップは7世代残す()
    {
        var store = new TaskStore(_dir);
        store.Save(new TaskData());

        for (var i = 0; i < 10; i++)
            store.Backup(FakeClock.At(2026, 11, 6, 9, 0, i));

        var files = Directory.GetFiles(store.BackupDirectory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(7, files.Count);
        Assert.Equal("tasks.20261106-090003.json", files[0]);
        Assert.Equal("tasks.20261106-090009.json", files[^1]);
    }

    [Fact]
    public void 設定ファイルがなければ既定値で作る()
    {
        var path = Path.Combine(_dir, "config.json");

        var config = AppConfig.LoadOrCreate(path);

        Assert.True(File.Exists(path));
        Assert.Equal(TimeSpan.FromHours(4), config.DayBoundaryTime);
        Assert.Contains("\"dayBoundary\": \"04:00\"", File.ReadAllText(path));
        Assert.DoesNotContain("%", config.DataDirectoryPath);
    }

    [Fact]
    public void 不正な設定は例外()
    {
        var path = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "dayBoundary": "25:00" }""");

        Assert.Throws<ConfigException>(() => AppConfig.LoadOrCreate(path));
    }

    [Fact]
    public void 日報は日付のファイル名で上書き保存する()
    {
        var day = new DateOnly(2026, 11, 6);
        ReportWriter.Write(_dir, day, "old");
        var path = ReportWriter.Write(_dir, day, "new");

        Assert.Equal(Path.Combine(_dir, "2026-11-06.md"), path);
        Assert.Equal("new", File.ReadAllText(path));
    }
}
