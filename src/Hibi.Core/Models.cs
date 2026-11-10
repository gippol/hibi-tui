using System.Text.Json.Serialization;

namespace Hibi.Core;

/// <summary>tasks.json のルート。</summary>
public sealed class TaskData
{
    /// <summary>
    /// 2: TaskItem.Order を追加。
    /// 3: TaskItem.UpdatedAt / OnHold / Due / Priority を追加（どれも省略時は既定値）。
    /// 4: DayRecord.TomorrowChosen を追加。
    /// 5: TomorrowChosen をやめ、DayRecord.TomorrowTaskIds が null なら「選んでいない」にした。
    /// </summary>
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<TaskItem> Tasks { get; set; } = [];

    /// <summary>業務日（yyyy-MM-dd）ごとの記録。</summary>
    public Dictionary<string, DayRecord> Days { get; set; } = [];
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Project { get; set; }
    public string? Memo { get; set; }

    /// <summary>一覧での並び順（小さいほど上）。すべてのタスクで通しの番号。</summary>
    public int Order { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最後に手で変えた日時（編集・保留の切り替え・完了の取り消し）。null なら作ってから変えていない。</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>手で保留にしたか。日数による自動の保留とは別。</summary>
    public bool OnHold { get; set; }

    /// <summary>期限（業務日）。null なら期限なし。</summary>
    public DateOnly? Due { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Normal;

    public DateTimeOffset? CompletedAt { get; set; }
    public List<WorkLog> WorkLogs { get; set; } = [];

    [JsonIgnore]
    public bool IsCompleted => CompletedAt is not null;
    [JsonIgnore]
    public bool IsRunning => WorkLogs.Any(l => l.End is null);

    [JsonIgnore]
    public TaskState State =>
        IsCompleted ? TaskState.Completed
        : IsRunning ? TaskState.Running
        : TaskState.Open;
}

public sealed class WorkLog
{
    public DateTimeOffset Start { get; set; }

    /// <summary>null なら計測中。</summary>
    public DateTimeOffset? End { get; set; }

    public TimeSpan DurationAt(DateTimeOffset now)
    {
        var d = (End ?? now) - Start;
        return d < TimeSpan.Zero ? TimeSpan.Zero : d;
    }
}

public sealed class DayRecord
{
    public string? Note { get; set; }

    /// <summary>
    /// この日に選んだ明日の予定（日報を出力したときに選ぶ）。null なら選んでいない（所感だけ書いた日など）、
    /// 空なら「何も選ばなかった」。
    /// </summary>
    public List<Guid>? TomorrowTaskIds { get; set; }

    /// <summary>schemaVersion 4 の tomorrowChosen。読み込んで移行するためだけに持つ。</summary>
    [JsonInclude, JsonPropertyName("tomorrowChosen"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal bool? LegacyTomorrowChosen { get; set; }
}

/// <summary>優先度。JSON には小文字の名前で書く。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))]
public enum TaskPriority
{
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("low")] Low,
}

public enum TaskState
{
    /// <summary>未完了（タイマー停止中）。作業記録の有無は問わない。</summary>
    Open,
    Running,
    Completed,
}
