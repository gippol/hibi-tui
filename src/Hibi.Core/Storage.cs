using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hibi.Core;

public sealed class AppConfig
{
    public string DataDirectory { get; set; } = @"%APPDATA%\hibi";
    public string ReportDirectory { get; set; } = @"%USERPROFILE%\Documents\hibi\reports";
    public string DayBoundary { get; set; } = "04:00";
    public double ForgottenTimerWarningHours { get; set; } = 8;

    /// <summary>この業務日数のあいだ触っていない未完了タスクを [保留] にまとめる。0 なら使わない。</summary>
    public int StaleTaskDays { get; set; } = 14;

    [JsonIgnore] public string DataDirectoryPath => Expand(DataDirectory);
    [JsonIgnore] public string ReportDirectoryPath => Expand(ReportDirectory);

    [JsonIgnore]
    public TimeSpan DayBoundaryTime =>
        TimeSpan.TryParseExact(DayBoundary, @"hh\:mm", CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
            ? t
            : throw new ConfigException($"dayBoundary は HH:mm 形式で指定してください: \"{DayBoundary}\"");

    [JsonIgnore]
    public TimeSpan ForgottenTimerWarning =>
        ForgottenTimerWarningHours > 0
            ? TimeSpan.FromHours(ForgottenTimerWarningHours)
            : throw new ConfigException("forgottenTimerWarningHours は 0 より大きい値にしてください。");

    private static string Expand(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));

    /// <summary>設定ファイルの場所は固定（%APPDATA%\hibi\config.json）。</summary>
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hibi", "config.json");

    /// <summary>読み込む。なければ既定値で作る（6.3）。</summary>
    public static AppConfig LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var config = new AppConfig();
            AtomicFile.WriteAllText(path, Json.Serialize(config, Json.Context.AppConfig));
            return config;
        }
        try
        {
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), Json.Context.AppConfig)
                ?? throw new ConfigException("config.json が空です。");
            _ = loaded.DayBoundaryTime;          // 検証
            _ = loaded.ForgottenTimerWarning;    // 検証
            if (loaded.StaleTaskDays < 0)
                throw new ConfigException("staleTaskDays は 0 以上にしてください（0 なら保留を使わない）。");
            return loaded;
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"config.json を読めません: {ex.Message}", ex);
        }
    }
}

public sealed class ConfigException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class DataLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>tasks.json の読み書きとバックアップ（N-R-01〜04）。</summary>
public sealed class TaskStore(string dataDirectory)
{
    public const int BackupGenerations = 7;

    public string DataPath => Path.Combine(dataDirectory, "tasks.json");
    public string BackupDirectory => Path.Combine(dataDirectory, "backup");

    public TaskData Load()
    {
        if (!File.Exists(DataPath)) return new TaskData();
        try
        {
            var data = JsonSerializer.Deserialize(File.ReadAllText(DataPath), Json.Context.TaskData)
                ?? throw new DataLoadException("tasks.json が空です。");
            if (data.SchemaVersion > TaskData.CurrentSchemaVersion)
                throw new DataLoadException($"tasks.json のバージョン({data.SchemaVersion})にこのアプリは対応していません。");
            Migrate(data);
            return data;
        }
        catch (JsonException ex)
        {
            throw new DataLoadException($"tasks.json を読めません: {ex.Message}", ex);
        }
    }

    /// <summary>古い形のデータを今の形に直す。ファイルには次に保存したときに書かれる。</summary>
    private static void Migrate(TaskData data)
    {
        if (data.SchemaVersion < 2)
        {
            // 1 → 2: 並び順を作成順で振る
            var i = 0;
            foreach (var t in data.Tasks.OrderBy(t => t.CreatedAt)) t.Order = i++;
        }
        if (data.SchemaVersion < 4)
        {
            // 3 → 4: 選んだかの印はなかったので、予定が空でない日だけ選んだことにする
            // （何も選ばずに出力した日は区別できないので、選んでいない扱い）
            foreach (var day in data.Days.Values) day.TomorrowChosen = day.TomorrowTaskIds.Count > 0;
        }
        data.SchemaVersion = TaskData.CurrentSchemaVersion;
    }

    public void Save(TaskData data) =>
        AtomicFile.WriteAllText(DataPath, Json.Serialize(data, Json.Context.TaskData));

    /// <summary>tasks.json をバックアップし、古いものは <see cref="BackupGenerations"/> 世代を残して消す。</summary>
    public void Backup(DateTimeOffset now)
    {
        if (!File.Exists(DataPath)) return;
        Directory.CreateDirectory(BackupDirectory);
        var name = $"tasks.{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json";
        File.Copy(DataPath, Path.Combine(BackupDirectory, name), overwrite: true);

        var old = Directory.GetFiles(BackupDirectory, "tasks.*.json")
            .Order(StringComparer.Ordinal)
            .SkipLast(BackupGenerations);
        foreach (var f in old) File.Delete(f);
    }
}

public static class ReportWriter
{
    /// <summary>日報: &lt;日報フォルダ&gt;\YYYY-MM-DD.md</summary>
    public static string Write(string reportDirectory, DateOnly day, string markdown) =>
        WriteFile(reportDirectory, BusinessDayCalendar.Key(day), markdown);

    /// <summary>月報: &lt;日報フォルダ&gt;\YYYY-MM.md</summary>
    public static string WriteMonthly(string reportDirectory, DateOnly month, string markdown) =>
        WriteFile(reportDirectory, ReportPeriod.MonthOf(month).Name, markdown);

    /// <summary>週報: &lt;日報フォルダ&gt;\YYYY-Www.md（ISO 週番号）</summary>
    public static string WriteWeekly(string reportDirectory, ReportPeriod week, string markdown) =>
        WriteFile(reportDirectory, week.Name, markdown);

    private static string WriteFile(string reportDirectory, string name, string markdown)
    {
        var path = Path.Combine(reportDirectory, $"{name}.md");
        AtomicFile.WriteAllText(path, markdown);
        return path;
    }
}

public static class AtomicFile
{
    /// <summary>一時ファイルに書いてから置き換える。途中で落ちても元のファイルは壊れない。</summary>
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
        {
            w.Write(content);
            w.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}

internal static class Json
{
    public static readonly AppJsonContext Context = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 日本語をエスケープせずに書く（人が読めるように）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static string Serialize<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.Serialize(value, info) + "\n";
}

[JsonSerializable(typeof(TaskData))]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
