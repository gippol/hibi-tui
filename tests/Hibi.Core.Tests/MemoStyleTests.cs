namespace Hibi.Core.Tests;

public class MemoStyleTests
{
    /// <summary>1行を、同じ種類の続きごとに (文字, 種類) へまとめる。</summary>
    private static List<(string, MemoTone)> Runs(string line)
    {
        var tones = Assert.Single(MemoStyle.Classify(line));
        var runs = new List<(string, MemoTone)>();
        for (var i = 0; i < line.Length;)
        {
            var j = i;
            while (j < line.Length && tones[j] == tones[i]) j++;
            runs.Add((line[i..j], tones[i]));
            i = j;
        }
        return runs;
    }

    [Fact]
    public void 行ごとに文字の数だけ種類を返す()
    {
        var lines = MemoStyle.Classify("# 見出し\r\n本文\n\n- 項目");

        Assert.Equal([5, 2, 0, 4], lines.Select(l => l.Length));
        Assert.Empty(MemoStyle.Classify(null));
    }

    [Theory]
    [InlineData("# 見出し")]
    [InlineData("### 小見出し")]
    [InlineData("#　全角の空白")]
    public void 見出しは行ぜんぶ(string line)
    {
        Assert.Equal([(line, MemoTone.Heading)], Runs(line));
    }

    [Theory]
    [InlineData("#123 は課題番号")]
    [InlineData("####### 7個は見出しではない")]
    public void 見出しでない井桁はそのまま(string line)
    {
        Assert.Equal([(line, MemoTone.Normal)], Runs(line));
    }

    [Fact]
    public void 箇条書きと番号は印だけ()
    {
        Assert.Equal([("- ", MemoTone.Marker), ("項目", MemoTone.Normal)], Runs("- 項目"));
        Assert.Equal([("  ", MemoTone.Normal), ("* ", MemoTone.Marker), ("字下げ", MemoTone.Normal)], Runs("  * 字下げ"));
        Assert.Equal([("12. ", MemoTone.Marker), ("手順", MemoTone.Normal)], Runs("12. 手順"));
        Assert.Equal([("・", MemoTone.Marker), ("中黒", MemoTone.Normal)], Runs("・中黒"));
        Assert.Equal([("-1 は負の数", MemoTone.Normal)], Runs("-1 は負の数"));
    }

    [Fact]
    public void チェックリストは済んだものを行ごと控えめにする()
    {
        Assert.Equal([("- [ ] ", MemoTone.Marker), ("やること", MemoTone.Normal)], Runs("- [ ] やること"));
        Assert.Equal([("- [x] ", MemoTone.Marker), ("済んだこと", MemoTone.Done)], Runs("- [x] 済んだこと"));
        Assert.Equal([("- [X] ", MemoTone.Marker), ("**太字** も", MemoTone.Done)], Runs("- [X] **太字** も"));
    }

    [Fact]
    public void 引用と区切り線は控えめにする()
    {
        Assert.Equal([("> 引用", MemoTone.Muted)], Runs("> 引用"));
        Assert.Equal([("---", MemoTone.Muted)], Runs("---"));
        Assert.Equal([("* * *", MemoTone.Muted)], Runs("* * *"));
    }

    [Fact]
    public void 強調とコード()
    {
        Assert.Equal(
            [
                ("ここは ", MemoTone.Normal), ("**", MemoTone.Muted), ("大事", MemoTone.Bold), ("**", MemoTone.Muted),
                (" で ", MemoTone.Normal), ("`npm test`", MemoTone.Code), (" を流す", MemoTone.Normal),
            ],
            Runs("ここは **大事** で `npm test` を流す"));
        Assert.Equal([("対のない ** と ` はそのまま", MemoTone.Normal)], Runs("対のない ** と ` はそのまま"));
    }

    [Fact]
    public void 囲んだコードは閉じるまでコード()
    {
        var lines = MemoStyle.Classify("前\n```\n# 見出しではない\n```\n後");

        Assert.Equal(
            [MemoTone.Normal, MemoTone.Code, MemoTone.Code, MemoTone.Code, MemoTone.Normal],
            lines.Select(l => l[0]));
    }

    [Fact]
    public void 開けるリンクだけ色を付ける()
    {
        Assert.Equal(
            [("資料: ", MemoTone.Normal), ("https://example.com/a", MemoTone.Link), ("、", MemoTone.Normal), (@"C:\資料\設計.pdf", MemoTone.Link)],
            Runs(@"資料: https://example.com/a、C:\資料\設計.pdf"));
        Assert.Equal([("- ", MemoTone.Marker), ("https://example.com/", MemoTone.Link)], Runs("- https://example.com/"));
        Assert.Equal([("> ", MemoTone.Muted), ("https://example.com/", MemoTone.Link)], Runs("> https://example.com/"));
        Assert.Equal([("javascript:alert(1)", MemoTone.Normal)], Runs("javascript:alert(1)"));
    }
}
