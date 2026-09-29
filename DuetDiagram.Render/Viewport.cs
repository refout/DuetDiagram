namespace DuetDiagram.Render;

/// <summary>
/// 画布看文档的那扇窗：缩放倍数、平移量与窗口尺寸。
/// </summary>
/// <remarks>
/// <para>
/// 它只有状态与几个变换方法，换算本身在 <see cref="ViewportTransform"/> 里。
/// 分开是因为两者变化的原因不同：窗口尺寸每拖一下边框就变，
/// 而换算规则在很长一段时间里不会动。
/// </para>
/// <para>
/// **它是值，不是可变对象。** 每个操作都返回一份新的，调用方自己决定什么时候换上去。
/// 就地修改的写法下，"这一帧用的是改之前还是改之后的视口"取决于语句顺序，
/// 而那种 bug 只在拖动时闪一下，很难复现。
/// </para>
/// <para>
/// 缩放上下界取主题里的值而不是写死在这里：它们是产品行为，
/// 而主题已经是"这类数字的落脚处"（最小节点宽度、留白、字号都在那里）。
/// </para>
/// </remarks>
/// <param name="Scale">缩放倍数。</param>
/// <param name="OffsetX">屏幕原点对应的文档位置，按缩放后的单位计。</param>
/// <param name="OffsetY">纵向的平移量，含义同 <paramref name="OffsetX"/>。</param>
/// <param name="Width">窗口宽度，单位与光标坐标一致。</param>
/// <param name="Height">窗口高度。</param>
public sealed record Viewport(double Scale, double OffsetX, double OffsetY, double Width, double Height)
{
    /// <summary>
    /// 适配内容时四周留的空白。
    /// </summary>
    /// <remarks>
    /// 不留的话，图的边界正好压在窗口边缘上，看上去像被裁掉了。
    /// 留一个节点留白的三分之一左右就够——太大则图变小，反而看不清标签。
    /// </remarks>
    public const double FitMargin = 24;

    /// <summary>还没有尺寸、也没有内容的视口。</summary>
    public static Viewport Empty { get; } = new(1, 0, 0, 0, 0);

    /// <summary>能缩到的最小倍数。</summary>
    public double MinZoom { get; init; } = Theme.Default.MinZoom;

    /// <summary>能放到的最大倍数。</summary>
    public double MaxZoom { get; init; } = Theme.Default.MaxZoom;

    /// <summary>这一份视口对应的换算规则。</summary>
    public ViewportTransform Transform => new(Scale, OffsetX, OffsetY);

    /// <summary>
    /// 当前看得见的那块文档区域。
    /// </summary>
    /// <remarks>
    /// 裁剪要的是它，而不是 <see cref="Width"/> / <see cref="Height"/>——
    /// 那两个是屏幕尺寸，和文档里元素的位置不在一个坐标系里。
    /// </remarks>
    public SpatialRect VisibleDocumentRect =>
        Transform.ToDocument(new SpatialRect(0, 0, Width, Height));

    /// <summary>
    /// 横向滚动条的范围。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **范围以内容中心为对称中心，不以内容外接框为界。** 按外接框算的话，
    /// 图一装进窗口范围就是零、拇指占满整条轨道，拖上去一动不动——
    /// 而"图装得下"恰恰是最常见的那一档，用户会以为滚动条坏了。
    /// </para>
    /// <para>
    /// 由此还得到一条好用的性质：适配把内容摆正中间，于是拇指正好落在轨道正中间，
    /// 与内容多大无关。
    /// </para>
    /// </remarks>
    /// <param name="content">内容的范围，文档坐标。</param>
    public ScrollRange HorizontalScrollRange(SpatialRect content) =>
        RangeAlong(content.CenterX, content.Width, Width, OffsetX);

    /// <inheritdoc cref="HorizontalScrollRange(SpatialRect)"/>
    public ScrollRange VerticalScrollRange(SpatialRect content) =>
        RangeAlong(content.CenterY, content.Height, Height, OffsetY);

    /// <summary>
    /// 把内容摆到横向滚动条上的某个位置。
    /// </summary>
    /// <remarks>
    /// 它是 <see cref="HorizontalScrollRange"/> 的逆运算，两者共用同一份范围长度——
    /// 各算一次的话，浮点上会差一点点，而表现是拖动时拇指自己抖。
    /// </remarks>
    /// <param name="content">内容的范围，文档坐标。</param>
    /// <param name="value">滚动条上的位置。</param>
    public Viewport ScrollToX(SpatialRect content, double value) =>
        this with { OffsetX = OffsetAlong(content.CenterX, content.Width, Width, value) };

    /// <inheritdoc cref="ScrollToX(SpatialRect,double)"/>
    public Viewport ScrollToY(SpatialRect content, double value) =>
        this with { OffsetY = OffsetAlong(content.CenterY, content.Height, Height, value) };

    /// <summary>按一份主题取缩放上下界。</summary>
    public static Viewport For(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return Empty with { MinZoom = theme.MinZoom, MaxZoom = theme.MaxZoom };
    }

    /// <summary>换窗口尺寸。缩放与平移量不变。</summary>
    /// <remarks>
    /// 不按新尺寸重新适配：用户已经摆好的视角不该因为拖了一下窗口就变。
    /// 想看全图时他会自己按适配。
    /// </remarks>
    public Viewport Resize(double width, double height) =>
        this with { Width = Math.Max(width, 0), Height = Math.Max(height, 0) };

    /// <summary>平移。参数是屏幕上的位移。</summary>
    public Viewport PanBy(double dx, double dy) =>
        this with { OffsetX = OffsetX + dx, OffsetY = OffsetY + dy };

    /// <summary>
    /// 缩放，并让锚点底下的那个文档位置留在原处。
    /// </summary>
    /// <remarks>
    /// 钳制在换算**之前**做，这样顶到上下界时锚点仍然不动。
    /// 先换算再钳制的话，倍数被改小、平移量却按改之前的倍数算过，
    /// 表现是滚到极限之后图会自己往一边滑。
    /// </remarks>
    /// <param name="factor">倍数变化量。大于一是放大。</param>
    /// <param name="anchorX">锚点的屏幕横坐标。</param>
    /// <param name="anchorY">锚点的屏幕纵坐标。</param>
    public Viewport ZoomAt(double factor, double anchorX, double anchorY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);

        var target = Math.Clamp(Scale * factor, MinZoom, MaxZoom);
        var moved = Transform.WithScaleAt(target, anchorX, anchorY);

        return this with { Scale = moved.Scale, OffsetX = moved.OffsetX, OffsetY = moved.OffsetY };
    }

    /// <summary>把内容整个放进视口并居中，四周留 <see cref="FitMargin"/>。</summary>
    public Viewport FitTo(SpatialRect content) => FitTo(content, FitMargin);

    /// <summary>
    /// 把内容整个放进视口并居中。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 缩放倍数同样钳制：内容只有一个点大时，不钳制会放成几百倍，
    /// 用户看到的是一个被放大的空白。
    /// </para>
    /// <para>
    /// 宽高为零的内容仍然居中，只是倍数沿用当前值。空文档就走这条路径——
    /// 它没有可适配的范围，但视口不该因此跳到一个随机的位置。
    /// </para>
    /// </remarks>
    public Viewport FitTo(SpatialRect content, double margin)
    {
        if (Width <= 0 || Height <= 0)
        {
            return this;
        }

        var usableWidth = Math.Max(Width - (margin * 2), 1);
        var usableHeight = Math.Max(Height - (margin * 2), 1);

        var scale = content.Width > 0 && content.Height > 0
            ? Math.Clamp(
                Math.Min(usableWidth / content.Width, usableHeight / content.Height),
                MinZoom,
                MaxZoom)
            : Math.Clamp(Scale, MinZoom, MaxZoom);

        return this with
        {
            Scale = scale,
            OffsetX = ((Width - (content.Width * scale)) / 2) - (content.X * scale),
            OffsetY = ((Height - (content.Height * scale)) / 2) - (content.Y * scale),
        };
    }

    /// <summary>
    /// 内容之外留的那圈余量。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 取 <see cref="FitMargin"/> 与四分之一视口的较大者。四分之一那一项是为了让
    /// **图完整显示时视图还能移动半个视口**（可滚距离正好是视口的一半）——
    /// 再小则拖上去像没反应，再大则内容能被整个推出屏幕、用户找不回来。
    /// </para>
    /// <para>
    /// <see cref="FitMargin"/> 只在视口极小（不到它的四倍）时接管，那时按比例算出来的余量
    /// 小得没有意义。
    /// </para>
    /// </remarks>
    private static double Pad(double viewportLength) => Math.Max(FitMargin, viewportLength / 4);

    /// <summary>滚动范围的总长，屏幕单位。正反解都走这一份，不然两者会在浮点上分叉。</summary>
    private static double SurfaceLength(double contentLength, double viewportLength) =>
        Math.Max(contentLength, viewportLength) + (Pad(viewportLength) * 2);

    /// <summary>沿一轴把内容中心的位置换成滚动条上的位置。</summary>
    private ScrollRange RangeAlong(double center, double contentLength, double viewportLength, double offset)
    {
        var surface = SurfaceLength(contentLength * Scale, viewportLength);
        var centerOnScreen = (center * Scale) + offset;

        return new ScrollRange(surface - viewportLength, viewportLength, (surface / 2) - centerOnScreen);
    }

    /// <summary>沿一轴把滚动条上的位置换回内容中心该在的屏幕位置。</summary>
    private double OffsetAlong(double center, double contentLength, double viewportLength, double value)
    {
        var surface = SurfaceLength(contentLength * Scale, viewportLength);

        return (surface / 2) - (center * Scale) - value;
    }
}
