using Avalonia;
using Avalonia.Headless;
using AppShell = DuetDiagram.App.App;

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
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessFixture)));

    /// <summary>
    /// 组装无头应用。
    /// </summary>
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
}
