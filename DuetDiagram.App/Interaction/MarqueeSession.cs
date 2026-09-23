using DuetDiagram.Render;

namespace DuetDiagram.App.Interaction;

/// <summary>
/// 一次框选：按在空白处、拖动、松手。
/// </summary>
/// <remarks>
/// <para>
/// **框选期间不改文档，也不改选中。** 拖动中只更新叠加层上的那个选框，
/// 松手才算一次选中。与拖拽同一条口径——边选边改的话，撤销要按很多次，
/// 而用户眼里是一次框选。
/// </para>
/// <para>
/// **按在元素上不进入框选。** 那种按下要么是拖节点、要么是点选，
/// 而从元素上拖出一个选框不是任何人的预期。
/// </para>
/// <para>
/// **判据与命中测试同一条**（<see cref="HitTester.Visible"/>）：同一份绘制列表、
/// 同样跳过那份"画出来但点不中"的名单。于是锁定的、别的图层藏起来的、
/// 别的页面上的元素都框不进来——命中测试跳过它们，框选也必须跳过，
/// 否则锁上之后还能被批量改。
/// </para>
/// </remarks>
public sealed class MarqueeSession
{
    /// <summary>
    /// 超过多少个屏幕像素才算框选。
    /// </summary>
    /// <remarks>
    /// 比它短的一拖算点选：按在空白处再松手就是"清空选中"，
    /// 而手指或鼠标在按下时抖一两个像素是常事。给零的话，用户想清空选中却框出一个空选框，
    /// 结果一样但看上去像没反应。
    /// </remarks>
    public const double ThresholdPixels = 3;

    private readonly DrawPoint _anchor;
    private readonly double _threshold;

    /// <summary>开始一次框选。</summary>
    /// <param name="anchor">按下的那一点，文档坐标。</param>
    /// <param name="additive">是否增选模式：松手时并进已有选中，而不是换掉它。</param>
    /// <param name="scale">视口缩放倍数，用来把屏幕上的阈值换成文档单位。</param>
    public MarqueeSession(DrawPoint anchor, bool additive, double scale)
    {
        _anchor = anchor;
        Additive = additive;

        // 缩放倍数越大，同样的屏幕距离对应越小的文档距离。不换算的话，
        // 放大之后轻轻一抖就被当成框选，缩小之后拖动一大段却还算点选。
        _threshold = scale <= 0 ? ThresholdPixels : ThresholdPixels / scale;
    }

    /// <summary>松手时是并进已有选中，还是换掉它。</summary>
    public bool Additive { get; }

    /// <summary>这一拖有没有超过阈值，也就是到底算不算框选。</summary>
    public bool IsMarquee { get; private set; }

    /// <summary>
    /// 指针移到了这里。
    /// </summary>
    /// <param name="current">当前指针位置，文档坐标。</param>
    /// <returns>选框。还没超过阈值时返回空——那时它还是一个"点"。</returns>
    public SpatialRect? Move(DrawPoint current)
    {
        var dx = current.X - _anchor.X;
        var dy = current.Y - _anchor.Y;

        if (!IsMarquee && Math.Sqrt((dx * dx) + (dy * dy)) < _threshold)
        {
            return null;
        }

        IsMarquee = true;

        return SpatialRect.FromCorners(_anchor.X, _anchor.Y, current.X, current.Y);
    }

    /// <summary>选框里有哪些元素。</summary>
    public IReadOnlyList<string> Ids(DrawList list, SpatialRect area)
    {
        ArgumentNullException.ThrowIfNull(list);

        return HitTester.Visible(list.Commands, area, list.Blocked);
    }
}
