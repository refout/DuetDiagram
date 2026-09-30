using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using DuetDiagram.App;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Serialization;
using DuetDiagram.Core.Workspace;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace DuetDiagram.E2E.Tests;

/// <summary>
/// 导出入口：选格式、选范围、选倍数，把画布上这一份写成文件。
/// </summary>
/// <remarks>
/// <para>
/// 选文件那一步在无头模式下打不开，所以用例走的是主窗口那个公开的导出入口
/// （<c>MainWindow.Export</c>），它与选完文件之后那条路是同一段代码——只有"文件写到哪儿"不同。
/// </para>
/// <para>
/// **范围与倍数是真的量出来的，不是"请求传到了"就算数。** 位图那两条按像素尺寸断言，
/// 因为尺寸是这两件事唯一说得清的后果；只断言"参数到了宿主"的话，
/// 宿主把参数丢掉不用的实现照样能过。
/// </para>
/// <para>
/// 每份产物都写在用完就删的目录里。用真实路径而不是内存里的字节，是因为
/// "写一份文件"这条路上会碰到目录、编码与原子替换这些东西，而它们正是真实导出会遇到的。
/// </para>
/// </remarks>
public sealed class ExportTests
{
    #region 对话框（Category=Export）

    [Fact]
    [Trait("Category", "Export")]
    public async Task The_export_dialog_offers_every_format()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var dialog = HeadlessFixture.ExportDialog(window);

            dialog.Formats.Should().BeEquivalentTo(ExportFormats.All, "四档格式都要能选");
            dialog.Ranges.Should().BeEquivalentTo(ExportRanges.All);

            window.Close();
        });
    }

    /// <summary>
    /// 一个格式办不到的旋钮在对话框里是灰的。
    /// </summary>
    /// <remarks>
    /// 灰掉而不是收下参数再按缺省值出图：后者会让人以为自己的选择生效了。
    /// 这与工具那条路拒绝并说清"哪个格式办得到"是同一件事的两种说法。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task A_format_that_cannot_honor_a_knob_has_it_disabled_in_the_dialog()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var dialog = HeadlessFixture.ExportDialog(window);

            foreach (var format in ExportFormats.All)
            {
                dialog.SelectFormat(format);

                dialog.RangeEnabled.Should().Be(
                    ExportFormats.HonorsRange(format),
                    $"{format} 那一档的范围可不可用要跟着格式走");

                dialog.ScaleEnabled.Should().Be(
                    ExportFormats.HonorsScale(format),
                    $"{format} 那一档的倍数可不可用要跟着格式走");
            }

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Export")]
    public async Task The_scale_box_refuses_a_number_that_is_not_positive()
    {
        await HeadlessFixture.Run(() =>
        {
            var window = HeadlessFixture.Open();
            var dialog = HeadlessFixture.ExportDialog(window);

            dialog.SelectFormat(ExportFormats.Png);
            dialog.SetScale("2");

            dialog.CanConfirm.Should().BeTrue();
            dialog.Hint.Should().BeNull();

            foreach (var bad in new[] { "0", "-1", "两倍", "" })
            {
                dialog.SetScale(bad);

                dialog.CanConfirm.Should().BeFalse($"{bad} 不是一个大于零的数，导出那一个按钮该是灰的");
                dialog.Hint.Should().NotBeNullOrWhiteSpace("要写一句为什么点不动");
            }

            window.Close();
        });
    }

    #endregion

    #region 真的写出文件（Category=Export）

    [Fact]
    [Trait("Category", "Export")]
    public async Task Exporting_svg_writes_an_openable_file()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var window = HeadlessFixture.Open();
            var path = temp.File("out.svg");

            var outcome = window.Export(path, new ExportRequest(ExportFormats.Svg));

            outcome.Should().NotBeNull();
            File.Exists(path).Should().BeTrue();

            var text = File.ReadAllText(path);

            text.Should().Contain("<svg", "写出来的要是一份矢量图，不是一段文本");
            text.Should().Contain("</svg>");

            window.Close();
        });
    }

    /// <summary>
    /// 倍数那一栏真的落到了像素尺寸上。
    /// </summary>
    /// <remarks>
    /// 按像素量而不是按"请求传到了宿主"：后者的话，宿主把倍数丢掉不用的实现照样能过，
    /// 而用户拿到的是一张没放大的图。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task Exporting_png_honors_the_scale()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var window = HeadlessFixture.Open();
            var list = window.Session.Scene.DrawList;

            window.Export(temp.File("one.png"), new ExportRequest(ExportFormats.Png));
            window.Export(temp.File("two.png"), new ExportRequest(ExportFormats.Png, Scale: 2));

            using var one = SKBitmap.Decode(temp.File("one.png"));
            using var two = SKBitmap.Decode(temp.File("two.png"));

            one.Should().NotBeNull();
            two.Should().NotBeNull();

            two.Width.Should().Be((int)Math.Ceiling(list.Width * 2));
            two.Height.Should().Be((int)Math.Ceiling(list.Height * 2));
            two.Width.Should().BeGreaterThan(one.Width, "二倍图要比一倍图宽");

            window.Close();
        });
    }

    /// <summary>
    /// 范围那一栏真的落到了画布尺寸上。
    /// </summary>
    /// <remarks>
    /// 按纸张裁时画布就是文档自己声明的那张纸，与内容多大无关——
    /// 所以这一条同时验了两件事：范围生效了，而且纸张尺寸是从文档读来的而不是写死的。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task Exporting_png_honors_the_page_range()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var window = HeadlessFixture.Open();
            var paper = window.Session.Document.Canvas.PageSize;

            window.Export(
                temp.File("sheet.png"),
                new ExportRequest(ExportFormats.Png, Range: ExportRanges.Page));

            using var sheet = SKBitmap.Decode(temp.File("sheet.png"));

            sheet.Should().NotBeNull();
            sheet.Width.Should().Be((int)Math.Ceiling(paper.Width));
            sheet.Height.Should().Be((int)Math.Ceiling(paper.Height));

            window.Close();
        });
    }

    /// <summary>
    /// PDF 与 PNG 都认范围，而 PDF 不认倍数。
    /// </summary>
    /// <remarks>
    /// 两种范围各导一份，比的是**产物不同**而不是某一处内部状态：
    /// 这一条要挡住的是"范围收下了却没用"。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task Exporting_pdf_writes_a_pdf_and_the_range_changes_it()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var window = HeadlessFixture.Open();

            window.Export(temp.File("content.pdf"), new ExportRequest(ExportFormats.Pdf));
            window.Export(
                temp.File("sheet.pdf"),
                new ExportRequest(ExportFormats.Pdf, Range: ExportRanges.Page));

            foreach (var name in new[] { "content.pdf", "sheet.pdf" })
            {
                var bytes = File.ReadAllBytes(temp.File(name));

                Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF", $"{name} 要真的是一份 PDF");
            }

            File.ReadAllBytes(temp.File("content.pdf")).Should().NotEqual(
                File.ReadAllBytes(temp.File("sheet.pdf")),
                "两种范围该导出不一样的东西");

            window.Close();
        });
    }

    /// <summary>
    /// 导出的 DSL 带着固定位置。
    /// </summary>
    /// <remarks>
    /// 界面这一层拿得到固定位置，工具那一层拿不到——后者会把"固定位置写不出来"
    /// 记进丢失清单。这里能写出来，所以导出的文本里应当有 <c>pin</c>。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task Exporting_dsl_carries_the_pinned_positions()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var window = HeadlessFixture.Open();
            var canvas = HeadlessFixture.Canvas(window);

            var from = HeadlessFixture.ToWindow(canvas, window, HeadlessFixture.CenterOf(canvas, "check"));
            Drag(window, from, new Point(from.X + 60, from.Y + 40));

            window.Session.PinnedNodes.Should().ContainKey("check", "这一拖要真的把它钉住，否则下面那条断言验不到东西");

            var path = temp.File("out.dsl");

            window.Export(path, new ExportRequest(ExportFormats.Dsl));

            var text = File.ReadAllText(path);

            text.Should().Contain("check");
            text.Should().Contain("pin", "固定位置写不出来时工具那条路要报丢失，而界面这条路写得出来");

            window.Close();
        });
    }

    /// <summary>
    /// 只读的窗口照样导得出去。
    /// </summary>
    /// <remarks>
    /// 导出是**读**这份文档、写**另一个**文件，与"能不能改这份文档"是两件事。
    /// 另存为才是后者，这一条不是。
    /// </remarks>
    [Fact]
    [Trait("Category", "Export")]
    public async Task The_export_entry_is_enabled_in_a_read_only_window()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();
            var path = temp.File("doc.json");

            File.WriteAllText(path, DiagramSerializer.SerializeFull(SampleDiagram.Document()));

            // 冒充另一个进程：先拿住这份文档的独占所有权。
            using var other = DocumentLock.Acquire(path);

            var window = new MainWindow(DocumentLaunch.File(path));

            window.Show();
            window.CaptureRenderedFrame();

            window.Session.IsReadOnly.Should().BeTrue("另一个进程正在编辑这份文档");

            var entry = HeadlessFixture.MenuBar(window).Find("export.dialog");

            entry.Should().NotBeNull();
            entry!.IsEnabled.Should().BeTrue("导出写的是另一个文件，只读不该挡住它");

            window.Invoke("export.dialog");

            HeadlessFixture.ExportDialog(window).IsVisible.Should().BeTrue("点一下要把对话框摆出来");

            window.Close();
        });
    }

    [Fact]
    [Trait("Category", "Export")]
    public async Task A_failed_export_says_so_on_the_status_bar()
    {
        await HeadlessFixture.Run(() =>
        {
            using var temp = new TempDirectory();

            // 拿一个普通文件当目录用：建目录那一步会失败，而失败要摆到状态栏上。
            File.WriteAllText(temp.File("blocker"), "not a directory");

            var window = HeadlessFixture.Open();
            var outcome = window.Export(
                Path.Combine(temp.File("blocker"), "out.svg"),
                new ExportRequest(ExportFormats.Svg));

            outcome.Should().BeNull();
            window.Status.HasMessage.Should().BeTrue("失败要摆在状态栏上，而不是点了没反应");
            window.Status.Message.Should().Contain("导不出去");

            window.Close();
        });
    }

    #endregion

    #region 辅助

    /// <summary>在画布上拖一次。</summary>
    private static void Drag(MainWindow window, Point from, Point to, int steps = 3)
    {
        window.MouseDown(from, MouseButton.Left);

        for (var step = 1; step <= steps; step++)
        {
            var t = (double)step / steps;

            window.MouseMove(new Point(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t)));
        }

        window.MouseUp(to, MouseButton.Left);
    }

    #endregion
}
