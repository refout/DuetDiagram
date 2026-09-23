using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.Render;
using AppShell = DuetDiagram.App.App;

[assembly: AvaloniaTestApplication(typeof(DuetDiagram.E2E.Tests.HeadlessFixture))]

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 无头模式下的界面宿主。
/// </summary>
/// <remarks>
/// <para>
/// 界面框架的测试要在没有显示器的机器上跑，靠的是把平台后端换成一个不画到屏幕上的实现。
/// 这个类就是那个替换点：测试框架按这里的 <see cref="BuildAvaloniaApp"/> 去组装应用，
/// 拿到什么就用什么启动。
/// </para>
/// <para>
/// **它启动的是主程序自己的那个应用类型**，不是另起一个。主题、字体与资源都在那里装着，
/// 另起一个的话，测的是一套界面，跑的是另一套。
/// </para>
/// <para>
/// 绘制走真实的绘图后端而不是空实现：空实现下抓到的帧永远是空白，
/// "画出来了没有"这件事就断言不了，而它恰恰是这一层要验的东西。
/// </para>
/// <para>
/// **界面对象只能在界面线程上碰。** 所有测试体都要经由 <see cref="Run"/> 交给会话，
/// 它负责把动作排到那条线程上执行。直接在自己的线程上 new 一个窗口会随机失败——
/// 有时能跑通，有时抛一句"不在正确的线程上"，而那种失败很难与真正的缺陷区分开。
/// </para>
/// <para>
/// 这里没有用配套的 XUnit 集成包：它绑定的是低一个大版本的测试框架，
/// 与仓库里现有的版本对不上，装上去在发现阶段就抛"找不到方法"。
/// 会话本身就是几十行的东西，直接用反而少一处会随版本漂移的依赖。
/// </para>
/// </remarks>
public static class HeadlessFixture
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessFixture).Assembly));

    /// <summary>
    /// 组装无头应用。
    /// </summary>
    /// <remarks>
    /// 无头平台只换掉"窗口从哪来"，绘图后端仍然要自己装上。
    /// 不装的话应用起不来，报的是"找不到字体管理器"——那句话与真正的原因
    /// 隔了一层，容易让人去查字体而不是查后端。
    /// </remarks>
    /// <summary>组装无头应用。</summary>
    /// <remarks>
    /// 无头平台只换掉"窗口从哪来"，绘图后端仍然要自己装上。
    /// 不装的话应用起不来，报的是"找不到字体管理器"——那句话与真正的原因
    /// 隔了一层，容易让人去查字体而不是查后端。
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AppShell>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont();

    /// <summary>在界面线程上跑一段测试体。</summary>
    /// <param name="body">测试体。</param>
    public static Task Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Session.Value.Dispatch(body, CancellationToken.None);
    }

    #region 窗口与画布

    /// <summary>
    /// 起一个窗口，并把测量、排布与渲染都跑完。
    /// </summary>
    /// <remarks>
    /// 那一帧不能省：不起的话画布尺寸还是零，视口也就没被适配过，
    /// 之后量出来的坐标与窗口位置全是错的，而失败会指向视口而不是"还没排布"。
    /// </remarks>
    public static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    /// <summary>窗口里那个画布。</summary>
    public static DiagramCanvas Canvas(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<DiagramCanvas>().Single();
    }

    /// <summary>窗口里那个诊断面板。</summary>
    public static DiagnosticsPanel Panel(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<DiagnosticsPanel>().Single();
    }

    /// <summary>窗口里那个属性面板。</summary>
    public static PropertyPanel Properties(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<PropertyPanel>().Single();
    }

    /// <summary>窗口里那个图层面板。</summary>
    public static LayerPanel Layers(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<LayerPanel>().Single();
    }

    /// <summary>窗口里那条工具栏。</summary>
    public static DiagramToolBar ToolBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<DiagramToolBar>().Single();
    }

    /// <summary>窗口里那条菜单栏。</summary>
    public static DiagramMenuBar MenuBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants().OfType<DiagramMenuBar>().Single();
    }

    /// <summary>
    /// 某个字段在界面上对应的那个编辑器。
    /// </summary>
    /// <remarks>
    /// 按控件类型加字段名去找，不按视觉树里的次序：次序一调整就会找错控件，
    /// 而那种错不会让测试失败，只会让它去验另一个字段。
    /// </remarks>
    public static T Editor<T>(Window window, string field)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.GetVisualDescendants()
            .OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == field);
    }

    /// <summary>
    /// 某个元素在画布上的中心点，按画布坐标。
    /// </summary>
    /// <remarks>
    /// 取它全部绘制指令的并集，与选中框用的是同一套口径。只取第一条的话，
    /// 标签比形状宽时算出来的中心会偏出去，而点在那儿命中的是空白——
    /// 于是用例失败在"没选中"上，看起来像选中坏了。
    /// </remarks>
    public static Point CenterOf(DiagramCanvas canvas, string elementId)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var model = canvas.Model
            ?? throw new InvalidOperationException("画布还没有数据上下文");

        SpatialRect? bounds = null;

        foreach (var command in model.DrawList.Commands)
        {
            if (!string.Equals(command.ElementId, elementId, StringComparison.Ordinal))
            {
                continue;
            }

            var box = command switch
            {
                DrawShape shape => shape.Rect,
                DrawText text => text.Box,
                DrawPolyline polyline => Box(polyline.Points),
                _ => (SpatialRect?)null,
            };

            if (box is { } value)
            {
                bounds = bounds is { } current ? current.Union(value) : value;
            }
        }

        if (bounds is not { } rect)
        {
            throw new InvalidOperationException($"绘制列表里没有 {elementId} 这个元素");
        }

        var onScreen = model.Viewport.Transform.ToScreen(rect);

        return new Point(onScreen.CenterX, onScreen.CenterY);
    }

    private static SpatialRect Box(IReadOnlyList<DrawPoint> points)
    {
        var left = points[0].X;
        var top = points[0].Y;
        var right = left;
        var bottom = top;

        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new SpatialRect(left, top, right - left, bottom - top);
    }

    /// <summary>画布中心，按画布自己的坐标算。</summary>
    public static Point CenterOf(DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        return new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
    }

    /// <summary>把画布坐标换成窗口坐标。指针事件给的是窗口坐标。</summary>
    public static Point ToWindow(DiagramCanvas canvas, Window window, Point local)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(window);

        return canvas.TranslatePoint(local, window)
            ?? throw new InvalidOperationException("画布不在窗口的视觉树里，量不出它在窗口里的位置");
    }

    /// <summary>
    /// 让窗口真的重画一帧，并交出那一帧的位图。
    /// </summary>
    /// <remarks>
    /// 作废那一步不能省。交给"上一步恰好让某个属性变了"的话，一旦以后那个属性不再变，
    /// 这里就会静默地少渲染一帧——而少掉的那一帧正好是要断言的那一帧，
    /// 于是断言落在上一帧的残留状态上，测试仍然全绿。
    /// </remarks>
    public static WriteableBitmap? Frame(MainWindow window, DiagramCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(canvas);

        canvas.InvalidateVisual();

        return window.CaptureRenderedFrame();
    }

    #endregion
}
