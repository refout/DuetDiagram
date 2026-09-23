using System.Text.Json.Serialization;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 命令的逆变更快照：命令执行前把"怎么改回去"需要的信息存下来，撤销时照着还原。
/// </summary>
/// <remarks>
/// <para>
/// 为什么用"抽象基类 + 密封派生记录"而不是一个万能的 object 字段：
/// 序列化必须在 AOT 下工作，AOT 不允许运行时反射发现类型。
/// 每个派生类型都显式标注多态标签，反序列化时才能按标签精确还原成正确的类型。
/// </para>
/// <para>
/// **新增派生记录时必须同时补一个标注多态标签的特性**，否则编译能过、
/// 单机运行也能过，但 AOT 发布后一旦反序列化这种 memento 就会失败——
/// 这是最典型的"测试环境发现不了、上生产才炸"的问题，所以有一条专门的测试
/// 用反射枚举程序集里所有具体 memento，跟已标注的集合做全等比较，漏一个就红。
/// </para>
/// <para>
/// 每个命令的 memento 必须携带足够还原的信息。最容易出错的地方是**位置**：
/// 撤销一个删除操作时，元素要插回原来的索引，否则顺序变了，
/// 后续依赖顺序的逻辑（例如层内次序）就会跟删除前不一致。
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$memento")]
[JsonDerivedType(typeof(AddNodeMemento), "add-node")]
[JsonDerivedType(typeof(RemoveNodeMemento), "remove-node")]
[JsonDerivedType(typeof(ConnectEdgeMemento), "connect-edge")]
[JsonDerivedType(typeof(DisconnectEdgeMemento), "disconnect-edge")]
[JsonDerivedType(typeof(SetNodeFieldMemento), "set-node-field")]
[JsonDerivedType(typeof(ReconnectEdgeMemento), "reconnect-edge")]
[JsonDerivedType(typeof(SetEdgeFieldMemento), "set-edge-field")]
[JsonDerivedType(typeof(LayoutConstraintMemento), "layout-constraint")]
[JsonDerivedType(typeof(SetDirectionMemento), "set-direction")]
[JsonDerivedType(typeof(SetKindMemento), "set-kind")]
[JsonDerivedType(typeof(PaletteMemento), "palette")]
[JsonDerivedType(typeof(CanvasMemento), "canvas")]
[JsonDerivedType(typeof(CompositeMemento), "composite")]
[JsonDerivedType(typeof(LayerMemento), "layer")]
[JsonDerivedType(typeof(AssignLayerMemento), "assign-layer")]
[JsonDerivedType(typeof(PageMemento), "page")]
[JsonDerivedType(typeof(TagMemento), "tag")]
[JsonDerivedType(typeof(ActionMemento), "action")]
public abstract record CommandMemento
{
    /// <summary>本次变更波及的元素标识，用于增量同步时告诉对端"重取这些元素"。</summary>
    public string[] AffectedIds { get; init; } = [];

    /// <summary>
    /// 逆变更，方向与命令本身相反：新增命令存的是"移除"，删除命令存的是"新增"。
    /// 撤销时把它写进版本日志，对端就能看到一次逻辑上正确的反向变更。
    /// </summary>
    public FieldChange[] InverseChanges { get; init; } = [];
}

/// <summary>
/// 一条边在被删除前所处的位置。
/// </summary>
/// <remarks>
/// 撤销删除节点时要连同它关联的边一起还原。只存边本身不够——
/// 边的集合顺序有意义，必须记住它原来在集合里的第几位，
/// 还原时按索引升序插回，才能得到与删除前逐项一致的顺序。
/// </remarks>
public sealed record EdgePlacement(int Index, EdgeDef Edge);

/// <summary>新增节点的逆变更：知道删掉哪个节点即可，索引只用于还原层内次序。</summary>
public sealed record AddNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    /// <summary>节点被插入的位置。</summary>
    public required int Index { get; init; }
}

/// <summary>
/// 删除节点的逆变更。
/// </summary>
/// <remarks>
/// 删除节点会连带删除所有挂在它身上的边，所以 memento 里除了节点本身，
/// 还要存下这些边以及它们各自原来的位置。
/// </remarks>
public sealed record RemoveNodeMemento : CommandMemento
{
    public required NodeDef Node { get; init; }

    public required int Index { get; init; }

    /// <summary>被连带删除的边及其原索引。</summary>
    public EdgePlacement[] RemovedEdges { get; init; } = [];
}

/// <summary>
/// 新增边的逆变更。
/// </summary>
/// <remarks>
/// 这里只记录边本身和它被追加到的位置。撤销新增边只需按标识把边移除，
/// 不必依赖索引，但保留索引让后续可能的"顺序敏感"场景仍有依据。
/// </remarks>
public sealed record ConnectEdgeMemento : CommandMemento
{
    public required EdgeDef Edge { get; init; }

    public required int Index { get; init; }
}

/// <summary>
/// 删除边的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 与新增边的逆变更形状一样（边本身 + 索引），但**不共用那个记录**：两个记录的
/// 类型名要说明方向，否则读日志的人看到一个"新增边"的快照却对应一次删除，
/// 只能靠上下文猜。多一个类型只多一行多态标签，而标签写错是编译期就能发现的。
/// </para>
/// <para>
/// 索引是撤销时的插入位置。边的集合顺序有语义（层内次序按出边先后排列），
/// 所以不能省掉索引改用追加——追加之后顺序变了，而两个哈希都按标识排序，
/// 算出来一模一样，这个错在哈希上看不出来。
/// </para>
/// </remarks>
public sealed record DisconnectEdgeMemento : CommandMemento
{
    public required EdgeDef Edge { get; init; }

    /// <summary>这条边被删除前的位置。</summary>
    public required int Index { get; init; }
}

/// <summary>
/// 改一个节点字段的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 这里存的是**改之前的那整份节点定义**，而不是"某个字段的旧值"。
/// 只存旧值的话，还原时要按字段名再拼一次定义，而那一次拼接与命令里的拼接
/// 必须逐字一致——两处一旦分叉，撤销出来的节点与原来那份会有细微差别，
/// 而差异只体现在哈希上，界面上看不出来。
/// </para>
/// <para>
/// 存整份定义也顺带解决了值的类型问题：字段值有字符串、枚举、记录、列表几种，
/// 只存旧值就要引入一个能装下它们的联合类型，而联合类型在原生编译下要靠多态注册，
/// 注册漏一个是发布之后才暴露的问题。
/// </para>
/// </remarks>
public sealed record SetNodeFieldMemento : CommandMemento
{
    public required string NodeId { get; init; }

    /// <summary>改之前的节点定义。</summary>
    public required NodeDef Previous { get; init; }

    /// <summary>改的是哪个字段。</summary>
    public required string Field { get; init; }

    /// <summary>改之前的字段值，读成文本。只用于差异展示，还原不靠它。</summary>
    public string? OldValue { get; init; }

    /// <summary>请求写入的字段值原样。只用于审计，还原不靠它。</summary>
    public string? NewValue { get; init; }
}

/// <summary>重连边端点的逆变更：记下改之前的整条边定义。</summary>
/// <remarks>
/// 端点是四个字段一起改的，存整条边比存四个旧值更稳——还原时直接按标识换回原定义，
/// 不必逐个字段拼回去。四个字段里某个拼错的话，撤销出来的边与原来那份会有细微差别，
/// 而差异只体现在哈希上，界面上看不出来。
/// </remarks>
public sealed record ReconnectEdgeMemento : CommandMemento
{
    public required string EdgeId { get; init; }

    /// <summary>改之前的边定义。</summary>
    public required EdgeDef Previous { get; init; }
}

/// <summary>改一条边字段的逆变更：记下改之前的整条边定义。</summary>
public sealed record SetEdgeFieldMemento : CommandMemento
{
    public required string EdgeId { get; init; }

    /// <summary>改之前的边定义。</summary>
    public required EdgeDef Previous { get; init; }

    /// <summary>改的是哪个字段。</summary>
    public required string Field { get; init; }

    /// <summary>改之前的字段值，读成文本。只用于差异展示，还原不靠它。</summary>
    public string? OldValue { get; init; }

    /// <summary>请求写入的字段值原样。只用于审计，还原不靠它。</summary>
    public string? NewValue { get; init; }
}

/// <summary>
/// 改布局主方向的逆变更。
/// </summary>
/// <remarks>
/// 只记改之前的那个方向就够——它是文档上的一个标量，不是集合里的一条，
/// 还原时直接写回去，没有"位置"要操心。
/// </remarks>
public sealed record SetDirectionMemento : CommandMemento
{
    /// <summary>改之前的方向。</summary>
    public required Direction Previous { get; init; }
}

/// <summary>
/// 改文档类型的逆变更。
/// </summary>
/// <remarks>
/// 与改方向同一个形状：图类型也是文档上的一个标量，不是集合里的一条，
/// 还原时直接写回去，没有"位置"要操心。两个记录不合并，是因为要还原的值类型不同——
/// 合成一个带两个可空成员的记录，读日志的人会看到一个只填了一半的快照，
/// 而它对应的是哪一次改动只能靠猜。
/// </remarks>
public sealed record SetKindMemento : CommandMemento
{
    /// <summary>改之前的图类型。</summary>
    public required DiagramKind Previous { get; init; }
}

/// <summary>
/// 调色板增删改的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 记的是**改之前的整份调色板**，而不是"这一个条目"或"这一个成员"。
/// 只记一个条目的话，还原时要按成员再拼一次记录，而那一次拼接必须与命令里的拼接
/// 逐字一致——两处一旦分叉，撤销出来的条目与原来那份会有细微差别，
/// 而差异只体现在哈希上，界面上看不出来。
/// </para>
/// <para>
/// 增、删、改共用这一个记录：三者要还原的都是"改之前的那一份"，方向不同而已。
/// </para>
/// </remarks>
public sealed record PaletteMemento : CommandMemento
{
    /// <summary>改之前的调色板。</summary>
    public required Palette Previous { get; init; }
}

/// <summary>
/// 画布设置的逆变更。
/// </summary>
/// <remarks>
/// 记的是**改之前的整份画布设置**，而不是"改到的那几个成员"。一次调用可能改到其中几项，
/// 只记一项的话，还原时要按成员再拼一次记录，而那一次拼接必须与命令里的拼接逐字一致——
/// 两处一旦分叉，撤销出来的设置与原来那份会有细微差别，而差异只体现在哈希上。
/// </remarks>
public sealed record CanvasMemento : CommandMemento
{
    /// <summary>改之前的画布设置。</summary>
    public required CanvasSettings Previous { get; init; }
}

/// <summary>
/// 一个成员（节点或组合）在改之前的父级。
/// </summary>
/// <remarks>
/// 成员关系有两处表达：容器的成员列表，与成员自己的父级字段。父级字段是冗余的那一份，
/// 但它同样要跟着还原——只把成员列表换回去而不管父级，文档就成了一份自相矛盾的东西，
/// 而这种矛盾只有整体校验器会报。
/// </remarks>
public sealed record MemberPlacement(string Id, string? Parent);

/// <summary>
/// 组合增删与成员搬移的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 记的是**改之前的整份组合集合**，而不是"这一个组合"。三条组合命令一次都会动到多个组合：
/// 把成员搬进一个新组合，除了新组合之外，原来那个容器的成员列表也跟着变了。
/// 只记一个的话，还原时要按种类再拼一次列表，而那一次拼接必须与命令里的拼接逐字一致——
/// 两处一旦分叉，撤销出来的成员关系与原来那份会有细微差别，而差异只体现在哈希上。
/// </para>
/// <para>
/// 组合数量是"一屏能看完"的量级，整份记下来的代价可以忽略。
/// </para>
/// </remarks>
public sealed record CompositeMemento : CommandMemento
{
    /// <summary>改之前的组合集合，顺序原样保留。</summary>
    public required CompositeDef[] PreviousComposites { get; init; }

    /// <summary>改之前各成员的父级。</summary>
    public MemberPlacement[] PreviousParents { get; init; } = [];
}

/// <summary>
/// 图层增删改的逆变更。
/// </summary>
/// <remarks>
/// 记的是**改之前的整份图层集合**。三条图层命令里，重排一次会改到每一个图层的次序，
/// 只记被点名的那个的话，还原之后其余图层的次序还停在改动之后的样子。
/// </remarks>
public sealed record LayerMemento : CommandMemento
{
    /// <summary>改之前的图层集合，顺序与次序原样保留。</summary>
    public required LayerDef[] PreviousLayers { get; init; }
}

/// <summary>
/// 一批元素的图层归属在改之前的样子。
/// </summary>
/// <remarks>
/// <para>
/// 只记「哪个元素原来在哪一层」。这里没有位置要记——与删除那类命令不同，
/// 改归属不动任何集合的先后：节点集合的顺序、边的顺序都由别的字段决定。
/// </para>
/// <para>
/// 也不记整份节点集合。一次移入动的是每个节点上的一个字段，
/// 记整份的话撤销要把节点集合整个换掉，而其中没被碰过的那些也被重写了一遍——
/// 那时如果中间有别的命令改过别的节点，换回去会把那些改动一起抹掉。
/// </para>
/// </remarks>
public sealed record AssignLayerMemento : CommandMemento
{
    /// <summary>每个被点名的元素在改之前的归属。空表示它当时在缺省层上。</summary>
    public required NodeLayerPlacement[] Previous { get; init; }
}

/// <summary>一个元素在改之前归在哪一层。</summary>
/// <param name="NodeId">元素标识。</param>
/// <param name="Layer">改之前的图层标识。空表示缺省层。</param>
public sealed record NodeLayerPlacement(string NodeId, string? Layer);

/// <summary>
/// 页面增删的逆变更。
/// </summary>
/// <remarks>
/// 记的是**改之前的整份页面集合**，而不是"这一页"。新建的逆操作是删除，按标识删掉就行；
/// 但删除的逆操作必须把那一页插回它原来那一格——页面的集合位置是加入顺序，
/// 一律追加到末尾会让撤销之后的页面次序与删除前不同，而两个哈希都按标识排序后遍历，
/// 顺序错了照样对得上，这个错在哈希上看不出来。
/// </remarks>
public sealed record PageMemento : CommandMemento
{
    /// <summary>改之前的页面集合，顺序原样保留。</summary>
    public required PageDef[] PreviousPages { get; init; }
}

/// <summary>
/// 标签增删的逆变更。
/// </summary>
/// <remarks>
/// 记的是**改之前的整份标签集合**。标签与元素的关系是单向的：成员列表挂在标签自己身上，
/// 被点名的元素上没有回指字段，所以删一个标签不需要顺带摘掉别处的引用，
/// 还原时把整份集合换回去就够了。
/// </remarks>
public sealed record TagMemento : CommandMemento
{
    /// <summary>改之前的标签集合，顺序原样保留。</summary>
    public required TagDef[] PreviousTags { get; init; }
}

/// <summary>
/// 动作增删的逆变更。
/// </summary>
/// <remarks>
/// 与标签同一个形状：关系是单向的，目标元素上没有回指字段，
/// 所以整份集合换回去就还原了，没有别处要摘。
/// </remarks>
public sealed record ActionMemento : CommandMemento
{
    /// <summary>改之前的动作集合，顺序原样保留。</summary>
    public required ActionDef[] PreviousActions { get; init; }
}

/// <summary>
/// 增删一条布局约束的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 记的是**改之前的整份布局提示**，而不是"这一条约束"。四类约束在同一份记录里，
/// 只记一条的话，还原时要按种类再拼一次列表，而那一次拼接必须与命令里的拼接逐字一致——
/// 两处一旦分叉，撤销出来的约束集合与原来那份会有细微差别，而差异只体现在哈希上。
/// </para>
/// <para>
/// **增、删、改共用这一个记录**：三者要还原的都是"改之前的那一份"，方向不同而已。
/// 记的是整份布局提示，所以两个间距的改动也走这里——它们同样落在布局提示上。
/// </para>
/// </remarks>
public sealed record LayoutConstraintMemento : CommandMemento
{
    /// <summary>改之前的布局提示。</summary>
    public required LayoutHints Previous { get; init; }
}
