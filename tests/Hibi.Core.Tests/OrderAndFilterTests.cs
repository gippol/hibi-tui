namespace Hibi.Core.Tests;

public class OrderTests
{
    private static string[] Titles(IEnumerable<TaskItem> ts) => ts.Select(t => t.Title).ToArray();

    [Fact]
    public void 追加した順に並び新しいものは最後()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A");
        svc.Add("B");
        svc.Add("C");
        Assert.Equal(["A", "B", "C"], Titles(svc.OpenTasks()));
    }

    [Fact]
    public void 上下に動かせて端では動かない()
    {
        var (svc, _) = Fixture.Create();
        var a = svc.Add("A");
        svc.Add("B");
        var c = svc.Add("C");
        var changed = 0;
        svc.Changed += () => changed++;

        Assert.True(svc.MoveOpen(c.Id, -1));
        Assert.Equal(["A", "C", "B"], Titles(svc.OpenTasks()));
        Assert.True(svc.MoveOpen(a.Id, +1));
        Assert.Equal(["C", "A", "B"], Titles(svc.OpenTasks()));

        Assert.False(svc.MoveOpen(c.Id, -1));
        Assert.Equal(2, changed);
    }

    [Fact]
    public void 範囲を絞ると見えている隣と入れ替わる()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A", "x");
        svc.Add("B", "y");
        var c = svc.Add("C", "x");

        // y を見えていないものとして C を上へ → A の前に来る
        Assert.True(svc.MoveOpen(c.Id, -1, t => t.Project == "x"));
        Assert.Equal(["C", "A", "B"], Titles(svc.OpenTasks()));
    }

    [Fact]
    public void 完了したタスクは動かさない()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A");
        var b = svc.Add("B");
        svc.Complete(b.Id);
        Assert.False(svc.MoveOpen(b.Id, -1));
    }

    [Fact]
    public void 完了を取り消しても並び順は重ならない()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("A");
        var b = svc.Add("B");
        var c = svc.Add("C");
        svc.Complete(b.Id);
        svc.MoveOpen(c.Id, -1); // C, A
        svc.Uncomplete(b.Id);
        Assert.Equal(3, svc.OpenTasks().Select(t => t.Order).Distinct().Count());
    }
}

public class TaskFilterTests
{
    private static TaskItem T(string title, string? project = null) => new() { Title = title, Project = project };

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("api", true)]          // 大文字小文字を区別しない
    [InlineData("修正", true)]
    [InlineData("back", true)]         // プロジェクトにも当たる
    [InlineData("api 修正", true)]     // すべての語を含む
    [InlineData("api ログイン", false)]
    [InlineData("#back", true)]        // # はプロジェクトだけ
    [InlineData("#api", false)]
    [InlineData("#backend 修正", true)]
    public void タイトルとプロジェクトで絞り込む(string query, bool expected)
    {
        Assert.Equal(expected, TaskFilter.Matches(T("API修正", "backend"), query));
    }

    [Fact]
    public void プロジェクトなしは井桁の語に当たらない()
    {
        Assert.False(TaskFilter.Matches(T("API修正"), "#a"));
        Assert.True(TaskFilter.Matches(T("API修正"), "#"));
    }
}

public class ProjectListTests
{
    [Fact]
    public void 大文字小文字を区別せずに並ぶ()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("1", "Zeta");
        svc.Add("2", "alpha");
        svc.Add("3", "Beta");
        Assert.Equal(["alpha", "Beta", "Zeta"], svc.Projects());
    }

    [Fact]
    public void 大文字小文字だけ違う名前は別のプロジェクトで大文字が先()
    {
        var (svc, _) = Fixture.Create();
        svc.Add("1", "backend");
        svc.Add("2", "Backend");
        svc.Add("3", "backend");
        Assert.Equal(["Backend", "backend"], svc.Projects());
    }
}
