using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using DuetDiagram.App;
using DuetDiagram.App.Controls;
using DuetDiagram.App.Services;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Layout;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 布局全部降级失败之后，画面、状态栏与那三个选项各自是什么样子。
/// </summary>
/// <remarks>
/// <para>
/// **画面不清空。** 保留上一次成功的结果，清空会让用户以为图丢了——
/// 于是他去找备份，而真正该做的是去掉那条算不出来的约束。
/// </para>
/// <para>
/// 用会失败的引擎来造这条路径。在真实引擎上构造一份它解不出的图，
/// 那条路径会随引擎的改进而失效；而"失败之后界面怎么办"这件事不能没人走。
/// </para>
/// </remarks>
public sealed class LayoutFailureTests
{
    #region 画面

    /// <summary>布局失败之后，绘制列表还是上一次成功的那一份。</summary>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task The_picture_stays_on_the_last_good_layout()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            using var session = new DiagramSession(SampleDiagram.Document(), engine: engine);

            session.LayoutFailure.Should().BeNull("第一次布局是算得出来的");

            var before = session.Scene;

            engine.Failing = true;
            session.Reload();

            session.LayoutFailure.Should().NotBeNull();
            session.LayoutFailure!.Error.Code.Should().Be(ErrorCodes.LayoutAllLevelsTimeout);
            session.Scene.Should().BeSameAs(before, "算不出来的时候画面不能动");
        });
    }

    /// <summary>降级过程中被丢掉的约束被记了下来。</summary>
    /// <remarks>
    /// 用户要做的第一件事是"去掉那条算不出来的约束"，而要知道去掉哪一条，
    /// 就得先看到这一轮丢了哪些。
    /// </remarks>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task The_dropped_constraints_are_reported()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            using var session = new DiagramSession(ConstrainedDocument(), engine: engine);

            engine.Failing = true;
            session.Reload();

            session.ConflictSummary.Should().NotBeEmpty("这一轮丢掉了模型提的同层约束");
            session.ConflictSummary.Should().Contain(text => text.Contains("同层", StringComparison.Ordinal));
        });
    }

    #endregion

    #region 窗口上的三选项

    /// <summary>状态栏变红，三个选项都在，画面不动。</summary>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task The_window_shows_a_red_bar_and_three_ways_out()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            var window = Open(engine);
            var dialog = Dialog(window);
            var before = window.Session.Scene;

            dialog.IsVisible.Should().BeFalse("没出事的时候不该挡着图");

            engine.Failing = true;
            window.Session.Reload();

            window.Session.Scene.Should().BeSameAs(before, "画面停在上一次成功的结果上");

            window.Status.HasMessage.Should().BeTrue();
            window.Status.Severity.Should().Be(StatusSeverity.Error, "布局失败是红的");
            window.Status.Message.Should().Contain("布局");

            window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Should()
                .Contain(block => block.IsVisible && block.Text == window.Status.Message);

            dialog.IsVisible.Should().BeTrue();

            Labels(dialog).Should().Equal("重试", "手动布局", "简化图");

            window.Close();
        });
    }

    /// <summary>
    /// 简化图把这一轮丢掉的约束摆出来，而不是替用户去掉一条。
    /// </summary>
    /// <remarks>
    /// 自动替用户去掉一条的话，去掉的可能是他真正在乎的那条，
    /// 而重排之后图变了却看不出是为什么。
    /// </remarks>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task Simplify_points_at_the_constraints_instead_of_dropping_them()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            var window = Open(engine);
            var dialog = Dialog(window);

            engine.Failing = true;
            window.Session.Reload();

            var detail = Detail(dialog);
            var constraints = window.Session.Document.Layout.SameRank.Count;

            Click(dialog, "简化图");

            Detail(dialog).Should().NotBe(detail, "按了就要有变化，否则这个按钮是坏的");
            Detail(dialog).Should().Contain(
                constraints == 0 ? "没有丢掉任何约束" : "同层",
                "要么如实说没丢约束，要么把丢掉的那几条摆出来");

            window.Session.Document.Layout.SameRank.Should().HaveCount(
                constraints,
                "界面只负责指出来，去掉哪一条是用户的判断");

            window.Close();
        });
    }

    /// <summary>重试成功之后，提示收起来，状态栏不再是红的。</summary>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task A_successful_retry_takes_the_warning_down()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            var window = Open(engine);
            var dialog = Dialog(window);

            engine.Failing = true;
            window.Session.Reload();

            dialog.IsVisible.Should().BeTrue();
            window.Status.Severity.Should().Be(StatusSeverity.Error);

            engine.Failing = false;
            Click(dialog, "重试");

            window.Session.LayoutFailure.Should().BeNull();
            dialog.IsVisible.Should().BeFalse("图已经排出来了，那条红字还挂着的话看起来像失败还在");
            window.Status.HasMessage.Should().BeFalse();

            window.Close();
        });
    }

    /// <summary>选了手动布局之后，改元素不再去问引擎，位置也不动。</summary>
    [Fact]
    [Trait("Category", "LayoutFailure")]
    public async Task Manual_layout_stops_asking_the_engine()
    {
        await HeadlessFixture.Run(() =>
        {
            var engine = new SwitchableEngine();
            var window = Open(engine);

            engine.Failing = true;
            window.Session.Reload();

            Click(Dialog(window), "手动布局");

            window.Session.ManualLayoutActive.Should().BeTrue();
            window.Session.LayoutFailure.Should().BeNull();

            var calls = engine.Calls;
            var placed = window.Session.Scene.Layout.Find("start");

            placed.Should().NotBeNull();

            window.Session.Select("start");
            window.Session.Apply("label", "起点").IsEffectiveSuccess.Should().BeTrue();

            engine.Calls.Should().Be(calls, "手动布局模式下不该再去问引擎");

            var after = window.Session.Scene.Layout.Find("start");

            after.Should().NotBeNull();
            after!.X.Should().Be(placed!.X, "位置冻在最近一次成功的布局上");
            after.Y.Should().Be(placed.Y);

            window.Session.Scene.DrawList.Commands
                .OfType<DrawText>()
                .Should()
                .Contain(text => text.Text == "起点", "只冻坐标，不连内容一起冻");

            window.Close();
        });
    }

    #endregion

    #region 造场景

    /// <summary>带一条模型提的同层约束的示例文档。降级时它会被丢掉，于是有得可报。</summary>
    private static DiagramDocument ConstrainedDocument() => DiagramDocument.CreateFromContent(
        "sample",
        DiagramKind.Flowchart,
        Direction.TB,
        nodes:
        [
            new NodeDef { Id = "start", Label = "开始" },
            new NodeDef { Id = "pass", Label = "通过" },
            new NodeDef { Id = "fail", Label = "失败" },
        ],
        layout: new LayoutHints
        {
            SameRank =
            [
                new Constraint<SameRankConstraint>(
                    new SameRankConstraint(["pass", "fail"]),
                    ConstraintOwner.Llm,
                    DateTimeOffset.UnixEpoch),
            ],
        });

    private static MainWindow Open(ILayoutEngine engine)
    {
        var window = new MainWindow(engine);

        window.Show();
        window.CaptureRenderedFrame();

        return window;
    }

    private static LayoutFailureDialog Dialog(MainWindow window) =>
        window.GetLogicalDescendants().OfType<LayoutFailureDialog>().Single();

    private static List<string?> Labels(LayoutFailureDialog dialog) =>
        [.. dialog.GetLogicalDescendants().OfType<Button>().Select(button => button.Content as string)];

    /// <summary>提示上那句补充说明。它在提示里是唯一一段会自动换行的字。</summary>
    private static string Detail(LayoutFailureDialog dialog) =>
        dialog.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Single(block => block.TextWrapping == TextWrapping.Wrap)
            .Text ?? string.Empty;

    /// <summary>
    /// 按下按钮，按它上面那行字找。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按文字找而不是按逻辑树里的次序：次序一调整就会按到另一个按钮上，
    /// 而那种错不会让测试失败，只会让它去验另一件事。
    /// </para>
    /// <para>
    /// 走逻辑树而不是视觉树：这条提示平时是收起来的，而收起来的控件不参与排布，
    /// 视觉树里也就没有它的子节点——按视觉树找会一个都找不到。
    /// </para>
    /// </remarks>
    private static void Click(LayoutFailureDialog dialog, string label) =>
        dialog.GetLogicalDescendants()
            .OfType<Button>()
            .Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal))
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// 一个可以随时拨到"算不出来"的引擎。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 拨开关而不是写一张按调用次数排的剧本：协调器每降一级就问引擎一次，
    /// 于是"一次重排"到底问几次取决于降级计划有几级。按次数写死的话，
    /// 计划一改，剧本就错位了，而错位的表现是"某条用例莫名其妙地不失败"。
    /// </para>
    /// <para>
    /// 拨开之后每一级都会失败，于是整次重排必然失败——这正是要造的场景。
    /// </para>
    /// </remarks>
    private sealed class SwitchableEngine : ILayoutEngine
    {
        private readonly ConstraintLayoutEngine _real = new();
        private int _calls;
        private volatile bool _failing;

        /// <summary>拨到真时每次调用都失败。</summary>
        public bool Failing
        {
            get => _failing;
            set => _failing = value;
        }

        /// <summary>已经被问过几次。</summary>
        public int Calls => Volatile.Read(ref _calls);

        public EngineLayoutResult Layout(LayoutRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return _failing
                ? throw new InvalidOperationException("这一份输入算不出来")
                : _real.Layout(request, cancellationToken);
        }
    }

    #endregion
}
