using System.Globalization;
using System.Text;
using Hibi.Core;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// TextView は v2.5 で非推奨（別パッケージの EditorView が後継）だが、読むだけの欄には標準の TextView で足りる
#pragma warning disable CS0618

namespace Hibi;

/// <summary>
/// 詳細欄のメモ。読むだけで、Markdown 風に色分けする（<see cref="MemoStyle"/>）。
/// <list type="bullet">
/// <item>読み取り専用の TextView は既定で入力欄用の配色（背景が変わる）になるので、まわりの欄と同じ通常の配色で描く</item>
/// <item>折り返しは TextView に任せず自分でする。表示の行と元の文字の対応を持って、色を元の文字から決めるため
/// （TextView の折り返しの対応を調べる方法は外に出ていない）。幅が変わったら折り返し直す</item>
/// </list>
/// </summary>
internal sealed class MemoView : TextView
{
    /// <summary>行ごとの、文字の種類（UTF-16 の位置）。</summary>
    private IReadOnlyList<MemoTone[]> _tones = [];

    /// <summary>元の行ごとの、1マス（書記素）が始まる UTF-16 の位置。</summary>
    private List<int[]> _cellStarts = [];

    /// <summary>表示の行ごとの、元の行と、その行の最初のマスの番号。</summary>
    private List<(int Row, int FirstCell)> _rows = [];

    private string[] _lines = [];
    private int _wrappedWidth = -1;

    /// <summary>今出しているメモ（折り返す前）。</summary>
    public string Source { get; private set; } = "";

    public MemoView()
    {
        ReadOnly = true;
        WordWrap = false;
        CanFocus = false;
    }

    /// <summary>中身を差し替える（色分けと折り返しをやり直す）。</summary>
    public void SetMemo(string text)
    {
        Source = text;
        // タブは幅が変わって色の位置がずれるので、表示では空白にする
        var shown = text.Replace("\t", "    ", StringComparison.Ordinal);
        _tones = MemoStyle.Classify(shown);
        _lines = shown.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        _cellStarts = _lines.Select(StringInfo.ParseCombiningCharacters).ToList();
        Wrap(Viewport.Width);
    }

    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        if (Viewport.Width != _wrappedWidth) Wrap(Viewport.Width);
    }

    /// <summary>
    /// 幅に収まるように折り返す。英数字の語の途中では、できるだけ切らずに語の前で折り返す（日本語はどこでも切る）。
    /// </summary>
    private void Wrap(int width)
    {
        _wrappedWidth = width;
        var max = width > 0 ? width : int.MaxValue;
        var rows = new List<(int Row, int FirstCell)>();
        var text = new StringBuilder();

        for (var r = 0; r < _lines.Length; r++)
        {
            var line = _lines[r];
            var starts = _cellStarts[r];
            var first = 0;
            while (true)
            {
                var end = BreakAt(line, starts, first, max);
                rows.Add((r, first));
                if (rows.Count > 1) text.Append('\n');
                text.Append(line.AsSpan(At(line, starts, first), At(line, starts, end) - At(line, starts, first)));
                if (end >= starts.Length) break;
                first = end;
            }
        }
        _rows = rows;
        Text = text.ToString();
    }

    /// <summary>マスの番号から UTF-16 の位置（行の終わりなら長さ）。</summary>
    private static int At(string line, int[] starts, int cell) => cell < starts.Length ? starts[cell] : line.Length;

    /// <summary><paramref name="first"/> から始めて、幅に収まる最後のマスの次（次の行の最初のマス）。</summary>
    private static int BreakAt(string line, int[] starts, int first, int max)
    {
        var used = 0;
        var i = first;
        for (; i < starts.Length; i++)
        {
            var w = Math.Max(Rune.GetRuneAt(line, starts[i]).GetColumns(), 1);
            if (used + w > max) break;
            used += w;
        }
        if (i >= starts.Length || i == first) return Math.Max(i, first + 1);

        // 英数字の語の途中なら、語の前まで戻る（1行まるごと1語なら戻らずに切る）
        if (IsWordChar(line[starts[i]]) && IsWordChar(line[starts[i - 1]]))
        {
            var j = i;
            while (j > first && IsWordChar(line[starts[j - 1]])) j--;
            if (j > first) return j;
        }
        return i;

        static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '%' or '?' or '=' or '&';
    }

    /// <summary>入力欄用の配色を使わず、まわり（通常）と同じにする。</summary>
    protected override bool OnGettingAttributeForRole(in VisualRole role, ref Attribute currentAttribute)
    {
        if (role is VisualRole.Normal or VisualRole.Editable or VisualRole.ReadOnly or VisualRole.Focus
            && SuperView is { } parent)
        {
            currentAttribute = parent.GetAttributeForRole(VisualRole.Normal);
            return true;
        }
        return base.OnGettingAttributeForRole(role, ref currentAttribute);
    }

    protected override void OnDrawReadOnlyColor(List<Cell> line, int idxCol, int idxRow) =>
        SetAttribute(AttributeAt(idxRow, idxCol));

    protected override void OnDrawNormalColor(List<Cell> line, int idxCol, int idxRow) =>
        SetAttribute(AttributeAt(idxRow, idxCol));

    /// <summary>表示の位置から、元の行と文字を探して色を決める。</summary>
    private Attribute AttributeAt(int row, int col)
    {
        var normal = GetAttributeForRole(VisualRole.Normal);
        if (row < 0 || row >= _rows.Count) return normal;
        var (r, first) = _rows[row];
        var cell = first + col;
        if (r >= _tones.Count || cell < 0 || cell >= _cellStarts[r].Length) return normal;
        var i = _cellStarts[r][cell];
        return i < _tones[r].Length ? AttributeFor(_tones[r][i], normal) : normal;
    }

    /// <summary>背景はまわりのまま、文字の色と太さだけ変える（一覧の色分けと同じ色を使う）。</summary>
    private static Attribute AttributeFor(MemoTone tone, Attribute normal)
    {
        var bg = normal.Background;
        return tone switch
        {
            MemoTone.Heading => new Attribute(new Color(ColorName16.BrightCyan), bg, TextStyle.Bold),
            MemoTone.Marker => new Attribute(new Color(ColorName16.BrightYellow), bg),
            MemoTone.Done => new Attribute(new Color(ColorName16.DarkGray), bg),
            MemoTone.Muted => new Attribute(new Color(ColorName16.Gray), bg),
            MemoTone.Code => new Attribute(new Color(ColorName16.BrightGreen), bg),
            MemoTone.Link => new Attribute(new Color(ColorName16.BrightBlue), bg, TextStyle.Underline),
            MemoTone.Bold => new Attribute(normal.Foreground, bg, TextStyle.Bold),
            _ => normal,
        };
    }
}
