namespace Hibi.Core;

/// <summary>
/// メモに書いたリンク（URL・ファイル・フォルダ）を拾い、開いてよいものかを見る。
/// 実際に開くのは画面側で、開く前に必ず確認を出す。ここでは開かない・ファイルも見ない。
/// <list type="bullet">
/// <item>URL: <c>http://</c> と <c>https://</c> だけ。<c>javascript:</c> やアプリを起動するスキームは拾わない</item>
/// <item>パス: <c>C:\...</c> と <c>\\サーバー\共有\...</c>。空白を含むパスは <c>"..."</c> で囲む</item>
/// </list>
/// </summary>
public static class MemoLinks
{
    public enum Kind
    {
        Url,
        /// <summary>ファイルかフォルダのパス（どちらかは開くときに確かめる）。</summary>
        Path,
    }

    /// <summary>拾ったリンク。<paramref name="Target"/> は開くときに使う形（URL は正規化したもの、パスは絶対パス）。</summary>
    public sealed record Link(Kind Kind, string Text, string Target);

    /// <summary>
    /// 直接は開かず「フォルダで表示」だけにする拡張子以外を、そのまま開いてよいものとして持つ（許可リスト）。
    /// 実行ファイル・スクリプト・ショートカット・マクロ付きの Office ファイルなどは入れない。
    /// </summary>
    private static readonly HashSet<string> SafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".pdf", ".rtf",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".vsdx",
        ".odt", ".ods", ".odp",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
        ".zip",
    };

    /// <summary>表示と中身をずらすのに使われる文字（向きを変える文字・幅のない文字）。これを含むリンクは拾わない。</summary>
    private static bool IsDeceptive(char c) =>
        char.IsControl(c)
        || c is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u2060' or '\uFEFF'
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';

    /// <summary>リンクの終わりとみなす文字（空白のほか、文の区切りによく使う全角の記号）。</summary>
    private static bool IsTerminator(char c) =>
        char.IsWhiteSpace(c) || c is '"' or '<' or '>' or '「' or '」' or '『' or '』' or '（' or '）' or '、' or '。';

    /// <summary>メモに出てくる順に、重複を除いてリンクを返す。開けない形のものは入れない。</summary>
    public static IReadOnlyList<Link> Extract(string? memo)
    {
        var links = new List<Link>();
        if (string.IsNullOrEmpty(memo)) return links;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(Link? link)
        {
            if (link is not null && seen.Add(link.Target)) links.Add(link);
        }

        var i = 0;
        while (i < memo.Length)
        {
            // "..." で囲んだパス（空白を含めるため）
            if (memo[i] == '"')
            {
                var close = memo.IndexOf('"', i + 1);
                var lineEnd = memo.IndexOfAny(['\r', '\n'], i + 1);
                if (close > i && (lineEnd < 0 || close < lineEnd))
                {
                    var inner = memo[(i + 1)..close];
                    if (LooksLikePath(inner, 0))
                    {
                        Add(ToPath(inner));
                        i = close + 1;
                        continue;
                    }
                }
                i++;
                continue;
            }

            if (StartsWithUrl(memo, i) || LooksLikePath(memo, i))
            {
                // 語の途中（abchttps://...）からは拾わない
                if (i > 0 && !IsTerminator(memo[i - 1]) && !IsOpening(memo[i - 1]))
                {
                    i++;
                    continue;
                }
                var end = i;
                while (end < memo.Length && !IsTerminator(memo[end])) end++;
                var word = TrimTrailing(memo[i..end]);
                Add(StartsWithUrl(word, 0) ? ToUrl(word) : ToPath(word));
                i += Math.Max(1, word.Length);
                continue;
            }
            i++;
        }
        return links;
    }

    private static bool IsOpening(char c) => c is '(' or '[' or '{' or '\'' or '`';

    private static bool StartsWithUrl(string s, int i) =>
        string.Compare(s, i, "https://", 0, 8, StringComparison.OrdinalIgnoreCase) == 0
        || string.Compare(s, i, "http://", 0, 7, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary><c>C:\</c> か <c>\\サーバー</c> で始まるか。</summary>
    private static bool LooksLikePath(string s, int i) =>
        (i + 2 < s.Length && char.IsAsciiLetter(s[i]) && s[i + 1] == ':' && s[i + 2] is '\\' or '/'
            && (i + 3 >= s.Length || s[i + 3] is not '/')) // C:// のような書き方は除く
        || (i + 2 < s.Length && s[i] == '\\' && s[i + 1] == '\\' && !char.IsWhiteSpace(s[i + 2]) && s[i + 2] != '\\');

    /// <summary>文の終わりの句読点や、対になっていない閉じかっこを落とす。</summary>
    private static string TrimTrailing(string word)
    {
        while (word.Length > 0)
        {
            var last = word[^1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '`')
                word = word[..^1];
            else if (last is ')' or ']' or '}' && Count(word, Open(last)) < Count(word, last))
                word = word[..^1];
            else break;
        }
        return word;

        static char Open(char close) => close switch { ')' => '(', ']' => '[', _ => '{' };
        static int Count(string s, char c) => s.Count(x => x == c);
    }

    private static Link? ToUrl(string text)
    {
        if (text.Any(IsDeceptive)) return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;
        // https://正規のサイト@別のサイト/ のように、ユーザー名の欄で行き先をごまかせるので拾わない
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        return new Link(Kind.Url, text, uri.AbsoluteUri);
    }

    private static Link? ToPath(string text)
    {
        if (text.Length == 0 || text.Any(IsDeceptive)) return null;
        // ワイルドカードや、ドライブ以外の : （代替データストリーム file.txt:stream）は受け付けない
        if (text.IndexOfAny(['*', '?', '|', '<', '>', '"']) >= 0) return null;
        if (text.IndexOf(':', 2) >= 0) return null;
        // \\?\ や \\.\ （デバイスのパス）は受け付けない
        if (text.StartsWith(@"\\?", StringComparison.Ordinal) || text.StartsWith(@"\\.", StringComparison.Ordinal)) return null;
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(text.Replace('/', '\\'));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        return new Link(Kind.Path, text, full);
    }

    /// <summary>ネットワーク上の場所（\\サーバー\共有）か。開くとそのサーバーにつなぎに行く。</summary>
    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>
    /// ファイルをそのまま既定のアプリで開いてよいか（許可リストの拡張子か）。
    /// false のものは開かず、エクスプローラーで場所を見せるだけにする。
    /// </summary>
    public static bool CanOpenFileDirectly(string path) =>
        SafeExtensions.Contains(System.IO.Path.GetExtension(path.TrimEnd('.', ' ')));

    /// <summary>URL の行き先（ホスト名）。国際化ドメインは見た目が似た文字でごまかせるので、ASCII の形（xn--）も添える。</summary>
    public static string HostOf(string url)
    {
        var uri = new Uri(url);
        return uri.IdnHost != uri.Host ? $"{uri.Host}（{uri.IdnHost}）" : uri.Host;
    }
}
