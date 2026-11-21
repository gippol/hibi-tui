using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Hibi;

/// <summary>一覧の1行の中の部分ごとの色の種類。</summary>
internal enum Tone
{
    /// <summary>テーマの文字色のまま（タスク名など）。</summary>
    Normal,
    /// <summary>[今日の予定] などの見出し。</summary>
    Header,
    /// <summary>作業中の印とタスク名。</summary>
    Running,
    /// <summary>優先度: 高の印。</summary>
    High,
    /// <summary>控えめに出すもの（#プロジェクト、期限、優先度: 低の印）。</summary>
    Muted,
    /// <summary>期限が今日。</summary>
    DueToday,
    /// <summary>期限切れ。</summary>
    Overdue,
    /// <summary>完了したタスクの行。</summary>
    Done,
}

/// <summary>色を付ける1つの部分。</summary>
internal readonly record struct Span(string Text, Tone Tone = Tone.Normal);

/// <summary>
/// 行を部分ごとの色で描く一覧の中身。文字は <paramref name="rows"/>（数・幅・文字での移動に使う）、
/// 色は同じ番号の <paramref name="spans"/> から取る。2つは同じ順に同じ数だけ入れておく。
/// 選んでいる行は、どこを選んでいるか見失わないように、色を分けずに選択の色で描く。
/// <see cref="ListWrapper{T}.Render"/> は virtual ではないので、<see cref="IListDataSource"/> を実装し直して差し替える
/// （ListView はインターフェイス経由で呼ぶ）。
/// </summary>
internal sealed class TaskListSource(ObservableCollection<string> rows, List<Span[]> spans)
    : ListWrapper<string>(rows), IListDataSource
{
    public new void Render(ListView container, bool selected, int item, int col, int line, int width, int viewportX)
    {
        if (selected || item < 0 || item >= spans.Count)
        {
            base.Render(container, selected, item, col, line, width, viewportX);
            return;
        }

        var normal = container.GetAttributeForRole(VisualRole.Normal);
        container.Move(Math.Max(col - viewportX, 0), line);

        // 横にずらしている分（viewportX）を飛ばし、width 桁まで描いて、残りは空白で埋める
        var skip = viewportX;
        var used = 0;
        foreach (var (rune, tone) in spans[item].SelectMany(s => s.Text.EnumerateRunes().Select(r => (r, s.Tone))))
        {
            var w = Math.Max(rune.GetColumns(), 1);
            if (skip > 0)
            {
                skip -= w;
                continue;
            }
            if (used + w > width) break;
            container.SetAttribute(AttributeFor(tone, normal));
            container.AddRune(rune);
            used += w;
        }
        container.SetAttribute(normal);
        for (; used < width; used++) container.AddRune(new Rune(' '));
    }

    /// <summary>背景はテーマのまま、文字の色と太さだけ変える。</summary>
    private static Attribute AttributeFor(Tone tone, Attribute normal)
    {
        var bg = normal.Background;
        return tone switch
        {
            Tone.Header => new Attribute(new Color(ColorName16.BrightCyan), bg, TextStyle.Bold),
            Tone.Running => new Attribute(new Color(ColorName16.BrightGreen), bg, TextStyle.Bold),
            Tone.High => new Attribute(new Color(ColorName16.BrightYellow), bg, TextStyle.Bold),
            Tone.Muted => new Attribute(new Color(ColorName16.Gray), bg),
            Tone.DueToday => new Attribute(new Color(ColorName16.BrightYellow), bg),
            Tone.Overdue => new Attribute(new Color(ColorName16.BrightRed), bg, TextStyle.Bold),
            Tone.Done => new Attribute(new Color(ColorName16.DarkGray), bg),
            _ => normal,
        };
    }
}
