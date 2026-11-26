using System.ComponentModel;
using System.Diagnostics;
using Hibi.Core;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Hibi;

/// <summary>
/// メモのリンク（<see cref="MemoLinks"/>）を開く。開く前に必ず行き先を見せて確認する。
/// <list type="bullet">
/// <item>URL は http / https だけ。ブラウザで開く</item>
/// <item>フォルダはエクスプローラーで開く</item>
/// <item>ファイルは、許可した拡張子（文書・画像など）だけ既定のアプリで開く。
/// 実行ファイル・スクリプト・ショートカットなどは開かず、エクスプローラーで場所を見せるだけにする</item>
/// <item>ネットワーク上の場所（\\サーバー\...）は、つなぎに行く前に確認する
/// （有るか無いかを調べるだけでもサーバーにつなぐため）</item>
/// </list>
/// </summary>
internal static class LinkOpener
{
    private const string Cancel = "キャンセル";

    public static void Open(IApplication app, MemoLinks.Link link)
    {
        if (link.Kind == MemoLinks.Kind.Url) OpenUrl(app, link.Target);
        else OpenPath(app, link.Target);
    }

    private static void OpenUrl(IApplication app, string url)
    {
        var insecure = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "\n※ 暗号化されていない接続（http）です。"
            : "";
        var message =
            $"ブラウザで次の URL を開きます。\n\n" +
            $"行き先: {MemoLinks.HostOf(url)}\n" +
            $"{url}\n{insecure}\n" +
            "行き先に心当たりがなければ開かないでください。";
        if (Ask(app, "URL を開く", message, "開く", Cancel) != 0) return;
        Start(app, new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void OpenPath(IApplication app, string path)
    {
        if (MemoLinks.IsNetworkPath(path))
        {
            var server = path.TrimStart('\\').Split('\\')[0];
            var message =
                $"ネットワーク上の場所にアクセスします。\n\n" +
                $"サーバー: {server}\n{path}\n\n" +
                "知らないサーバーには、つなぐだけで Windows のサインイン情報が送られることがあります。\n" +
                "心当たりがなければ続けないでください。";
            if (Ask(app, "ネットワーク上の場所", message, "続ける", Cancel) != 0) return;
        }

        if (Directory.Exists(path))
        {
            if (Ask(app, "フォルダを開く", $"エクスプローラーで次のフォルダを開きます。\n\n{path}", "開く", Cancel) != 0) return;
            Start(app, Explorer($"\"{path}\""));
        }
        else if (File.Exists(path))
        {
            var name = Path.GetFileName(path);
            if (MemoLinks.CanOpenFileDirectly(path))
            {
                var choice = Ask(app, "ファイルを開く",
                    $"次のファイルを既定のアプリで開きます。\n\n{name}\n{path}",
                    "開く", "フォルダで表示", Cancel);
                if (choice == 0) Start(app, new ProcessStartInfo(path) { UseShellExecute = true });
                else if (choice == 1) Start(app, ShowInFolder(path));
            }
            else
            {
                var ext = Path.GetExtension(path);
                var kind = ext.Length > 0 ? $"この種類のファイル（{ext}）" : "拡張子のないファイル";
                var message =
                    $"{kind}は、プログラムとして動くことがあるため、hibi からは直接開きません。\n" +
                    "エクスプローラーで場所を見せるので、中身を確かめてから開いてください。\n\n" +
                    $"{name}\n{path}";
                if (Ask(app, "ファイルを開く", message, "フォルダで表示", Cancel) != 0) return;
                Start(app, ShowInFolder(path));
            }
        }
        else
        {
            Dialogs.Error(app, $"見つかりません（消えたか、名前が変わったか、つながっていません）。\n\n{path}");
        }
    }

    /// <summary>押したボタンの番号。Esc なら null。</summary>
    private static int? Ask(IApplication app, string title, string message, params string[] buttons) =>
        MessageBox.Query(app, title, message, buttons);

    /// <summary>
    /// エクスプローラーは Windows のフォルダのものを使う（PATH やカレントフォルダに置かれた同じ名前のものを拾わないように）。
    /// パスには " を含めない（<see cref="MemoLinks"/> で弾いている）ので、引数は " で囲むだけでいい。
    /// </summary>
    private static ProcessStartInfo Explorer(string arguments) =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arguments)
        {
            UseShellExecute = false,
        };

    /// <summary>ファイルを開かず、それを選んだ状態でフォルダを開く。</summary>
    private static ProcessStartInfo ShowInFolder(string path) => Explorer($"/select,\"{path}\"");

    private static void Start(IApplication app, ProcessStartInfo info)
    {
        try
        {
            using var _ = Process.Start(info);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Dialogs.Error(app, $"開けませんでした: {ex.Message}");
        }
    }
}
