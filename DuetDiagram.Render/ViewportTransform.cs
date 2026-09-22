namespace DuetDiagram.Render;

/// <summary>
/// 文档坐标与屏幕坐标之间的换算。
/// </summary>
/// <remarks>
/// <para>
/// **纯函数，不持有状态。** 缩放倍数与平移量就是构造它的三个数，换算过程只读它们。
/// 做成有状态的对象之后，渲染路径与命中测试会各自维护一份"当前视口"，
/// 两份迟早差一点点，而表现是"点不中元素"——那种偏差在画面上完全看不出来。
/// </para>
/// <para>
/// 只有等比缩放与平移，没有旋转与错切。旋转在流程图上没有用武之地，
/// 而多两个自由度会把"坐标往返"从一行乘法变成矩阵求逆，
/// 命中测试的误差也就跟着放大。
/// </para>
/// </remarks>
/// <param name="Scale">缩放倍数。恒为正数，视口负责钳制它的上下界。</param>
/// <param name="OffsetX">屏幕原点对应的文档位置，按缩放后的单位计。</param>
/// <param name="OffsetY">纵向的平移量，含义同 <paramref name="OffsetX"/>。</param>
public readonly record struct ViewportTransform(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>原样映射：一倍缩放、不平移。</summary>
    public static ViewportTransform Identity { get; } = new(1, 0, 0);

    /// <summary>文档坐标转屏幕坐标。</summary>
    public DrawPoint ToScreen(double x, double y) =>
        new((x * Scale) + OffsetX, (y * Scale) + OffsetY);

    /// <inheritdoc cref="ToScreen(double,double)"/>
    public DrawPoint ToScreen(DrawPoint point) => ToScreen(point.X, point.Y);

    /// <summary>屏幕坐标转文档坐标。</summary>
    public DrawPoint ToDocument(double x, double y) =>
        new((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    /// <inheritdoc cref="ToDocument(double,double)"/>
    public DrawPoint ToDocument(DrawPoint point) => ToDocument(point.X, point.Y);

    /// <summary>
    /// 把一个矩形换算到屏幕坐标。
    /// </summary>
    /// <remarks>
    /// 只换算左上角再按倍数缩放宽高，不换算两个对角点。
    /// 两个对角点分别换算再相减，多一次乘加也就多一份浮点误差，
    /// 而误差在裁剪判断上会变成"边缘元素一闪一闪"。
    /// </remarks>
    public SpatialRect ToScreen(SpatialRect rect)
    {
        var origin = ToScreen(rect.X, rect.Y);

        return new SpatialRect(origin.X, origin.Y, rect.Width * Scale, rect.Height * Scale);
    }

    /// <inheritdoc cref="ToScreen(SpatialRect)"/>
    public SpatialRect ToDocument(SpatialRect rect)
    {
        var origin = ToDocument(rect.X, rect.Y);

        return new SpatialRect(origin.X, origin.Y, rect.Width / Scale, rect.Height / Scale);
    }

    /// <summary>
    /// 换一个缩放倍数，并让屏幕上的某个点始终对着同一个文档位置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 锚点取**屏幕坐标**，因为光标位置本来就是屏幕坐标，换算一步都不用多做。
    /// 以视口中心为锚点的话，用户想看清的那一处会随着缩放跑出视口，
    /// 于是只能一边滚滚轮一边往回拖。
    /// </para>
    /// <para>
    /// 做法是先记下锚点底下的文档位置，缩放之后再把那个位置挪回锚点上。
    /// 直接按倍数缩放平移量是错的——那样锚点会漂。
    /// </para>
    /// </remarks>
    /// <param name="scale">新的缩放倍数。</param>
    /// <param name="anchorX">锚点的屏幕横坐标。</param>
    /// <param name="anchorY">锚点的屏幕纵坐标。</param>
    public ViewportTransform WithScaleAt(double scale, double anchorX, double anchorY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        var anchor = ToDocument(anchorX, anchorY);
        var scaled = new ViewportTransform(scale, 0, 0).ToScreen(anchor);

        return new ViewportTransform(scale, anchorX - scaled.X, anchorY - scaled.Y);
    }

    /// <summary>平移。参数是屏幕上的位移，与光标位移同一单位。</summary>
    public ViewportTransform Translated(double dx, double dy) =>
        new(Scale, OffsetX + dx, OffsetY + dy);

    public override string ToString() =>
        $"scale={Numbers.Format(Scale)} offset=({Numbers.Format(OffsetX)}, {Numbers.Format(OffsetY)})";
}
