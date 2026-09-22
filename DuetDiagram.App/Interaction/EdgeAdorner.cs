using DuetDiagram.Core.Model;
using DuetDiagram.Core.Sidecar;
using DuetDiagram.Layout;
using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 连线与边编辑时画在文档之上的一层临时图形：端口把手、连线预览、折点把手。
/// </summary>
/// <remarks>
/// <para>
/// 它只产出绘制指令，不读不写文档。画布把这一层叠在正式绘制列表之上，
/// 与节点拖拽的预览同构——临时图形跟着指针走，真正的改动留到松手那一刻才落定。
/// </para>
/// <para>
/// 把手大小按文档坐标给。它们画在节点边界上，随缩放一起变，而节点本体也随缩放一起变，
/// 所以相对大小不会失调；真要做成"屏幕像素恒定"得绕开变换在屏幕空间画，
/// 那是另一件事，现在不值得为它绕一次。
/// </para>
/// </remarks>
public static class EdgeAdorner
{
    private const string HandleColor = "#1f6feb";
    private const string PreviewColor = "#1f6feb";
    private const double HandleSize = 9;

    /// <summary>每个端口一个实心小方块把手。</summary>
    public static IReadOnlyList<DrawCommand> PortHandles(EngineLayoutResult layout, DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);

        var handles = new List<DrawCommand>();

        foreach (var (_, _, anchor) in EdgeHandleHitTest.PortAnchors(layout, document))
        {
            handles.Add(Handle(anchor.X, anchor.Y));
        }

        return handles;
    }

    /// <summary>连线预览：从起点端口拉到当前指针的一条虚线。</summary>
    public static IReadOnlyList<DrawCommand> ConnectPreview(IReadOnlyList<DrawPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return
        [
            new DrawPolyline(
                "__connect__",
                points,
                PreviewColor,
                1.5,
                LineStyle.Dashed,
                ArrowStyle.None),
        ];
    }

    /// <summary>一条边的固定折点：每个折点一个实心圆。</summary>
    public static IReadOnlyList<DrawCommand> BendHandles(string edgeId, IReadOnlyList<Anchor> bends)
    {
        ArgumentNullException.ThrowIfNull(bends);

        var handles = new List<DrawCommand>(bends.Count);

        foreach (var bend in bends)
        {
            handles.Add(Handle(bend.X, bend.Y, NodeShape.Circle));
        }

        return handles;
    }

    private static DrawShape Handle(double x, double y, NodeShape shape = NodeShape.Rect) =>
        new(
            "__handle__",
            shape,
            new SpatialRect(x - (HandleSize / 2), y - (HandleSize / 2), HandleSize, HandleSize),
            HandleColor,
            "#ffffff",
            1,
            LineStyle.Solid,
            0,
            1);
}
