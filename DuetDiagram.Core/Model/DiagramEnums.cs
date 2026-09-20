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

/// <summary>端口所在的边。</summary>
public enum PortSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>连线的走线方式。</summary>
public enum EdgeRoute
{
    /// <summary>正交折线。分层布局的默认选择。</summary>
    Orthogonal,

    /// <summary>曲线。手工绘制的自由连线用它。</summary>
    Curved,

    /// <summary>两点直线。</summary>
    Straight,
}

/// <summary>边标签沿连线的位置。</summary>
public enum LabelPosition
{
    Start,
    Middle,
    End,
}

/// <summary>水平对齐。</summary>
public enum TextAlign
{
    Start,
    Center,
    End,
}

/// <summary>垂直对齐。</summary>
public enum VerticalAlign
{
    Start,
    Middle,
    End,
}

/// <summary>文字方向。</summary>
public enum WritingDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
}

/// <summary>字重。只分常规与加粗两档，够用且不给字体匹配增加歧义。</summary>
public enum FontWeight
{
    Normal,
    Bold,
}

/// <summary>画布网格样式。</summary>
public enum GridStyle
{
    None,
    Lines,
    Dots,
}

/// <summary>纸张方向。</summary>
public enum CanvasOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// 约束的归属方。
/// </summary>
/// <remarks>
/// 这个区分决定了约束的优先级与可覆盖性：人工设定的约束不该被自动重排冲掉，
/// 而模型或引擎推导出来的约束在用户拖动之后应当让位。
/// </remarks>
public enum ConstraintOwner
{
    /// <summary>引擎自动推导。</summary>
    Auto,

    /// <summary>模型给出。</summary>
    Llm,

    /// <summary>人工设定。</summary>
    Human,
}
