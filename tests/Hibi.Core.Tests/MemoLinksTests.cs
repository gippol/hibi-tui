namespace Hibi.Core.Tests;

public class MemoLinksTests
{
    private static List<string> Targets(string memo) => MemoLinks.Extract(memo).Select(l => l.Target).ToList();

    [Fact]
    public void メモがなければ何も拾わない()
    {
        Assert.Empty(MemoLinks.Extract(null));
        Assert.Empty(MemoLinks.Extract(""));
        Assert.Empty(MemoLinks.Extract("リンクのない長い文章です。\nhttp という語はあるけれど URL ではない。"));
    }

    [Fact]
    public void URLとパスを出てくる順に拾う()
    {
        var links = MemoLinks.Extract(
            "仕様書: https://example.com/spec?id=1#top\n" +
            "共有: \\\\fileserver\\share\\見積.xlsx\n" +
            "手元: C:\\資料\\設計.pdf");

        Assert.Equal(
            [
                (MemoLinks.Kind.Url, "https://example.com/spec?id=1#top"),
                (MemoLinks.Kind.Path, @"\\fileserver\share\見積.xlsx"),
                (MemoLinks.Kind.Path, @"C:\資料\設計.pdf"),
            ],
            links.Select(l => (l.Kind, l.Target)));
    }

    [Fact]
    public void 文の句読点や閉じかっこはリンクに含めない()
    {
        Assert.Equal(
            ["https://example.com/a", "https://example.com/b", "https://example.com/c", "https://en.wikipedia.org/wiki/Foo_(bar)"],
            Targets("見て https://example.com/a. (https://example.com/b) 「https://example.com/c」、 https://en.wikipedia.org/wiki/Foo_(bar)"));
    }

    [Fact]
    public void 空白を含むパスは引用符で囲めば拾える()
    {
        Assert.Equal([@"C:\My Documents\週次 報告.docx"], Targets("ここ → \"C:\\My Documents\\週次 報告.docx\" を見る"));
    }

    [Fact]
    public void 同じリンクは1回だけ()
    {
        Assert.Single(MemoLinks.Extract("https://example.com/ と https://example.com/ と HTTPS://EXAMPLE.COM/"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("ftp://example.com/a")]
    [InlineData("abchttps://example.com/")]
    [InlineData("https://")]
    public void httpとhttps以外や語の途中からは拾わない(string memo)
    {
        Assert.Empty(MemoLinks.Extract(memo));
    }

    [Fact]
    public void ユーザー名で行き先をごまかしたURLは拾わない()
    {
        Assert.Empty(MemoLinks.Extract("https://www.example.co.jp@evil.example/login"));
    }

    [Theory]
    [InlineData("https://example.com/\u202Efdp.exe")] // 右から左へ向きを変える文字
    [InlineData("https://exa\u200Bmple.com/")] // 幅のない空白
    [InlineData("C:\\資料\\\u202Excod.exe")]
    public void 見た目をごまかす文字を含むものは拾わない(string memo)
    {
        Assert.Empty(MemoLinks.Extract(memo));
    }

    [Theory]
    [InlineData(@"C:\資料\a.txt:hidden")] // 代替データストリーム
    [InlineData(@"C:\資料\*.xlsx")]
    [InlineData(@"\\?\C:\Windows\System32\cmd.exe")]
    [InlineData(@"\\.\PhysicalDrive0")]
    public void 普通でない形のパスは拾わない(string memo)
    {
        Assert.Empty(MemoLinks.Extract(memo));
    }

    [Theory]
    [InlineData(@"C:\資料\設計.pdf", true)]
    [InlineData(@"C:\資料\見積.XLSX", true)]
    [InlineData(@"C:\資料\メモ.txt", true)]
    [InlineData(@"C:\tools\setup.exe", false)]
    [InlineData(@"C:\tools\run.bat", false)]
    [InlineData(@"C:\tools\run.ps1", false)]
    [InlineData(@"C:\tools\ショートカット.lnk", false)]
    [InlineData(@"C:\資料\マクロ.xlsm", false)]
    [InlineData(@"C:\資料\page.html", false)]
    [InlineData(@"C:\資料\setup.exe.", false)]
    [InlineData(@"C:\資料\拡張子なし", false)]
    public void そのまま開くのは許可した拡張子だけ(string path, bool expected)
    {
        Assert.Equal(expected, MemoLinks.CanOpenFileDirectly(path));
    }

    [Fact]
    public void ネットワーク上の場所を見分ける()
    {
        Assert.True(MemoLinks.IsNetworkPath(@"\\fileserver\share\a.pdf"));
        Assert.False(MemoLinks.IsNetworkPath(@"C:\資料\a.pdf"));
    }

    [Fact]
    public void 国際化ドメインはASCIIの形も添える()
    {
        var link = Assert.Single(MemoLinks.Extract("https://例え.jp/"));

        Assert.Equal("example.com", MemoLinks.HostOf("https://example.com/a"));
        Assert.Contains("xn--", MemoLinks.HostOf(link.Target));
    }
}
