using System.ComponentModel;
using System.Globalization;
using DuetDiagram.Render;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 画布的状态：画什么、看哪儿。
/// </summary>
/// <remarks>
/// <para>
/// **它只认绘制列表，不认文档。** 文档到绘制列表那一段在渲染层，
/// 画布这一侧再读一次 IR 的话，渲染路径就绕过了绘制列表——
/// 快照测试覆盖到的东西与实际画出来的东西会分叉，而两边都"通过"。
/// </para>
/// <para>
/// 视口在这里而不是在控件里：状态栏要显示坐标与缩放，而状态栏与画布是两个控件。
/// 状态放在其中一个控件里，另一个就只能靠回调去要，那样的耦合迟早会绕成一团。
/// </para>
/// </remarks>
public sealed class CanvasViewModel : INotifyPropertyChanged
{
    private DrawList _drawList = DrawList.Empty;
    private Viewport _viewport;
    private DrawPoint _pointer;
    private bool _pointerInside;

    public CanvasViewModel(Theme? theme = null)
    {
        Theme = theme ?? Theme.Default;
        _viewport = Viewport.For(Theme);
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>外观查表。缩放上下界也从它取。</summary>
    public Theme Theme { get; }

    /// <summary>要画的东西。</summary>
    public DrawList DrawList
    {
        get => _drawList;
        private set => Set(ref _drawList, value, nameof(DrawList), nameof(IsEmpty));
    }

    /// <summary>看哪儿。</summary>
    public Viewport Viewport
    {
        get => _viewport;
        private set => Set(ref _viewport, value, nameof(Viewport), nameof(ZoomText));
    }

    /// <summary>没有可画的东西。界面据此显示一句提示而不是一片空白。</summary>
    public bool IsEmpty => _drawList.Commands.Count == 0;

    /// <summary>光标在文档里的位置。光标不在画布上时给出一个占位符。</summary>
    public string PointerText =>
        _pointerInside
            ? string.Format(CultureInfo.InvariantCulture, "x {0:0.#}   y {1:0.#}", _pointer.X, _pointer.Y)
            : "—";

    /// <summary>当前缩放倍数。</summary>
    /// <remarks>
    /// 用百分比而不是倍数：用户判断"能不能看清"看的是百分比，
    /// 而 0.35 这种写法要先在心里乘一百。
    /// </remarks>
    public string ZoomText =>
        string.Format(CultureInfo.InvariantCulture, "缩放 {0:0}%", _viewport.Scale * 100);

    /// <summary>
    /// 换一份绘制列表，并把视口适配到内容上。
    /// </summary>
    /// <remarks>
    /// 每次换列表都重新适配：保留上一次的视角的话，换一份内容差得远的文档之后
    /// 用户看到的是一片空白，只能自己摸索着找回来。
    /// </remarks>
    public void Load(DrawList drawList)
    {
        ArgumentNullException.ThrowIfNull(drawList);

        DrawList = drawList;
        Viewport = _viewport.FitTo(ContentBounds);
    }

    /// <summary>
    /// 窗口尺寸变了。
    /// </summary>
    /// <remarks>
    /// 第一次拿到尺寸时补一次适配。绘制列表通常在窗口排布之前就装进来了，
    /// 那时没有尺寸可适配，适配那一步只能空转；不补的话，内容会停在左上角，
    /// 看上去像视口算错了，而其实是它根本没适配过。
    /// 之后每次改尺寸都不再适配——用户已经摆好的视角不该因为拖了一下边框就变。
    /// </remarks>
    public void Resize(double width, double height)
    {
        var measured = _viewport.Width > 0 && _viewport.Height > 0;
        var resized = _viewport.Resize(width, height);

        Viewport = measured ? resized : resized.FitTo(ContentBounds);
    }

    /// <summary>以光标为锚点缩放。</summary>
    public void ZoomAt(double factor, double anchorX, double anchorY) =>
        Viewport = _viewport.ZoomAt(factor, anchorX, anchorY);

    /// <summary>平移。参数是屏幕上的位移。</summary>
    public void PanBy(double dx, double dy) => Viewport = _viewport.PanBy(dx, dy);

    /// <summary>光标移到了画布上的某个位置。参数是屏幕坐标。</summary>
    public void MovePointer(double screenX, double screenY)
    {
        var document = _viewport.Transform.ToDocument(screenX, screenY);

        // 同一个位置重复上报是常态：指针事件比像素密得多。
        // 每次都通知一遍会让状态栏每帧重排一次，而它显示的东西根本没变。
        if (_pointerInside && _pointer.Equals(document))
        {
            return;
        }

        _pointerInside = true;
        _pointer = document;
        Raise(nameof(PointerText));
    }

    /// <summary>光标离开了画布。</summary>
    public void LeavePointer()
    {
        if (!_pointerInside)
        {
            return;
        }

        _pointerInside = false;
        Raise(nameof(PointerText));
    }

    private void Set<T>(ref T field, T value, params string[] names)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        foreach (var name in names)
        {
            Raise(name);
        }
    }

    /// <summary>内容的范围。绘制列表的宽高就是它。</summary>
    private SpatialRect ContentBounds => new(0, 0, _drawList.Width, _drawList.Height);

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
