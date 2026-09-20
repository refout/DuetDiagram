namespace DuetDiagram.Layout;

/// <summary>
/// 布局引擎。
/// </summary>
/// <remarks>
/// <para>
/// **引擎不得感知降级级别。** 它只回答一个问题：给定这批输入，坐标是什么。
/// 降到几级、为什么降、试了几次，都是协调器的事。
/// </para>
/// <para>
/// 这条界线不划清的话，引擎内部会长出"如果这是降级后的输入就少做点什么"这类分支，
/// 而那样的分支既难测又难推理——同一个级别组合在不同调用路径上会走出不同的算法。
/// 级别的影响只体现为**输入不同**：降级就是丢掉一部分输入再调一次。
/// </para>
/// <para>
/// 参数里的取消令牌用于协作式取消。实现应当在每个阶段之间检查它，
/// 让超时被放弃的那一次能尽快停下来，而不是占着线程跑完。
/// </para>
/// </remarks>
public interface ILayoutEngine
{
    /// <summary>求解一次布局。</summary>
    /// <param name="request">布局输入。已经按级别裁剪过。</param>
    /// <param name="cancellationToken">取消令牌。实现应当在阶段之间检查。</param>
    EngineLayoutResult Layout(LayoutRequest request, CancellationToken cancellationToken = default);
}
