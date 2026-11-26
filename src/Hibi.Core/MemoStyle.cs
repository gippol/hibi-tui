namespace Hibi.Core;

/// <summary>メモの文字の種類（色分けに使う）。</summary>
public enum MemoTone
{
    Normal,
    /// <summary><c># 見出し</c> の行。</summary>
    Heading,
    /// <summary>箇条書き・番号・チェックボックスの印（<c>-</c> <c>1.</c> <c>[ ]</c>）。</summary>
    Marker,
    /// <summary>チェック済み（<c>- [x]</c>）の行。</summary>
    Done,
    /// <summary><c>&gt;</c> の引用、<c>---</c> の区切り線、<c>**</c> などの記号。</summary>
    Muted,
    /// <summary><c>`コード`</c> と <c>```</c> で囲んだ部分。</summary>
    Code,
    /// <summary>開けるリンク（<see cref="MemoLinks"/> が拾うもの）。</summary>
    Link,
    /// <summary><c>**強調**</c> の中身。</summary>
    Bold,
}

/// <summary>
/// メモを Markdown 風に色分けするための種類を決める。表示を変えるだけで、メモの中身は変えない（記号も消さない）。
/// </summary>
public static class MemoStyle
{
    /// <summary>
    /// 行ごと（<c>\n</c> で分ける。<c>\r</c> は落とす）に、各文字（UTF-16 の位置）の種類を返す。
    /// </summary>
    public static IReadOnlyList<MemoTone[]> Classify(string? memo)
    {
        var result = new List<MemoTone[]>();
        if (memo is null) return result;

        var inFence = false;
        foreach (var raw in memo.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tones = new MemoTone[line.Length];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Fill(tones, 0, tones.Length, MemoTone.Code);
                inFence = !inFence;
            }
            else if (inFence)
                Fill(tones, 0, tones.Length, MemoTone.Code);
            else if (IsHeading(trimmed))
                Fill(tones, 0, tones.Length, MemoTone.Heading);
            else if (IsRule(trimmed))
                Fill(tones, 0, tones.Length, MemoTone.Muted);
            else if (trimmed.StartsWith('>'))
            {
                Fill(tones, 0, tones.Length, MemoTone.Muted);
                Links(line, tones);
            }
            else
            {
                var body = indent + ListMarkerLength(trimmed);
                Fill(tones, indent, body, MemoTone.Marker);
                var rest = line[body..];
                if (CheckBox(rest) is { } box)
                {
                    Fill(tones, body, body + box.Length, MemoTone.Marker);
                    if (box.Done)
                    {
                        Fill(tones, body + box.Length, tones.Length, MemoTone.Done);
                        result.Add(tones);
                        continue;
                    }
                    body += box.Length;
                }
                Inline(line, body, tones);
                Links(line, tones);
            }
            result.Add(tones);
        }
        return result;
    }

    private static void Fill(MemoTone[] tones, int from, int to, MemoTone tone)
    {
        for (var i = Math.Max(0, from); i < Math.Min(to, tones.Length); i++) tones[i] = tone;
    }

    /// <summary><c>#</c> を 1〜6 個並べて空白（全角も）が続くもの。</summary>
    private static bool IsHeading(string s)
    {
        var n = 0;
        while (n < s.Length && s[n] == '#') n++;
        return n is >= 1 and <= 6 && (n == s.Length || s[n] is ' ' or '　' or '\t');
    }

    /// <summary><c>---</c> <c>***</c> <c>___</c>（同じ記号3つ以上。間の空白は可）。</summary>
    private static bool IsRule(string s)
    {
        var t = s.Replace(" ", "", StringComparison.Ordinal);
        return t.Length >= 3 && t[0] is '-' or '*' or '_' && t.All(c => c == t[0]);
    }

    /// <summary>行頭の <c>- </c> <c>* </c> <c>+ </c> <c>・</c> <c>1. </c> <c>1) </c> の長さ（印と続く空白）。なければ 0。</summary>
    private static int ListMarkerLength(string s)
    {
        if (s.Length >= 2 && s[0] is '-' or '*' or '+' && s[1] is ' ' or '　') return 2;
        if (s.Length >= 1 && s[0] == '・') return 1;
        var n = 0;
        while (n < s.Length && char.IsAsciiDigit(s[n])) n++;
        if (n is > 0 and <= 3 && n + 1 < s.Length && s[n] is '.' or ')' && s[n + 1] == ' ') return n + 2;
        return 0;
    }

    /// <summary>行の残りの先頭の <c>[ ] </c> / <c>[x] </c>。</summary>
    private static (int Length, bool Done)? CheckBox(string s)
    {
        if (s.Length < 3 || s[0] != '[' || s[2] != ']') return null;
        if (s.Length > 3 && s[3] is not (' ' or '　')) return null;
        return s[1] switch
        {
            ' ' or '　' => (Math.Min(s.Length, 4), false),
            'x' or 'X' or 'ｘ' => (Math.Min(s.Length, 4), true),
            _ => null,
        };
    }

    /// <summary><c>`コード`</c> と <c>**強調**</c>。対になっていない記号はそのまま。</summary>
    private static void Inline(string line, int from, MemoTone[] tones)
    {
        var i = from;
        while (i < line.Length)
        {
            if (line[i] == '`')
            {
                var close = line.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    Fill(tones, i, close + 1, MemoTone.Code);
                    i = close + 1;
                    continue;
                }
            }
            else if (string.CompareOrdinal(line, i, "**", 0, 2) == 0)
            {
                var close = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    Fill(tones, i, i + 2, MemoTone.Muted);
                    Fill(tones, i + 2, close, MemoTone.Bold);
                    Fill(tones, close, close + 2, MemoTone.Muted);
                    i = close + 2;
                    continue;
                }
            }
            i++;
        }
    }

    /// <summary><see cref="MemoLinks"/> が拾うリンクだけ色を付ける（色が付いていれば l で開ける）。コードの中は除く。</summary>
    private static void Links(string line, MemoTone[] tones)
    {
        var at = 0;
        foreach (var link in MemoLinks.Extract(line))
        {
            var i = line.IndexOf(link.Text, at, StringComparison.Ordinal);
            if (i < 0) continue;
            if (tones[i] != MemoTone.Code) Fill(tones, i, i + link.Text.Length, MemoTone.Link);
            at = i + link.Text.Length;
        }
    }
}
