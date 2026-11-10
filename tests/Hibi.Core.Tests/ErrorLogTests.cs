namespace Hibi.Core.Tests;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hibi-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Exception Thrown()
    {
        try { throw new InvalidOperationException("テスト用のエラー"); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void 日時と例外の中身を書き足す()
    {
        ErrorLog.Append(_dir, FakeClock.At(2026, 11, 11, 10, 0), Thrown());
        var path = ErrorLog.Append(_dir, FakeClock.At(2026, 11, 11, 10, 5), Thrown());

        var text = File.ReadAllText(path);
        Assert.Equal(ErrorLog.PathIn(_dir), path);
        Assert.Contains("[2026-11-11 10:00:00 +09:00]", text);
        Assert.Contains("[2026-11-11 10:05:00 +09:00]", text);
        Assert.Contains("System.InvalidOperationException: テスト用のエラー", text);
        Assert.Contains(nameof(Thrown), text); // スタックトレースも残す
    }

    [Fact]
    public void 大きくなったら1世代だけ残して書き直す()
    {
        Directory.CreateDirectory(_dir);
        var path = ErrorLog.PathIn(_dir);
        File.WriteAllText(path, new string('x', (int)ErrorLog.MaxBytes + 1));

        ErrorLog.Append(_dir, FakeClock.At(2026, 11, 11, 10, 0), Thrown());

        Assert.True(new FileInfo(Path.Combine(_dir, "error.1.log")).Length > ErrorLog.MaxBytes);
        Assert.StartsWith("[2026-11-11", File.ReadAllText(path));
    }
}
