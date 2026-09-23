namespace DuetDiagram.Core.Model;

/// <summary>
/// 页面定义。
/// </summary>
/// <remarks>
/// 这里只取最小字段集，等画布的多页交互开工时再补。
/// 提前猜一批字段（缩略图、页面尺寸、背景）风险更大——那些值多半会落在
/// <see cref="CanvasSettings"/> 上，猜错了要连着迁移数据。
/// </remarks>
public sealed record PageDef : IDefinition
{
    public required string Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>页面顺序。</summary>
    public int Order { get; init; }
}

/// <summary>
/// 图层定义。
/// </summary>
/// <remarks>
/// 同样只取最小集合。可见性与锁定各自有命令能改，渲染层也照它们办事
/// （见 <c>LayerPlan</c>）：可见是不画，锁定是画出来但点不中、改不了。
/// 图层样式、混合模式这类尚未确定的东西不提前加。
/// </remarks>
public sealed record LayerDef : IDefinition
{
    public required string Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    /// <summary>图层顺序。数值大的画在上面。</summary>
    public int Order { get; init; }
}
