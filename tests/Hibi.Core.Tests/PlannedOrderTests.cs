namespace Hibi.Core.Tests;

/// <summary>[今日の予定] の並べ替え（前の業務日の TomorrowTaskIds の順を変える）。</summary>
public class PlannedOrderTests
{
    private static string[] Titles(IEnumerable<TaskItem> ts) => ts.Select(t => t.Title).ToArray();

    /// <summary>11-06 に選んだ予定を、11-09（月）に今日の予定として見る。</summary>
    private static (TaskService Svc, TaskItem[] Tasks) Planned(params (string Title, string? Project)[] tasks)
    {
        var (svc, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 18, 0));
        var added = tasks.Select(t => svc.Add(t.Title, t.Project)).ToArray();
        svc.SetTomorrow(svc.Today, added.Select(t => t.Id));
        clock.Set(FakeClock.At(2026, 11, 9, 9, 0));
        return (svc, added);
    }

    [Fact]
    public void 上下に動かせて端では動かない()
    {
        var (svc, t) = Planned(("A", null), ("B", null), ("C", null));
        var changed = 0;
        svc.Changed += () => changed++;

        Assert.True(svc.MovePlanned(t[2].Id, -1));
        Assert.Equal(["A", "C", "B"], Titles(svc.PlannedToday()));
        Assert.True(svc.MovePlanned(t[0].Id, +1));
        Assert.Equal(["C", "A", "B"], Titles(svc.PlannedToday()));

        Assert.False(svc.MovePlanned(t[2].Id, -1));
        Assert.False(svc.MovePlanned(t[1].Id, +1));
        Assert.Equal(2, changed);
    }

    [Fact]
    public void 予定に入っていないタスクは動かさない()
    {
        var (svc, _) = Planned(("A", null), ("B", null));
        var other = svc.Add("X");
        Assert.False(svc.MovePlanned(other.Id, -1));
    }

    [Fact]
    public void 完了したものや絞り込みで見えないものは飛ばして隣と入れ替える()
    {
        var (svc, t) = Planned(("A", "x"), ("B", "y"), ("C", "x"), ("D", "x"));
        svc.Complete(t[1].Id);

        // C を上へ: 完了した B を飛ばして A の前へ
        Assert.True(svc.MovePlanned(t[2].Id, -1));
        Assert.Equal(["C", "A", "D"], Titles(svc.PlannedToday()));

        // A が見えていなければ、D を上へで C の前へ
        Assert.True(svc.MovePlanned(t[3].Id, -1, x => x.Title != "A"));
        Assert.Equal(["D", "C", "A"], Titles(svc.PlannedToday()));
    }

    [Fact]
    public void 並べ替えは選んだ日の記録に残り今日の記録は作らない()
    {
        var (svc, t) = Planned(("A", null), ("B", null));
        svc.MovePlanned(t[1].Id, -1);

        Assert.Equal([t[1].Id, t[0].Id], svc.GetDay(new DateOnly(2026, 11, 6))!.TomorrowTaskIds);
        Assert.Null(svc.GetDay(new DateOnly(2026, 11, 9)));
    }

    [Fact]
    public void プロジェクトごとにまとめられる()
    {
        var (svc, _) = Planned(("1", "web"), ("2", null), ("3", "api"), ("4", "web"));

        Assert.True(svc.GroupPlannedByProject());
        Assert.Equal(["3", "1", "4", "2"], Titles(svc.PlannedToday()));
        Assert.False(svc.GroupPlannedByProject());
    }

    [Fact]
    public void まとめるとき見えないものと完了したものの位置は変えない()
    {
        var (svc, t) = Planned(("A", "z"), ("B", "y"), ("C", "x"), ("D", "w"));
        svc.Complete(t[1].Id);

        Assert.True(svc.GroupPlannedByProject(x => x.Title != "C"));
        // A, D の席に w, z の順。C はそのまま、完了した B の id も選んだ日の記録の2番目に残る
        Assert.Equal(["D", "C", "A"], Titles(svc.PlannedToday()));
        Assert.Equal([t[3].Id, t[1].Id, t[2].Id, t[0].Id], svc.GetDay(new DateOnly(2026, 11, 6))!.TomorrowTaskIds);
    }

    [Fact]
    public void 予定がなければ何もしない()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A");
        Assert.False(svc.GroupPlannedByProject());
    }
}
