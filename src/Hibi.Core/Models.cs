using System.Text.Json.Serialization;

namespace Hibi.Core;

/// <summary>tasks.json のルート。</summary>
public sealed class TaskData
{
    /// <summary>2: TaskItem.Order を追加。</summary>
    public const int CurrentSchemaVersion = 2;

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
    public List<Guid> TomorrowTaskIds { get; set; } = [];
}

public enum TaskState
{
    /// <summary>未完了（タイマー停止中）。作業記録の有無は問わない。</summary>
    Open,
    Running,
    Completed,
}
