namespace Hibi.Core.Tests;

public class WorkLogTextTests
{
    private static TimeSpan Jst(DateTime _) => FakeClock.Jst;

    [Fact]
    public void 書式化して読み戻すと同じになる()
    {
        WorkLog[] logs =
        [
            new() { Start = FakeClock.At(2026, 11, 6, 10, 0), End = FakeClock.At(2026, 11, 6, 12, 30) },
            new() { Start = FakeClock.At(2026, 11, 6, 23, 0), End = FakeClock.At(2026, 11, 7, 1, 0) },
            new() { Start = FakeClock.At(2026, 11, 7, 13, 0) },
        ];

        var text = WorkLogText.Format(logs);
        var parsed = WorkLogText.Parse(text, Jst);

        Assert.Equal("2026-11-06 10:00 - 12:30\n2026-11-06 23:00 - 2026-11-07 01:00\n2026-11-07 13:00 - \n", text);
        Assert.Equal(logs.Select(l => (l.Start, l.End)), parsed.Select(l => (l.Start, l.End)));
    }

    [Fact]
    public void 終了が開始より前の時刻なら翌日扱いで空行とコメントは無視する()
    {
        var parsed = WorkLogText.Parse("# コメント\n\n2026-11-06 23:30 - 0:15\n", Jst);

        var l = Assert.Single(parsed);
        Assert.Equal(FakeClock.At(2026, 11, 7, 0, 15), l.End);
    }

    [Theory]
    [InlineData("2026-11-06 10:00")]
    [InlineData("2026-13-06 10:00 - 11:00")]
    [InlineData("2026-11-06 10:00 - 25:00")]
    [InlineData("2026-11-06 10:00 - 9:30")] // 翌日扱いだと23.5時間になる打ち間違い
    public void 不正な行は行番号付きで例外(string line)
    {
        var ex = Assert.Throws<FormatException>(() => WorkLogText.Parse("\n" + line, Jst));
        Assert.StartsWith("2行目", ex.Message);
    }
}
