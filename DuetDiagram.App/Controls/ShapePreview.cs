using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DuetDiagram.App.Rendering;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.App.Controls;

/// <summary>
/// 一个形状的小预览。
/// </summary>
/// <remarks>
/// <para>
/// 画的是形状库给的那份几何，与画布走同一个渲染器——预览里看到的形状就是画布上
/// 会画出来的形状。用图标代替文字，是因为"圆角矩形"与"胶囊"这种名字差一个字、
/// 样子差很多，靠读名字挑形状容易挑错。
/// </para>
/// <para>
/// 预览固定画成正方形。真实节点的外接矩形多半是扁的，所以预览只表达"是哪种形状"，
/// 不表达比例——比例由布局决定，挑形状的时候还看不到。
/// </para>
/// </remarks>
internal sealed class ShapePreview : Control
{
    /// <summary>要预览的形状。</summary>
    public static readonly StyledProperty<NodeShape> ShapeProperty =
        AvaloniaProperty.Register<ShapePreview, NodeShape>(nameof(Shape));

    private static readonly IBrush Outline = new ImmutableSolidColorBrush(Color.Parse("#39424f"));

    static ShapePreview()
    {
        AffectsRender<ShapePreview>(ShapeProperty);
    }

    /// <summary>要预览的形状。</summary>
    public NodeShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);

        if (side <= 0)
        {
            return;
        }

        // 正方形居中方块，四周留一点边，免得描边贴到控件边缘被裁掉。
        var inset = side * 0.12;
        var box = new Rect(
            ((Bounds.Width - side) / 2) + inset,
            ((Bounds.Height - side) / 2) + inset,
            side - (inset * 2),
            side - (inset * 2));

        ShapeGeometryRenderer.Draw(
            context,
            ShapeRegistry.Default.Find(Shape).Geometry,
            box,
            fill: null,
            new Pen(Outline, 1.4),
            styleRadius: 4);
    }
}
