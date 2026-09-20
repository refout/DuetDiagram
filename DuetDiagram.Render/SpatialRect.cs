namespace DuetDiagram.Render;

/// <summary>
/// 轴对齐的矩形。
/// </summary>
/// <remarks>
/// 用值类型而不是记录类：索引里会存成千上万个它，引用类型会让每个矩形多一次分配与一次解引用。
/// 值类型在这里没有语义上的代价——矩形就是四个数，没有身份可言。
/// </remarks>
public readonly record struct SpatialRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public double Area => Width * Height;

    /// <summary>是否与另一个矩形相交。边框相接算相交。</summary>
    /// <remarks>
    /// 相接算相交而不是不算：视口裁剪宁可多画一个贴边的元素，也不能漏画。
    /// 漏画的表现是"拖动时边缘的元素一闪一闪"，而多画一个的开销可以忽略。
    /// </remarks>
    public bool Intersects(SpatialRect other) =>
        X <= other.Right && Right >= other.X && Y <= other.Bottom && Bottom >= other.Y;

    /// <summary>是否完全包含另一个矩形。</summary>
    public bool Contains(SpatialRect other) =>
        other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;

    /// <summary>是否包含一个点。</summary>
    public bool Contains(double px, double py) =>
        px >= X && px <= Right && py >= Y && py <= Bottom;

    /// <summary>向外扩一圈。视口预取用它。</summary>
    public SpatialRect Inflate(double margin) =>
        new(X - margin, Y - margin, Width + (margin * 2), Height + (margin * 2));

    /// <summary>把另一个矩形并进来，得到能容纳两者的最小矩形。</summary>
    public SpatialRect Union(SpatialRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);

        return new SpatialRect(left, top, right - left, bottom - top);
    }

    public static SpatialRect FromCorners(double x0, double y0, double x1, double y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));

    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Width:0.##}×{Height:0.##})";
}
