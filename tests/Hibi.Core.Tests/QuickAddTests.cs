namespace Hibi.Core.Tests;

public class QuickAddTests
{
    private static readonly DateOnly Today = new(2026, 11, 21);

    private static QuickAdd.Result Parse(string text) => QuickAdd.Parse(text, Today);

    [Fact]
    public void 記法がなければタイトルをそのまま返す()
    {
        var r = Parse("資料を作る　午前中に");

        Assert.Equal("資料を作る　午前中に", r.Title);
        Assert.Null(r.Project);
        Assert.Null(r.Due);
        Assert.Null(r.Priority);
    }

    [Fact]
    public void プロジェクト_優先度_期限をまとめて取り出す()
    {
        var r = Parse("資料作成 #営業 !高 @11-25");

        Assert.Equal("資料作成", r.Title);
        Assert.Equal("営業", r.Project);
        Assert.Equal(TaskPriority.High, r.Priority);
        Assert.Equal(new DateOnly(2026, 11, 25), r.Due);
    }

    [Fact]
    public void 記法はタイトルのどこに書いてもいい_全角スペースでも区切る()
    {
        var r = Parse("#開発　レビュー　する !低");

        Assert.Equal("レビュー する", r.Title);
        Assert.Equal("開発", r.Project);
        Assert.Equal(TaskPriority.Low, r.Priority);
    }

    [Theory]
    [InlineData("!", TaskPriority.High)]
    [InlineData("!高", TaskPriority.High)]
    [InlineData("!普通", TaskPriority.Normal)]
    [InlineData("!低", TaskPriority.Low)]
    public void 優先度(string token, TaskPriority expected) =>
        Assert.Equal(expected, Parse($"作業 {token}").Priority);

    [Theory]
    [InlineData("@今日", 2026, 11, 21)]
    [InlineData("@明日", 2026, 11, 22)]
    [InlineData("@2027-1-5", 2027, 1, 5)]
    [InlineData("@12-01", 2026, 12, 1)]
    [InlineData("@12/1", 2026, 12, 1)]
    [InlineData("@11-21", 2026, 11, 21)] // 今日は今年のまま
    [InlineData("@1-10", 2027, 1, 10)]   // 月日だけで今日より前なら来年
    public void 期限(string token, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Parse($"作業 {token}").Due);

    [Theory]
    [InlineData("Issue #123 を直す")]   // # の後ろが数字だけなら番号
    [InlineData("@田中 さんに連絡")]    // 日付でない @ は名前など
    [InlineData("# だけ")]
    [InlineData("!! 至急")]
    [InlineData("@2-30 存在しない日")]
    [InlineData("@1-2-3 年が4桁でない")]
    [InlineData("@99999-1 桁が多い")]
    public void 当てはまらない語はタイトルに残す(string text)
    {
        var r = Parse(text);

        Assert.Equal(text, r.Title);
        Assert.Null(r.Project);
        Assert.Null(r.Due);
        Assert.Null(r.Priority);
    }

    [Fact]
    public void 同じ種類を2回書いたら後のほうを使う()
    {
        var r = Parse("作業 #A #B");

        Assert.Equal("作業", r.Title);
        Assert.Equal("B", r.Project);
    }

    [Fact]
    public void 記法だけならタイトルは空になる()
    {
        Assert.Equal("", Parse("#営業 !").Title);
    }
}
