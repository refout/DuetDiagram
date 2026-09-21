using DuetDiagram.Core.Model;

namespace DuetDiagram.Layout;

/// <summary>一个坐标点。</summary>
public sealed record LayoutPoint(double X, double Y);

/// <summary>
/// 布局用的端口。
/// </summary>
/// <remarks>
/// 只保留布局关心的三样：名字、在哪条边、沿边偏移多少。
/// 颜色、是否自定义这些属于渲染与归属信息，布局不需要，带了只会让输入类型跟着 IR 一起膨胀。
/// </remarks>
/// <param name="Name">端口名。与边上的端口引用对应。</param>
/// <param name="Side">在哪条边。</param>
/// <param name="Offset">沿所在边的偏移，0 到 1。</param>
public sealed record LayoutPort(string Name, PortSide Side, double Offset);

/// <summary>
/// 一个组合与它的成员。
/// </summary>
/// <remarks>
/// <para>
/// 布局只需要两样：标识，以及成员是谁。成员表是权威的那一份——
/// 节点的父级只是便于查询的冗余索引，两份不一致时按成员表算。
/// </para>
/// <para>
/// 组合的**包围盒不在这里**：它由成员的最终坐标算出来，而坐标要等布局求解完才有。
/// 所以这里给的是结构，盒子在求解之后现算，见 <c>CompositeOutline</c>。
/// </para>
/// </remarks>
/// <param name="Id">组合标识。</param>
/// <param name="Members">直接成员。可以是节点，也可以是另一个组合。</param>
public sealed record LayoutGroup(string Id, IReadOnlyList<string> Members);

/// <summary>
/// 参与布局的一个节点。
/// </summary>
/// <remarks>
/// <para>
/// 尺寸是**测量出来的**，不是 IR 里的字段。IR 只存语义（标签、形状），
/// 实际占多大取决于字体、字号与文本长度，而那些要到渲染时才确定。
/// 因此尺寸由调用方测好后传进来，布局不自己猜。
/// </para>
/// <para>
/// <see cref="Pinned"/> 是用户声明的坐标。它是**唯一的硬保证**：
/// 布局在任何情况下都不能让这个坐标偏移。
/// </para>
/// <para>
/// 输入与输出分成两个类型（还有 <see cref="PlacedNode"/>）。
/// 合成一个的话，输入上会挂着一对无意义的坐标，而任何一处忘了初始化
/// 都会变成"读取了上次残留的位置"这种很难查的问题。
/// </para>
/// </remarks>
/// <param name="Id">节点标识。</param>
/// <param name="Width">测量得到的宽度。</param>
/// <param name="Height">测量得到的高度。</param>
/// <param name="Pinned">固定坐标。为空表示由引擎自由摆放。</param>
/// <param name="Ports">端口。为空表示按形状自动均分。</param>
/// <param name="Parent">所属组合的标识。用于层级布局，本轮仅透传。</param>
public sealed record LayoutNode(
    string Id,
    double Width,
    double Height,
    LayoutPoint? Pinned = null,
    IReadOnlyList<LayoutPort>? Ports = null,
    string? Parent = null)
{
    /// <summary>取指定名称的端口。找不到返回空。</summary>
    public LayoutPort? FindPort(string? name) =>
        name is null ? null : Ports?.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

/// <summary>参与布局的一条边。</summary>
/// <param name="Id">边标识。</param>
/// <param name="From">起点节点标识。</param>
/// <param name="To">终点节点标识。</param>
/// <param name="FromPort">起点端口名。为空表示自动选边。</param>
/// <param name="ToPort">终点端口名。为空表示自动选边。</param>
public sealed record LayoutEdge(
    string Id,
    string From,
    string To,
    string? FromPort = null,
    string? ToPort = null);

/// <summary>
/// 布局参数。
/// </summary>
/// <remarks>
/// <para>
/// 只带布局真正需要的东西。约束的归属方、创建时间这些属于冲突处置，
/// 应当在调用这一层之前就处理掉——引擎不该关心"这条约束是谁提的"。
/// </para>
/// <para>
/// <see cref="SameRankGroups"/> 里的每一组是一批必须落在同一层的节点。
/// 归属方已经在这一层之前过滤掉了，传进来的都应当被执行。
/// </para>
/// </remarks>
public sealed record LayoutOptions(
    Direction Direction = Direction.TB,
    double NodeSpacing = 40,
    double LayerSpacing = 70,
    IReadOnlyList<IReadOnlyList<string>>? SameRankGroups = null)
{
    /// <summary>
    /// 层是不是沿纵向排列的。
    /// </summary>
    /// <remarks>
    /// 这个判断决定层内让位往哪个方向推。注意与"层的推进方向"无关：
    /// 自下而上和自上而下的层内阅读方向都是从左往右，所以两者用同一个方向让位。
    /// </remarks>
    public bool RanksAreVertical => Direction is Direction.TB or Direction.BT;
}
