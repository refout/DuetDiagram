namespace DuetDiagram.Render;

/// <summary>
/// 一条滚动条要的三个数：能滚多远、看得见多长、现在滚到哪儿。
/// </summary>
/// <remarks>
/// <para>
/// 三个数都是屏幕单位，与光标坐标同一套。它只描述"条"这一侧，
/// 不持有视口——视口与条之间的换算在 <see cref="Viewport"/> 里，
/// 放在那边是因为换算是从视口的缩放与平移量出发的。
/// </para>
/// <para>
/// **它是值，不是可变对象**，理由与 <see cref="Viewport"/> 相同：
/// 就地修改的写法下"这一帧读的是改前还是改后"取决于语句顺序，
/// 而那种 bug 只在拖动时闪一下，很难复现。
/// </para>
/// </remarks>
public readonly record struct ScrollRange
{
    /// <summary>
    /// 建一条范围，顺手把三个数收进合法区间。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **钳的是滚动条上的位置，不是视口平移量。** 平移是无边界的——
    /// 用户把图拖出屏幕之后，滚动条只能表达"到头了"，而不是跟着记一个超出范围的位置。
    /// 钳在这里而不是钳在调用方：调用方有好几处（模型每帧读一次），
    /// 漏掉任何一处，模型与控件就会各说一个数，而表现是拇指和视图对不上。
    /// </para>
    /// <para>
    /// 视口长度取零下界：首次排布之前视口的宽高都是零，
    /// 那时这条范围仍然要能算出来，不能出现负数或 NaN。
    /// </para>
    /// </remarks>
    /// <param name="maximum">能滚动的距离。</param>
    /// <param name="viewportLength">视口在这一轴上的长度。</param>
    /// <param name="value">当前滚到的位置。</param>
    public ScrollRange(double maximum, double viewportLength, double value)
    {
        Maximum = Math.Max(maximum, 0);
        ViewportLength = Math.Max(viewportLength, 0);
        Value = Math.Clamp(value, 0, Maximum);
    }

    /// <summary>
    /// 能滚动的距离。
    /// </summary>
    /// <remarks>
    /// 拇指在轨道上占多大，由它与 <see cref="ViewportLength"/> 一起定：
    /// 两者之比就是"看得见的那一段占全部内容的多少"。
    /// </remarks>
    public double Maximum { get; }

    /// <summary>视口在这一轴上的长度。</summary>
    public double ViewportLength { get; }

    /// <summary>当前滚到哪儿，恒在 <c>[0, Maximum]</c> 之内。</summary>
    public double Value { get; }
}
