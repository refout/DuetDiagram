using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 一次拖拽手势的瞬时状态。
/// </summary>
/// <remarks>
/// 它只记录"从哪开始、现在偏了多少、动的是谁"，不做任何提交。
/// 提交（写固定位置、重布局）由宿主在松手那一刻做，见 <see cref="DragController"/>。
/// 把状态与提交分开，是因为拖动过程中每一帧都要读这份状态去更新预览，
/// 而提交只在最后一帧发生一次——两件事混在一起，最容易在"第几帧该提交"上出错。
/// </remarks>
public sealed class DragSession
{
    private readonly DrawPoint _start;

    public DragSession(DrawPoint start, IReadOnlyList<string> nodeIds, IReadOnlyList<string> edgeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        ArgumentNullException.ThrowIfNull(edgeIds);

        _start = start;
        NodeIds = nodeIds;
        EdgeIds = edgeIds;
        Delta = new DrawPoint(0, 0);
    }

    /// <summary>这一拖要移动的节点标识。</summary>
    public IReadOnlyList<string> NodeIds { get; }

    /// <summary>要跟着重画的边标识。</summary>
    public IReadOnlyList<string> EdgeIds { get; }

    /// <summary>当前相对起点的偏移，文档坐标。</summary>
    public DrawPoint Delta { get; private set; }

    /// <summary>指针到了一个新位置，重算偏移。</summary>
    public void Move(DrawPoint current)
    {
        Delta = new DrawPoint(current.X - _start.X, current.Y - _start.Y);
    }
}

/// <summary>
/// 一次拖拽开始时的快照：动的是哪些节点、哪些边要跟着重画。
/// </summary>
/// <remarks>
/// 画布拿到它就开预览，不再回问会话"这一拖到底动了谁"——
/// 那个问题在按下那一刻已经答过一次，拖动中重问只会让预览与选中对不上。
/// </remarks>
public sealed record DragPreview(IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds);
