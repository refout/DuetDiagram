using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using DuetDiagram.App;
using DuetDiagram.Core.Model;
using DuetDiagram.Render;
using FluentAssertions;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 两条端到端场景：大图导入后能立刻看见并点得中，以及把当前这张图导出成别人能打开的文件。
/// </summary>
/// <remarks>
/// <para>
/// 两条都走与 Phase 2 那批端到端同一口径——无头模式下起真的窗口、送真的输入、抓一帧，
/// 不另起一个界面，主题与绘图后端都是主程序那一套。
/// </para>
/// <para>
/// 界面上的导出对话框仍是占位，所以「导出」这一条走的是与界面同一份绘制列表那条管线：
/// 直接拿窗口画布上的 <see cref="DrawList"/> 交给 <see cref="SvgExporter"/>。
/// 那正是导出器真正的消费方，且它只消费绘制列表、不重新遍历文档——与画布是同一份几何。
/// </para>
/// </remarks>
public sealed class Phase4ScenarioTests
{
    #region 大型架构图：导入 500 节点，渲染 < 2s，可交互

    /// <summary>
    /// 导进来 500 个节点，整段导入加首帧渲染在两秒内，且导入的图点得中。
    /// </summary>
    /// <remarks>
    /// 时间量的是「用户能看见」的那一段：从导入命令落地（含布局）到第一帧画完。
    /// 两秒是这个场景的门槛，留了很大余量——卡在这里的表现是导入之后要等一会儿才出图。
    /// 「可交互」用一次真的点击验收：在节点中心按下再抬起，它应当被选中，
    /// 说明命中测试与画布都接上了，而不是画出来点不中。
    /// </remarks>
    [Fact]
    [Trait("Category", "Phase4Scenario")]
    public async Task Importing_a_500_node_diagram_renders_under_two_seconds_and_is_clickable()
    {
        await HeadlessFixture.Run(() =>
        {
            using var scratch = new Scratch();

            var path = scratch.Write("big.mmd", Big());

            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);
            var before = window.Session.Document.Nodes.Count;

            var stopwatch = Stopwatch.StartNew();
            window.Import(path);
            HeadlessFixture.Frame(window, canvas);
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(
                TimeSpan.FromSeconds(2),
                "五百节点渲染要 < 2s，量的就是导入落地到首帧画完这一段");

            window.Session.Document.Nodes.Count.Should().Be(before + 500, "五百个节点整段落进文档");

            // 可交互：点中其中一个节点应当选中它。
            var target = window.Session.Document.Nodes
                .First(node => node.Label == "节点1").Id;
            var at = HeadlessFixture.ToWindow(canvas, window, HeadlessFixture.CenterOf(canvas, target));

            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            HeadlessFixture.Frame(window, canvas);

            window.Session.SelectedIds.Should()
                .Contain(target, "导入的图可点击选中，命中与画布都接上了");

            window.Close();
        });
    }

    #endregion

    #region 协作导出：导出 SVG 给同事 draw.io 打开

    /// <summary>
    /// 把当前窗口的绘制列表导出成 SVG，文件能被别人的查看器打开，且内容不丢。
    /// </summary>
    /// <remarks>
    /// 三个判据：无损（<see cref="SvgExport.Dropped"/> 里每一处取舍都给出理由，且每类绘制指令都落成元素）、
    /// 可打开（合法的 SVG 根、带 viewBox，不同查看器尺寸一致）、文字可编辑
    /// （文字以 <c>text</c> 元素交付，对方能在 draw.io 里改，而不是转成路径）。
    /// </remarks>
    [Fact]
    [Trait("Category", "Phase4Scenario")]
    public async Task Exporting_svg_produces_an_openable_lossless_file()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            HeadlessFixture.Frame(window, canvas);

            var drawList = canvas.Model?.DrawList
                ?? throw new InvalidOperationException("画布还没有绘制列表");

            var export = SvgExporter.Export(drawList);

            using var scratch = new Scratch();
            var path = scratch.Write("diagram.svg", export.Svg);

            // 无损的第一层：导出器对每一处「没按矢量路径画」的取舍都必须给出理由，
            // 而不是静默跳过——静默跳过的话加一种绘制指令之后导出会少画一样，而没有任何报错。
            // 「文字」那一条是刻意的披露（文字交 <text> 而不是路径，换来可搜索可改），
            // 它不算丢数据，所以这里验的是「每一条都说得清为什么」，不是「清单为空」。
            export.Dropped.Should().OnlyContain(
                feature => !string.IsNullOrEmpty(feature.Reason),
                "导出器对每一处取舍都要给出理由，静默丢一类指令是不允许的");

            // 可打开：合法的 SVG，带 viewBox 让别人的查看器按正确尺寸打开。
            var document = XDocument.Load(path);
            var svg = document.Root
                ?? throw new InvalidOperationException("导出的不是合法的 XML");

            svg.Name.LocalName.Should().Be("svg");
            svg.Attribute("viewBox").Should().NotBeNull(
                "缺 viewBox 的话不同查看器里的尺寸不一致，对方会以为是导出错了");

            // 无损的第二层：绘制列表里的每条指令都至少落成一个 SVG 元素，一个都不能少。
            // 不按形状种类逐个核对（菱形与箭头都是 polygon，几何类型会重叠），而是验总数下界：
            // 少一条指令就少一个元素，于是 SVG 元素数会低于指令数——那正是「静默丢一类」的表现。
            var texts = drawList.Commands.OfType<DrawText>().Count();

            var svgElements = svg.Descendants().Count(element =>
                element.Name.LocalName is "rect" or "ellipse" or "circle"
                    or "polygon" or "polyline" or "path" or "line" or "text");
            var svgTexts = svg.Descendants().Count(element => element.Name.LocalName == "text");

            svgElements.Should().BeGreaterThanOrEqualTo(
                drawList.Commands.Count,
                "绘制列表里每条指令都至少落成一个 SVG 元素，少一条就是静默丢了一类");
            svgTexts.Should().Be(
                texts, "每行文字都落成一个 text 元素，对方才能在 draw.io 里改；转成路径就搜不到也改不了");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>读 svg 元素上的一个属性值。</summary>
    private static string? Attr(XElement element, string name) =>
        element.Attribute(name)?.Value;

    /// <summary>一条链式的内容：<c>n1 --&gt; n2 --&gt; …</c>，一共 500 个节点。</summary>
    private static string Big()
    {
        var text = new StringBuilder("flowchart TB\n");

        for (var i = 1; i <= 500; i++)
        {
            text.Append("  n").Append(i).Append("[节点").Append(i).Append(']').Append('\n');
        }

        for (var i = 1; i < 500; i++)
        {
            text.Append("  n").Append(i).Append(" --> n").Append(i + 1).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>一个用完即删的临时目录，里面写文件给导入入口读。</summary>
    /// <remarks>
    /// 用真实路径而不是内存里的字符串，是因为「读一份文件」这条路上会碰到编码、换行与路径，
    /// 而它们正是真实用户手上那份文件会有的样子。
    /// </remarks>
    private sealed class Scratch : IDisposable
    {
        public Scratch()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "duet-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, string content)
        {
            var full = System.IO.Path.Combine(Path, name);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响用例结论，留给系统临时目录自己收拾。
            }
        }
    }

    #endregion
}
