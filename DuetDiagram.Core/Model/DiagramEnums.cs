namespace DuetDiagram.Core.Model;

/// <summary>图类型。对应 IR 的 <see cref="DiagramDocument.Kind"/>。</summary>
public enum DiagramKind
{
    Flow,
    Flowchart,
    Block,
    State,
}

/// <summary>主方向，是方向的唯一来源。</summary>
public enum Direction
{
    LR,
    TB,
    RL,
    BT,
}

/// <summary>节点形状。</summary>
public enum NodeShape
{
    Rect,
    Rounded,
    Stadium,
    Diamond,
    Circle,
    Hexagon,
    Parallelogram,
    Cylinder,
}

/// <summary>边线型。</summary>
public enum LineStyle
{
    Solid,
    Dashed,
    Dotted,
}

/// <summary>边箭头。</summary>
public enum ArrowStyle
{
    None,
    Arrow,
    OpenArrow,
    Circle,
    Cross,
}
