using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using System.Linq;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 画布上的点下面是不是一个"可以抓住的把手"：端口（连线起点）、边的端点（重连）、边的中间（加折点）。
/// </summary>
/// <remarks>
/// <para>
/// 判定集中在一处：画布只管问"这点下面是什么把手"，得到结果再去决定走哪条手势。
/// 把判定散在画布各处的话，连线、重连、加折点三处迟早会对"多近算抓住"给出不同的答案。
/// </para>
/// <para>
/// 容差是文档坐标。调用方先把屏幕像素换算成文档距离再传进来，放大之后同样的屏幕距离
/// 对应更小的文档距离，不换算的话放大之后这些把手会变得极难点中。
/// </para>
/// </remarks>
public static class EdgeHandleHitTest
{
    /// <summary>一个端口把手：哪个节点的哪个端口（节点没有显式端口时端口名为空）。</summary>
    public readonly record struct PortHandle(string NodeId, string? PortName);

    /// <summary>抓住边上的哪个把手。</summary>
    public enum EdgeHandleKind
    {
        /// <summary>没抓到把手。</summary>
        None,

        /// <summary>边的起点端点，拖它可以把起点重连到别处。</summary>
        Start,

        /// <summary>边的终点端点，拖它可以把终点重连到别处。</summary>
        End,

        /// <summary>边的中间，拖它可以加一个折点。</summary>
        Midpoint,
    }

    /// <summary>抓住了一条边的某个把手。折点那一种带"插到哪一段之间"。</summary>
    public readonly record struct EdgeHandleHit(string EdgeId, EdgeHandleKind Kind, int Segment);

    /// <summary>节点右上角之外、留给"从节点拖出连线"的那个默认把手名。</summary>
    private const string DefaultPortName = "__connect__";

    /// <summary>
    /// 每个节点在文档坐标下的端口把手位置。没有显式端口的节点给一个默认把手（端口名为空）。
    /// </summary>
    /// <remarks>
    /// 命中判定与把手绘制共用这份结果，避免两处对"哪些节点有把手"给出不同的答案。
    /// </remarks>
    public static IEnumerable<(string NodeId, string? PortName, LayoutPoint Anchor)> PortAnchors(
        EngineLayoutResult layout,
        DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);

        foreach (var node in document.Nodes)
        {
            var placed = layout.Find(node.Id);

            if (placed is null)
            {
                continue;
            }

            var ports = node.Ports.Count == 0
                ? [new LayoutPort(DefaultPortName, PortSide.Right, 0.5)]
                : node.Ports.Select(p => new LayoutPort(p.Name, p.Side, p.Offset)).ToList();

            foreach (var port in ports)
            {
                yield return (
                    node.Id,
                    string.Equals(port.Name, DefaultPortName, StringComparison.Ordinal) ? null : port.Name,
                    placed.PortAnchor(port));
            }
        }
    }

    /// <summary>
    /// 这一点下面是不是一个端口把手。
    /// </summary>
    public static PortHandle? HitPort(
        EngineLayoutResult layout,
        DiagramDocument document,
        DrawPoint point,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(document);

        PortHandle? best = null;
        var bestDistance = tolerance;

        foreach (var (nodeId, portName, anchor) in PortAnchors(layout, document))
        {
            var distance = Distance(anchor, point);

            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = new PortHandle(nodeId, portName);
            }
        }

        return best;
    }

    /// <summary>
    /// 这一点下面是不是一条边的端点或中间。
    /// </summary>
    /// <remarks>
    /// 端点优先于中间：按下靠近端点时用户多半是想重连，而不是在那一小段上加折点。
    /// 跨边取最近的那一条，避免两条边交叠时抓错。
    /// </remarks>
    public static EdgeHandleHit HitEdgeHandle(EngineLayoutResult layout, DrawPoint point, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var best = new EdgeHandleHit(string.Empty, EdgeHandleKind.None, -1);
        var bestDistance = tolerance;

        foreach (var edge in layout.Edges)
        {
            if (edge.Points.Length == 0)
            {
                continue;
            }

            var start = edge.Points[0];
            var end = edge.Points[^1];

            var startDistance = Distance(start, point);

            if (startDistance <= bestDistance)
            {
                bestDistance = startDistance;
                best = new EdgeHandleHit(edge.Id, EdgeHandleKind.Start, -1);
            }

            var endDistance = Distance(end, point);

            if (endDistance <= bestDistance)
            {
                bestDistance = endDistance;
                best = new EdgeHandleHit(edge.Id, EdgeHandleKind.End, -1);
            }

            // 中间把手：找离点最近的这一段，且点到那一段的垂直距离够近。
            // 段本身比端点长得多，所以这一段判定只在"没抓住端点"时才有意义。
            for (var index = 0; index + 1 < edge.Points.Length; index++)
            {
                var segmentDistance = DistanceToSegment(edge.Points[index], edge.Points[index + 1], point);

                if (segmentDistance <= bestDistance)
                {
                    bestDistance = segmentDistance;
                    best = new EdgeHandleHit(edge.Id, EdgeHandleKind.Midpoint, index);
                }
            }
        }

        return best;
    }

    private static double Distance(LayoutPoint a, DrawPoint b) =>
        Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double DistanceToSegment(LayoutPoint a, LayoutPoint b, DrawPoint point)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);

        if (lengthSquared < 1e-9)
        {
            return Distance(a, point);
        }

        var t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var projectionX = a.X + (t * dx);
        var projectionY = a.Y + (t * dy);

        return Math.Sqrt(((projectionX - point.X) * (projectionX - point.X)) + ((projectionY - point.Y) * (projectionY - point.Y)));
    }
}
