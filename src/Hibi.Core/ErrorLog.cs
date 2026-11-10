using System.Globalization;
using System.Text;

namespace Hibi.Core;

/// <summary>予期しないエラーを &lt;データフォルダ&gt;\error.log に書き足す（原因を後から追えるように）。</summary>
public static class ErrorLog
{
    /// <summary>これより大きくなったら error.1.log に移して書き直す（1世代だけ残す）。</summary>
    public const long MaxBytes = 1024 * 1024;

    public static string PathIn(string dataDirectory) => Path.Combine(dataDirectory, "error.log");

    /// <summary>書き足して、ログの場所を返す。</summary>
    public static string Append(string dataDirectory, DateTimeOffset now, Exception ex)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = PathIn(dataDirectory);
        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
            File.Move(path, Path.Combine(dataDirectory, "error.1.log"), overwrite: true);

        var entry = $"[{now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}]\n{ex}\n\n";
        File.AppendAllText(path, entry, new UTF8Encoding(false));
        return path;
    }
}
