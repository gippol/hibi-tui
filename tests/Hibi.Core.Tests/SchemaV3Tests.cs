namespace Hibi.Core.Tests;

/// <summary>schemaVersion 3: 更新日時・手での保留・期限・優先度。</summary>
public class SchemaV3Tests
{
    // ---------- 編集も「触った」に数える ----------

    [Fact]
    public void 編集すると最後に触った日が更新されて保留から戻る()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var t = svc.Add("A");
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));
        Assert.True(svc.IsOnHold(t, 14));

        svc.Edit(t.Id, "A2", null, "メモを足した", null, TaskPriority.Normal);

        Assert.Equal(FakeClock.At(2026, 11, 6, 10, 0), t.UpdatedAt);
        Assert.Equal(new DateOnly(2026, 11, 6), svc.LastActivityDay(t));
        Assert.False(svc.IsOnHold(t, 14));
    }

    [Fact]
    public void 完了の取り消しも触ったに数える()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var t = svc.Add("A");
        svc.Complete(t.Id);
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));

        svc.Uncomplete(t.Id);

        Assert.False(svc.IsOnHold(t, 14));
    }

    // ---------- 手で保留にする・戻す ----------

    [Fact]
    public void 手で保留にすると日数に関係なく保留()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A");

        svc.SetOnHold(t.Id, true);

        Assert.True(t.OnHold);
        Assert.True(svc.IsOnHold(t, 14));
        Assert.True(svc.IsOnHold(t, 0)); // 自動の保留を使わなくても、手の保留は効く
        Assert.False(svc.IsStale(t, 14));
    }

    [Fact]
    public void 自動で保留になったものを戻すと触ったことになる()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 10, 1, 10, 0));
        var t = svc.Add("A");
        clock.Set(FakeClock.At(2026, 11, 6, 10, 0));
        Assert.True(svc.IsOnHold(t, 14));

        svc.SetOnHold(t.Id, false);

        Assert.False(svc.IsOnHold(t, 14));
    }

    [Fact]
    public void 手で保留にしたものはタイマーを始めると戻る()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A");
        svc.SetOnHold(t.Id, true);

        svc.Start(t.Id);

        Assert.False(t.OnHold);
        Assert.False(svc.IsOnHold(t, 14));
    }

    [Fact]
    public void 完了したタスクは保留にできない()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A");
        svc.Complete(t.Id);

        Assert.Throws<InvalidOperationException>(() => svc.SetOnHold(t.Id, true));
        Assert.False(svc.IsOnHold(t, 14));
    }

    [Fact]
    public void 手で保留にしたものは今日の予定に出さない()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 18, 0));
        var a = svc.Add("A");
        var b = svc.Add("B");
        svc.SetTomorrow(svc.Today, [a.Id, b.Id]);
        clock.Set(FakeClock.At(2026, 11, 7, 9, 0));

        svc.SetOnHold(a.Id, true);

        Assert.Equal([b], svc.PlannedToday());
    }

    // ---------- 期限・優先度 ----------

    [Fact]
    public void 期限と優先度を付けて追加と編集ができる()
    {
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A", due: new DateOnly(2026, 11, 10), priority: TaskPriority.High);

        Assert.Equal(new DateOnly(2026, 11, 10), t.Due);
        Assert.Equal(TaskPriority.High, t.Priority);

        svc.Edit(t.Id, "A", null, null, null, TaskPriority.Low);
        Assert.Null(t.Due);
        Assert.Equal(TaskPriority.Low, t.Priority);
    }

    [Fact]
    public void 優先度の既定は普通()
    {
        var (svc, _) = Fixture.Create();
        Assert.Equal(TaskPriority.Normal, svc.Add("A").Priority);
    }

    [Fact]
    public void 期限切れは今の業務日より前の期限で未完了のもの()
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 7, 2, 0)); // 11-06 の業務日
        var past = svc.Add("過去", due: new DateOnly(2026, 11, 5));
        var today = svc.Add("今日", due: new DateOnly(2026, 11, 6));
        var none = svc.Add("なし");
        var done = svc.Add("完了", due: new DateOnly(2026, 11, 1));
        svc.Complete(done.Id);

        Assert.True(svc.IsOverdue(past));
        Assert.False(svc.IsOverdue(today));
        Assert.False(svc.IsOverdue(none));
        Assert.False(svc.IsOverdue(done));
    }
}

public sealed class SchemaV3StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void 新しい項目を保存して読み込める()
    {
        var store = new TaskStore(_dir);
        var (svc, _) = Fixture.Create();
        var t = svc.Add("A", due: new DateOnly(2026, 11, 10), priority: TaskPriority.High);
        svc.SetOnHold(t.Id, true);

        store.Save(svc.Data);
        var json = File.ReadAllText(store.DataPath);
        var lt = Assert.Single(store.Load().Tasks);

        Assert.Contains($"\"schemaVersion\": {TaskData.CurrentSchemaVersion}", json);
        Assert.Contains("\"due\": \"2026-11-10\"", json);
        Assert.Contains("\"priority\": \"high\"", json);
        Assert.Contains("\"onHold\": true", json);
        Assert.Equal(new DateOnly(2026, 11, 10), lt.Due);
        Assert.Equal(TaskPriority.High, lt.Priority);
        Assert.True(lt.OnHold);
        Assert.Equal(t.UpdatedAt, lt.UpdatedAt);
    }

    [Fact]
    public void バージョン2のデータは既定値で読み込む()
    {
        Directory.CreateDirectory(_dir);
        var store = new TaskStore(_dir);
        File.WriteAllText(store.DataPath, """
            {
              "schemaVersion": 2,
              "tasks": [
                { "id": "00000000-0000-0000-0000-000000000001", "title": "A", "order": 0, "createdAt": "2026-11-05T10:00:00+09:00", "workLogs": [] }
              ],
              "days": {}
            }
            """);

        var data = store.Load();
        var t = Assert.Single(data.Tasks);

        Assert.Equal(TaskData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Null(t.UpdatedAt);
        Assert.False(t.OnHold);
        Assert.Null(t.Due);
        Assert.Equal(TaskPriority.Normal, t.Priority);
        Assert.Equal(0, t.Order); // v2 の並び順は振り直さない
    }
}
