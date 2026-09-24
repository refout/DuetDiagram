using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands;

/// <summary>
/// 文本预设增删改的逆变更。
/// </summary>
/// <remarks>
/// <para>
/// 记的是**改之前的整份预设集合**，而不是"这一个预设"。三条预设命令共用这一个记录：
/// 三者要还原的都是"改之前的那一份"，方向不同而已。与调色板、标签、动作那几个同一个形状。
/// </para>
/// <para>
/// 预设与元素的关系是**按值**的：应用预设把样式成员抄到节点的文本样式上，
/// 元素身上不存预设的标识。所以删一个预设不需要摘任何引用，整份集合换回去就还原了。
/// </para>
/// </remarks>
public sealed record TextPresetMemento : CommandMemento
{
    /// <summary>改之前的预设集合，顺序原样保留。</summary>
    public required TextStylePreset[] PreviousPresets { get; init; }
}

/// <summary>
/// 应用预设的逆变更：每个被点名节点在改之前的文本样式。
/// </summary>
/// <remarks>
/// <para>
/// 与预设增删改的逆变更**刻意分成两个记录**：两者要还原的值类型不同——
/// 一个还原的是预设集合，一个还原的是各节点上的文本样式。合成一个带两组成员的记录，
/// 读日志的人会看到一个只填了一半的快照，而它对应的是哪一次改动只能靠猜。
/// </para>
/// <para>
/// 只记每个节点的旧文本样式，不记整份节点定义：应用预设动的是每个节点上的
/// 一个成员，其余字段一个都不碰。记整份的话撤销要把节点集合整个换掉，
/// 而其中没被碰过的那些也被重写了一遍——那时如果中间有别的命令改过别的节点，
/// 换回去会把那些改动一起抹掉。
/// </para>
/// </remarks>
public sealed record TextPresetApplyMemento : CommandMemento
{
    /// <summary>每个被点名的节点在改之前的文本样式。空表示它当时没有设置任何文本样式。</summary>
    public required PresetTextPlacement[] Previous { get; init; }
}

/// <summary>一个节点在应用预设之前的文本样式。</summary>
/// <param name="NodeId">节点标识。</param>
/// <param name="Text">改之前的文本样式。空表示当时没有设置。</param>
public sealed record PresetTextPlacement(string NodeId, TextStyle? Text);
