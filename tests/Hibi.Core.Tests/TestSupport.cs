namespace Hibi.Core.Tests;

/// <summary>JST 固定の操作可能な時計。</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    public static readonly TimeSpan Jst = TimeSpan.FromHours(9);
    private static readonly TimeZoneInfo JstZone = TimeZoneInfo.CreateCustomTimeZone("JST", Jst, "JST", "JST");

    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => JstZone;

    public void Set(DateTimeOffset t) => _now = t;
    public void Advance(TimeSpan d) => _now += d;

    public static DateTimeOffset At(int y, int mo, int d, int h, int mi, int s = 0) => new(y, mo, d, h, mi, s, Jst);
}

internal static class Fixture
{
    public static (TaskService Service, FakeClock Clock) Create(DateTimeOffset? now = null)
    {
        var clock = new FakeClock(now ?? FakeClock.At(2026, 11, 6, 9, 0));
        var service = new TaskService(new TaskData(), new BusinessDayCalendar(TimeSpan.FromHours(4)), clock);
        return (service, clock);
    }
}
