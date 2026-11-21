namespace Hibi.Core.Tests;

public class ChangePriorityTests
{
    [Theory]
    [InlineData(TaskPriority.Low, 1, TaskPriority.Normal)]
    [InlineData(TaskPriority.Normal, 1, TaskPriority.High)]
    [InlineData(TaskPriority.High, -1, TaskPriority.Normal)]
    [InlineData(TaskPriority.Normal, -1, TaskPriority.Low)]
    public void 一段ずつ上げ下げする(TaskPriority from, int delta, TaskPriority expected)
    {
        var (service, _) = Fixture.Create();
        var t = service.Add("A", priority: from);

        Assert.True(service.ChangePriority(t.Id, delta));
        Assert.Equal(expected, t.Priority);
    }

    [Theory]
    [InlineData(TaskPriority.High, 1)]
    [InlineData(TaskPriority.Low, -1)]
    public void 端ではそれ以上動かさず_変更もしない(TaskPriority from, int delta)
    {
        var (service, clock) = Fixture.Create();
        var t = service.Add("A", priority: from);
        var updated = t.UpdatedAt;
        var changed = 0;
        service.Changed += () => changed++;
        clock.Advance(TimeSpan.FromHours(1));

        Assert.False(service.ChangePriority(t.Id, delta));
        Assert.Equal(from, t.Priority);
        Assert.Equal(updated, t.UpdatedAt);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void 変えたら触ったことにして保存する()
    {
        var (service, clock) = Fixture.Create();
        var t = service.Add("A");
        var changed = 0;
        service.Changed += () => changed++;
        clock.Advance(TimeSpan.FromHours(1));

        service.ChangePriority(t.Id, 1);

        Assert.Equal(service.Now, t.UpdatedAt);
        Assert.Equal(1, changed);
    }
}
