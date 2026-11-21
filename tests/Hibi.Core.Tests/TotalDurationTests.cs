namespace Hibi.Core.Tests;

public class TotalDurationTests
{
    [Fact]
    public void その日の作業記録をすべてのタスクで足す_計測中も今までの分を入れる()
    {
        var (service, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 9, 0));
        var a = service.Add("A");
        var b = service.Add("B");

        service.Toggle(a.Id);
        clock.Advance(TimeSpan.FromHours(3));
        service.Toggle(b.Id); // A は止まる
        clock.Advance(TimeSpan.FromMinutes(90));

        Assert.Equal(TimeSpan.FromHours(4.5), service.TotalDurationOn(service.Today));
    }

    [Fact]
    public void 別の業務日の作業は入れない()
    {
        var (service, clock) = Fixture.Create(FakeClock.At(2026, 11, 6, 9, 0));
        var a = service.Add("A");
        service.Toggle(a.Id);
        clock.Advance(TimeSpan.FromHours(2));
        service.Toggle(a.Id);

        clock.Set(FakeClock.At(2026, 11, 7, 9, 0));

        Assert.Equal(TimeSpan.Zero, service.TotalDurationOn(service.Today));
        Assert.Equal(TimeSpan.FromHours(2), service.TotalDurationOn(new DateOnly(2026, 11, 6)));
    }
}
