using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 排好版的一段字。
/// </summary>
/// <remarks>
/// 与 <see cref="TextSegment"/> 同一个形状，但位置说的是**另一件事**：
/// 那一边的 <c>Y</c> 是行顶，这一边的是这一段的框顶，而框高是这一段自己量出来的。
/// 公式里的每一段字号都可能不同（上下标要缩小），共用行高的话上下标会被顶开。
/// </remarks>
/// <param name="Text">这一段写出来是什么。</param>
/// <param name="X">左边缘，相对整块的左上角。</param>
/// <param name="Y">上边缘，相对整块的左上角。</param>
/// <param name="Width">宽度，量出来的。</param>
/// <param name="Height">高度，量出来的。</param>
/// <param name="Appearance">外观，已经解析。</param>
public sealed record MathSegment(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    TextAppearance Appearance);

/// <summary>排好版的一条线：分数线、根号的上横线。</summary>
/// <param name="X1">起点横坐标，相对整块的左上角。</param>
/// <param name="Y1">起点纵坐标。</param>
/// <param name="X2">终点横坐标。</param>
/// <param name="Y2">终点纵坐标。</param>
/// <param name="Weight">线宽。</param>
public sealed record MathRule(double X1, double Y1, double X2, double Y2, double Weight);

/// <summary>
/// 排好版的一整个公式。
/// </summary>
/// <remarks>
/// <para>
/// **它比文本块多两样东西：基线，以及不属于文字的线。** 分数与根号画不出文字的框，
/// 所以除了文字段还要带一组线；而基线是"行内对齐"唯一能对齐的东西——
/// 按框居中排的话，带分数与不带宽的公式会各居各的，两行之间看着参差不齐。
/// </para>
/// <para>
/// 位置相对整块的左上角，不是画布坐标——加画布偏移是排进容器那一步的事。
/// </para>
/// </remarks>
/// <param name="Width">整块宽度。</param>
/// <param name="Height">整块高度。</param>
/// <param name="Baseline">基线离整块顶端的距离。</param>
/// <param name="Segments">文字段。</param>
/// <param name="Rules">线。</param>
public sealed record MathBody(
    double Width,
    double Height,
    double Baseline,
    IReadOnlyList<MathSegment> Segments,
    IReadOnlyList<MathRule> Rules);

/// <summary>
/// 把一棵语法树排成有位置、有尺寸的一整块。
/// </summary>
/// <remarks>
/// <para>
/// **只用 <see cref="ITextMeasurer"/> 给的宽与高，不另起一套字体。** 另起一套的话，
/// 公式里的字与旁边的字会来自两个字体栈，表现是同一个节点里两种字形。
/// 代价是拿不到字体的升部与降部，所以基线在行框里的位置按一个写死的比例算
/// （见 <see cref="MathMetrics.BaselineFraction"/>）——它是**约定**，不是量出来的。
/// </para>
/// <para>
/// **同一份输入每次排出来逐字节相同。** 与文本排版同一条口径：这里没有一个
/// 会随机器变化的东西，所有尺寸都由字号乘一个写死的比例得到。
/// </para>
/// <para>
/// **上下标与分母不做二次压缩。** 真排版里"上下标的上下标"还要再小一档，
/// 那需要按嵌套层数传一个字号下去；这一轮不做，边界写在文档里。
/// </para>
/// </remarks>
internal static class MathLayout
{
    /// <summary>排一整棵树。</summary>
    public static MathBody Arrange(MathNode root, TextAppearance text, ITextMeasurer measurer)
    {
        var box = Measure(root, text, measurer);

        // 排出来的坐标以基线为原点，而块对外说的是"左上角"。
        // 差一个升部，只在这里补一次。
        return new MathBody(
            box.Width,
            box.Ascent + box.Descent,
            box.Ascent,
            [.. box.Segments.Select(segment => segment with { Y = segment.Y + box.Ascent })],
            [.. box.Rules.Select(rule => rule with { Y1 = rule.Y1 + box.Ascent, Y2 = rule.Y2 + box.Ascent })]);
    }

    private static Box Measure(MathNode node, TextAppearance text, ITextMeasurer measurer) => node switch
    {
        MathRow row => Row(row, text, measurer),
        MathAtom atom => Atom(atom, text, measurer),
        MathSpace space => new Box { Width = space.Em * text.FontSize },
        MathFraction fraction => Fraction(fraction, text, measurer),
        MathRadical radical => Radical(radical, text, measurer),
        MathScript script => Script(script, text, measurer),
        _ => new Box(),
    };

    private static Box Row(MathRow row, TextAppearance text, ITextMeasurer measurer)
    {
        var result = new Box();
        var x = 0.0;

        foreach (var child in row.Children)
        {
            var box = Measure(child, text, measurer);

            result.Add(box, x, 0);
            result.Ascent = Math.Max(result.Ascent, box.Ascent);
            result.Descent = Math.Max(result.Descent, box.Descent);
            x += box.Width;
        }

        result.Width = x;
        return result;
    }

    private static Box Atom(MathAtom atom, TextAppearance text, ITextMeasurer measurer)
    {
        var appearance = text with { Italic = atom.Italic };
        var size = measurer.Measure(
            atom.Text,
            appearance.FontFamily,
            appearance.FontSize,
            appearance.Weight,
            appearance.Italic);

        var ascent = size.Height * MathMetrics.BaselineFraction;
        var box = new Box { Width = size.Width, Ascent = ascent, Descent = size.Height - ascent };

        // 一段文字自己的框就是量出来的那个框，框顶落在 -升部 处：
        // 绘制方把字在框里垂直居中，居中之后字的中心正好比基线高一点点，
        // 与"基线离框顶 0.8 倍框高"这条约定对得上。
        box.Segments.Add(new MathSegment(atom.Text, 0, -ascent, size.Width, size.Height, appearance));

        return box;
    }

    private static Box Fraction(MathFraction fraction, TextAppearance text, ITextMeasurer measurer)
    {
        var numerator = Measure(fraction.Numerator, text, measurer);
        var denominator = Measure(fraction.Denominator, text, measurer);

        var em = text.FontSize;
        var axis = em * MathMetrics.AxisHeight;
        var gap = em * MathMetrics.FractionGap;
        var thickness = em * MathMetrics.RuleThickness;
        var width = Math.Max(numerator.Width, denominator.Width);

        // 分子底在轴线上方 gap 处，分母顶在轴线下方 gap 处，横线压在轴线上。
        var numeratorBaseline = -axis - gap - numerator.Descent;
        var denominatorBaseline = -axis + gap + denominator.Ascent;

        var box = new Box
        {
            Width = width,
            Ascent = axis + gap + numerator.Height,
            Descent = denominator.Height + gap - axis,
        };

        box.Add(numerator, (width - numerator.Width) / 2, numeratorBaseline);
        box.Add(denominator, (width - denominator.Width) / 2, denominatorBaseline);
        box.Rules.Add(new MathRule(0, -axis, width, -axis, thickness));

        return box;
    }

    private static Box Radical(MathRadical radical, TextAppearance text, ITextMeasurer measurer)
    {
        var inner = Measure(radical.Radicand, text, measurer);

        var em = text.FontSize;
        var gap = em * MathMetrics.RadicalGap;
        var thickness = em * MathMetrics.RuleThickness;
        var hook = inner.Height * MathMetrics.RadicalOverhang;
        var width = hook + gap + inner.Width;

        var box = new Box
        {
            Width = width,
            Ascent = inner.Ascent + gap + thickness,
            Descent = inner.Descent,
        };

        // 内容顶到上横线的下沿，基线因此比整块的基线高一点点。
        box.Add(inner, hook + gap, -gap);

        // 根号是一个"√"：从左边中部起笔，斜下到谷底，再斜上顶到横线，最后横着拉到右端。
        // 三段各是一条直线，因为绘制列表里没有折线以外的画法——
        // 多画一种"路径"指令，每一个绘制方都要跟着认一次。
        //
        // 横线占的是上沿到上沿加线宽那一条，所以它的中心线在升部下面半个线宽处；
        // 内容顶在横线的下沿，两处都从 box.Ascent 推，不各算各的。
        var bar = -box.Ascent + (thickness / 2);
        var contentTop = -box.Ascent + thickness;
        var start = contentTop + (inner.Height * 0.55);
        var valley = contentTop + (inner.Height * 0.72);
        var shoulder = hook * 0.62;

        box.Rules.Add(new MathRule(0, start, hook * 0.3, valley, thickness));
        box.Rules.Add(new MathRule(hook * 0.3, valley, shoulder, bar, thickness));
        box.Rules.Add(new MathRule(shoulder, bar, width, bar, thickness));

        return box;
    }

    private static Box Script(MathScript script, TextAppearance text, ITextMeasurer measurer)
    {
        var root = Measure(script.Base, text, measurer);

        var em = text.FontSize;
        var scriptText = text with { FontSize = text.FontSize * MathMetrics.ScriptScale };
        var superscript = script.Superscript is null ? null : Measure(script.Superscript, scriptText, measurer);
        var subscript = script.Subscript is null ? null : Measure(script.Subscript, scriptText, measurer);

        var gap = em * MathMetrics.ScriptGap;
        var raise = em * MathMetrics.SuperscriptShift;
        var lower = em * MathMetrics.SubscriptShift;
        var scriptWidth = Math.Max(superscript?.Width ?? 0, subscript?.Width ?? 0);
        var x = scriptWidth > 0 ? root.Width + gap : root.Width;

        var box = new Box
        {
            Width = x + scriptWidth,
            Ascent = Math.Max(root.Ascent, superscript is null ? 0 : raise + superscript.Ascent),
            Descent = Math.Max(root.Descent, subscript is null ? 0 : lower + subscript.Descent),
        };

        box.Add(root, 0, 0);

        if (superscript is not null)
        {
            box.Add(superscript, x, -raise);
        }

        if (subscript is not null)
        {
            box.Add(subscript, x, lower);
        }

        return box;
    }

    /// <summary>排到一半的一块。坐标以基线为原点，还没有归一化成"左上角为原点"。</summary>
    private sealed class Box
    {
        public double Width { get; set; }

        public double Ascent { get; set; }

        public double Descent { get; set; }

        public double Height => Ascent + Descent;

        public List<MathSegment> Segments { get; } = [];

        public List<MathRule> Rules { get; } = [];

        /// <summary>把另一块整块挪到某个位置，连同它的线与字。</summary>
        public void Add(Box other, double dx, double dy)
        {
            foreach (var segment in other.Segments)
            {
                Segments.Add(segment with { X = segment.X + dx, Y = segment.Y + dy });
            }

            foreach (var rule in other.Rules)
            {
                Rules.Add(rule with
                {
                    X1 = rule.X1 + dx,
                    X2 = rule.X2 + dx,
                    Y1 = rule.Y1 + dy,
                    Y2 = rule.Y2 + dy,
                });
            }
        }
    }
}

/// <summary>
/// 公式排版的尺寸约定。
/// </summary>
/// <remarks>
/// <para>
/// **它们全是写死的比例，不是量出来的。** 度量接口只给宽与高，没有字体的升部降部，
/// 而公式里每一处间距都得有个数。写死之后同一份输入在任何机器上排出同一份结果；
/// 换成"按字体实际度量算"的话，换一台机器公式的疏密就变了，快照也跟着变。
/// </para>
/// <para>
/// 比例取自常见数学字体的观感，不追求与某一套排版系统逐像素相同：
/// 这一层要的是**稳定**与**边界清楚**，不是复刻。
/// </para>
/// </remarks>
internal static class MathMetrics
{
    /// <summary>基线在行框里离顶端的比例。与文本那一边的行高约定配套。</summary>
    public const double BaselineFraction = 0.8;

    /// <summary>数学轴线的高度。分数线压在它上面，上下标的基准也由它推。</summary>
    public const double AxisHeight = 0.25;

    /// <summary>分子分母与横线之间的空隙。</summary>
    public const double FractionGap = 0.12;

    /// <summary>分数线与根号的线宽。</summary>
    public const double RuleThickness = 0.06;

    /// <summary>根号里的内容与上横线之间的空隙。</summary>
    public const double RadicalGap = 0.1;

    /// <summary>根号钩子的横向宽度占内容高度的比例。</summary>
    public const double RadicalOverhang = 0.35;

    /// <summary>上下标相对底的字号比例。</summary>
    public const double ScriptScale = 0.7;

    /// <summary>上下标与底之间的横向空隙。</summary>
    public const double ScriptGap = 0.05;

    /// <summary>上标抬高的高度。</summary>
    public const double SuperscriptShift = 0.45;

    /// <summary>下标压低的高度。</summary>
    public const double SubscriptShift = 0.2;
}
