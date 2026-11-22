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
        Assert.Equal(14, config.StaleTaskDays);
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
    public void 保留までの日数は0以上()
    {
        var path = Path.Combine(_dir, "config.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "staleTaskDays": -1 }""");

        Assert.Throws<ConfigException>(() => AppConfig.LoadOrCreate(path));
    }

    [Fact]
    public void 画面で変えた設定を保存すると次に読み込める()
    {
        var path = Path.Combine(_dir, "config.json");
        var config = AppConfig.LoadOrCreate(path).Clone();
        config.ReportDirectory = @"%USERPROFILE%\日報";
        config.DayBoundary = "05:30";
        config.ForgottenTimerWarningHours = 1.5;
        config.StaleTaskDays = 0;

        config.Save(path);
        var loaded = AppConfig.LoadOrCreate(path);

        Assert.Equal(@"%USERPROFILE%\日報", loaded.ReportDirectory);
        Assert.Contains(@"%USERPROFILE%\\日報", File.ReadAllText(path)); // 環境変数は展開せず、日本語もそのまま書く
        Assert.Equal(new TimeSpan(5, 30, 0), loaded.DayBoundaryTime);
        Assert.Equal(TimeSpan.FromMinutes(90), loaded.ForgottenTimerWarning);
        Assert.Equal(0, loaded.StaleTaskDays);
    }

    [Theory]
    [InlineData("dayBoundary", "24:00")]
    [InlineData("dayBoundary", "4時")]
    [InlineData("forgottenTimerWarningHours", "0")]
    [InlineData("staleTaskDays", "-1")]
    [InlineData("dataDirectory", "")]
    [InlineData("reportDirectory", "  ")]
    public void おかしな設定は保存しない(string name, string value)
    {
        var path = Path.Combine(_dir, "config.json");
        var config = AppConfig.LoadOrCreate(path).Clone();
        var before = File.ReadAllText(path);
        switch (name)
        {
            case "dayBoundary": config.DayBoundary = value; break;
            case "forgottenTimerWarningHours": config.ForgottenTimerWarningHours = double.Parse(value); break;
            case "staleTaskDays": config.StaleTaskDays = int.Parse(value); break;
            case "dataDirectory": config.DataDirectory = value; break;
            case "reportDirectory": config.ReportDirectory = value; break;
        }

        Assert.Throws<ConfigException>(() => config.Save(path));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void 写しを変えても元の設定は変わらない()
    {
        var config = new AppConfig();
        var copy = config.Clone();
        copy.StaleTaskDays = 3;

        Assert.Equal(14, config.StaleTaskDays);
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

public sealed class SchemaMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void バージョン1のデータは作成順に並び順を振る()
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, """
            {
              "schemaVersion": 1,
              "tasks": [
                { "id": "00000000-0000-0000-0000-000000000002", "title": "B", "createdAt": "2026-11-06T10:00:00+09:00", "workLogs": [] },
                { "id": "00000000-0000-0000-0000-000000000001", "title": "A", "createdAt": "2026-11-05T10:00:00+09:00", "workLogs": [] }
              ],
              "days": {}
            }
            """);

        var data = store.Load();

        Assert.Equal(TaskData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(1, data.Tasks.Single(t => t.Title == "B").Order);
        Assert.Equal(0, data.Tasks.Single(t => t.Title == "A").Order);
    }
}
