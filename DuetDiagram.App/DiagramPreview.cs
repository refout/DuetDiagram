using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DuetDiagram.App;

/// <summary>
/// 绘图区的最小实现：画一张固定的示意图，用来验证整条渲染链路能否跑通。
/// </summary>
/// <remarks>
/// <para>
/// 这里刻意只画"死"的图形，不接任何编辑逻辑。它的职责是证明三件事能在当前技术栈上同时成立：
/// 图形绘制上下文可用、文本能被正确测量与绘制、坐标变换后的结果符合预期。
/// 真正的画布、视口虚拟化、交互都在后续阶段实现。
/// </para>
/// <para>
/// 走自绘而不是用现成的控件组合，是因为最终要自己控制成千上万个元素的绘制时机与裁剪，
/// 提前确认自绘这条路可行比先做出好看的界面更重要。
/// </para>
/// </remarks>
public sealed class DiagramPreview : Control
{
    private static readonly IBrush NodeFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFE));
    private static readonly IBrush NodeStroke = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    private static readonly IBrush DecisionFill = new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0xE3));
    private static readonly IBrush DecisionStroke = new SolidColorBrush(Color.FromRgb(0xE8, 0x8A, 0x1A));
    private static readonly IBrush EdgeStroke = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2C));
    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    /// <summary>
    /// 宿主报告的参与绘制元素数量。它不改变画出来的内容，只作为图上的一行注记，
    /// 让观察截图的人能确认这次渲染的规模与预期一致。
    /// </summary>
    public int NodeCount { get; set; } = 4;

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // 位置写死：只验证绘制正确性，不涉及布局算法。
        var start = new Rect(24, 40, 120, 44);
        var check = new Rect(184, 20, 140, 80);
        var fail = new Rect(184, 140, 120, 44);
        var end = new Rect(420, 40, 120, 44);

        // 先画连线再画节点，这样连线端部会被节点盖住，不必精确计算裁剪。
        DrawConnector(context, start, check);
        DrawConnector(context, check, fail);
        DrawConnector(context, check, end);
        DrawConnector(context, fail, end);

        DrawBox(context, start, "开始", NodeFill, NodeStroke);
        DrawDiamond(context, check, "校验", DecisionFill, DecisionStroke);
        DrawBox(context, fail, "失败", NodeFill, NodeStroke);
        DrawBox(context, end, "结束", NodeFill, NodeStroke);

        var watermark = new FormattedText(
            $"elements: {NodeCount}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            12,
            LabelBrush);

        context.DrawText(watermark, new Point(24, Math.Max(24, Bounds.Height - 24)));
    }

    private static void DrawBox(DrawingContext context, Rect bounds, string label, IBrush fill, IBrush stroke)
    {
        context.DrawRectangle(fill, new Pen(stroke, 1.5), new RoundedRect(bounds, 6));
        DrawCenteredLabel(context, bounds, label);
    }

    private static void DrawDiamond(DrawingContext context, Rect bounds, string label, IBrush fill, IBrush stroke)
    {
        var center = bounds.Center;
        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(center.X, bounds.Top), true);
            sink.LineTo(new Point(bounds.Right, center.Y));
            sink.LineTo(new Point(center.X, bounds.Bottom));
            sink.LineTo(new Point(bounds.Left, center.Y));
            sink.EndFigure(true);
        }

        context.DrawGeometry(fill, new Pen(stroke, 1.5), geometry);
        DrawCenteredLabel(context, bounds, label);
    }

    private static void DrawCenteredLabel(DrawingContext context, Rect bounds, string label)
    {
        var text = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            14,
            LabelBrush);

        context.DrawText(text, new Point(
            bounds.Center.X - (text.Width / 2),
            bounds.Center.Y - (text.Height / 2)));
    }

    /// <summary>
    /// 画一条从 <paramref name="from"/> 连到 <paramref name="to"/> 的折线并加上箭头。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 出口和入口按两个元素的相对位置挑：目标基本在正下方或正上方时走竖向（底边中点到底边中点），
    /// 否则走横向（右边中点到左边中点）。这一步不能省——
    /// 如果一律按横向处理，上下相邻的两个元素之间会画出一条先向左再向下的回头线，
    /// 看起来像画错了，而实际上是出口选错了边。
    /// </para>
    /// <para>
    /// 折线取中点折一次，这是流程图里最常见的走线方式，也让同层与跨层的连线都能沿空白区域走。
    /// </para>
    /// </remarks>
    private static void DrawConnector(DrawingContext context, Rect from, Rect to, double arrowSize = 7)
    {
        var vertical = to.Top >= from.Bottom - 1 || to.Bottom <= from.Top + 1;

        Point start;
        Point end;
        Point firstBend;
        Point secondBend;

        // 走标准的"两折"路径：先沿出口方向走一段，再横移到目标轴线，最后进入目标。
        // 只折一次会产生一段斜线，视觉上比两折更乱，也更容易压到别的元素。
        if (vertical)
        {
            var goingDown = to.Top >= from.Bottom;
            start = new Point(from.Center.X, goingDown ? from.Bottom : from.Top);
            end = new Point(to.Center.X, goingDown ? to.Top : to.Bottom);

            var joint = (start.Y + end.Y) / 2;
            firstBend = new Point(start.X, joint);
            secondBend = new Point(end.X, joint);
        }
        else
        {
            var goingRight = to.Left >= from.Right;
            start = new Point(goingRight ? from.Right : from.Left, from.Center.Y);
            end = new Point(goingRight ? to.Left : to.Right, to.Center.Y);

            var joint = (start.X + end.X) / 2;
            firstBend = new Point(joint, start.Y);
            secondBend = new Point(joint, end.Y);
        }

        var geometry = new StreamGeometry();

        using (var sink = geometry.Open())
        {
            sink.BeginFigure(start, false);
            sink.LineTo(firstBend);
            sink.LineTo(secondBend);
            sink.LineTo(end);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(EdgeStroke, 1.5), geometry);

        // 箭头单独画一个三角形，避免与折线共用一个图形而需要处理端点的连接方式。
        var back = vertical
            ? new Point(end.X - (arrowSize / 2), end.Y - (end.Y > start.Y ? arrowSize : -arrowSize))
            : new Point(end.X - (end.X > start.X ? arrowSize : -arrowSize), end.Y - (arrowSize / 2));

        var forward = vertical
            ? new Point(end.X + (arrowSize / 2), back.Y)
            : new Point(back.X, end.Y + (arrowSize / 2));

        var head = new StreamGeometry();

        using (var sink = head.Open())
        {
            sink.BeginFigure(end, true);
            sink.LineTo(back);
            sink.LineTo(forward);
            sink.EndFigure(true);
        }

        context.DrawGeometry(EdgeStroke, null, head);
    }
}
