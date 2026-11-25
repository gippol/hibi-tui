namespace Hibi;

/// <summary>
/// ターミナルのウィンドウ（タブ）のタイトル。タスクバーやタブに出るので、別のウィンドウにいても計測の状態が見える。
/// Windows だけ（Console.Title の設定は Win32 の SetConsoleTitle で、画面の描画とぶつからない）。
/// 終了するときに起動前のタイトルへ戻す。
/// </summary>
internal static class ConsoleTitle
{
    private static string? _original;
    private static string? _current;

    public static void Set(string title)
    {
        if (!OperatingSystem.IsWindows() || title == _current) return;
        try
        {
            _original ??= Console.Title;
            Console.Title = title;
            _current = title;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            // タイトルを変えられない環境（リダイレクト中など）では何もしない
        }
    }

    public static void Restore()
    {
        if (!OperatingSystem.IsWindows() || _original is null) return;
        try
        {
            Console.Title = _original;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or InvalidOperationException)
        {
        }
        _current = null;
    }
}
