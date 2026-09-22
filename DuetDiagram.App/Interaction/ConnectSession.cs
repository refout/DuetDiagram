using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 一次连线手势进行中的状态。
/// </summary>
/// <remarks>
/// 按下那一刻把来源节点与端口名记死，之后只更新指针位置用于预览。
/// 端口名不等到松手再去猜：自动端口压在节点边界哪一侧、偏多少，
/// 按下那一瞬间才是最准的，松手时再算反而可能选错边。
/// </remarks>
public sealed class ConnectSession
{
    public ConnectSession(string sourceId, string? sourcePort, DrawPoint start)
    {
        ArgumentNullException.ThrowIfNull(sourceId);

        SourceId = sourceId;
        SourcePort = sourcePort;
        Start = start;
        Current = start;
    }

    /// <summary>连线的来源节点（按下那一刻的节点）。</summary>
    public string SourceId { get; }

    /// <summary>从哪个端口出发；节点没有端口时为空。</summary>
    public string? SourcePort { get; }

    /// <summary>按下那一刻的文档坐标（端口位置）。</summary>
    public DrawPoint Start { get; }

    /// <summary>当前指针位置，文档坐标。只用于预览。</summary>
    public DrawPoint Current { get; private set; }

    /// <summary>预览线：从起点端口拉到当前指针。</summary>
    public IReadOnlyList<DrawPoint> PreviewPoints => [Start, Current];

    /// <summary>移动指针，只更新预览位置，不碰文档。</summary>
    public void Move(DrawPoint current) => Current = current;
}
