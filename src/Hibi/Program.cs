using System.Diagnostics;
using System.Text;
using Hibi;
using Hibi.Core;
using Terminal.Gui.App;

var startupTimer = Stopwatch.StartNew();
Console.OutputEncoding = Encoding.UTF8;

// 設定ファイルの場所は環境変数で差し替えられる（試験用・複数環境用）
var configPath = Environment.GetEnvironmentVariable("HIBI_CONFIG") is { Length: > 0 } p
    ? Path.GetFullPath(p)
    : AppConfig.DefaultConfigPath;
// --startup-check: 最初の描画まで行って終了し、起動時間を表示する（N-P-01 の確認用）
var startupCheck = args.Contains("--startup-check");

AppConfig config;
TaskStore store;
TaskData data;
try
{
    config = AppConfig.LoadOrCreate(configPath);
    store = new TaskStore(config.DataDirectoryPath);
    data = store.Load();
    store.Backup(DateTimeOffset.Now);
}
catch (ConfigException ex)
{
    Console.Error.WriteLine($"設定ファイルのエラー: {ex.Message}");
    Console.Error.WriteLine($"  {configPath}");
    Console.Error.WriteLine("  ファイルを直すか、削除すると既定値で作り直します。");
    return 1;
}
catch (DataLoadException ex)
{
    // N-R-04: 壊れたファイルは上書きせずに終了する
    var s = new TaskStore(AppConfig.LoadOrCreate(configPath).DataDirectoryPath);
    Console.Error.WriteLine($"データを読み込めませんでした: {ex.Message}");
    Console.Error.WriteLine($"  {s.DataPath}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("バックアップから戻す手順:");
    Console.Error.WriteLine($"  1. {s.BackupDirectory} を開く");
    Console.Error.WriteLine("  2. いちばん新しい tasks.YYYYMMDD-HHmmss.json を選ぶ");
    Console.Error.WriteLine($"  3. それを {s.DataPath} にコピーして上書きする");
    return 1;
}

var service = new TaskService(data, new BusinessDayCalendar(config.DayBoundaryTime), TimeProvider.System);

// 画面の外（別スレッドなど）で落ちたときも、せめて記録は残す
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex) TryLog(ex);
};

using IApplication app = Application.Create();
app.Init();

using var main = new MainWindow(app, service, store, config);
app.StopAfterFirstIteration = startupCheck;

// 予期しないエラーは記録してから知らせ、そのまま続ける（データは変更のたびに保存済み）。
// ただし短い間に何度も起きるなら（描画のたびに落ちるなど）続けても抜け出せないので終了する。
var recentErrors = new Queue<DateTimeOffset>();
Exception? fatal = null;
string? fatalLog = null;
app.Run(main, ex =>
{
    var logPath = TryLog(ex);
    var now = DateTimeOffset.Now;
    recentErrors.Enqueue(now);
    while (recentErrors.Peek() < now - TimeSpan.FromSeconds(10)) recentErrors.Dequeue();
    if (recentErrors.Count >= 3)
    {
        (fatal, fatalLog) = (ex, logPath);
        return false;
    }
    try
    {
        Dialogs.Error(app,
            $"予期しないエラーが起きました: {ex.Message}\n" +
            $"記録: {logPath ?? "（書けませんでした）"}\n" +
            "このまま使い続けられます。");
        return true;
    }
    catch (Exception)
    {
        (fatal, fatalLog) = (ex, logPath);
        return false;
    }
});

if (fatal is not null)
{
    Console.Error.WriteLine($"予期しないエラーが続いたため終了しました: {fatal.Message}");
    Console.Error.WriteLine($"  記録: {fatalLog ?? "（書けませんでした）"}");
    return 1;
}

if (startupCheck)
    Console.WriteLine($"startup: {startupTimer.ElapsedMilliseconds} ms (process: {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} ms)");
return 0;

/// <summary>error.log に書く。書けなければ null（記録のせいでさらに落ちないように）。</summary>
string? TryLog(Exception ex)
{
    try
    {
        return ErrorLog.Append(config.DataDirectoryPath, DateTimeOffset.Now, ex);
    }
    catch (Exception)
    {
        return null;
    }
}
