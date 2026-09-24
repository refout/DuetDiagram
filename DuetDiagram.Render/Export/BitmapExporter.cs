using DuetDiagram.Core.Model;
using SkiaSharp;

namespace DuetDiagram.Render;

/// <summary>
/// 把一份绘制列表光栅化成位图。
/// </summary>
/// <remarks>
/// <para>
/// **它只消费绘制列表，不重新遍历文档**，与 SVG 导出同一条口径。重新遍历的话，
/// 导出与画布成了两条绘制路径，而两条迟早会对不上——对不上的表现是"导出的图与屏幕上不一样"，
/// 而两边各自都自洽。
/// </para>
/// <para>
/// **它只负责开画布、定范围、编成 PNG。** 画什么由 <see cref="CanvasPainter"/> 定，
/// 而那一份与 PDF 导出共用——两种格式的差别只在最后一步，不在画法上。
/// </para>
/// <para>
/// **它是无头的。** 光栅化直接开一块离屏画布，不经过任何窗口平台。命令行、服务端与
/// 模型那条通路都没有窗口平台，依赖窗口的话，界面上一切正常而命令行导出必失败。
/// </para>
/// <para>
/// **确定性。** 同一份绘制列表与同一组选项导出两次逐像素相同：不带时间戳、
/// 不带随机标识、不依赖集合迭代顺序，抗锯齿也显式打开而不是听凭默认值。
/// 唯一与机器有关的是字体——字形由本机装的字体画出来，换一台机器字体不同则像素不同，
/// 这一条与"节点尺寸由字体量出来"是同一件事，不是导出引入的。
/// </para>
/// </remarks>
public static class BitmapExporter
{
    /// <summary>
    /// 导出。
    /// </summary>
    /// <remarks>
    /// 产物用的是 Core 那份记录，不是这里另立一个：工具层要把它原样交给调用方，
    /// 而工具层看不见渲染层。两边各一份的话，"丢失清单"就有了两种形状。
    /// </remarks>
    /// <param name="list">绘制列表。</param>
    /// <param name="options">选择。为空时用默认。</param>
    /// <exception cref="ArgumentException">按页面裁却没有给页面尺寸。</exception>
    public static BitmapExport Export(DrawList list, BitmapOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(list);

        var settings = options ?? new BitmapOptions();
        var (frameWidth, frameHeight) = Frame(list, settings);
        var pad = settings.Crop == BitmapCrop.Content ? Math.Max(0, settings.Padding) : 0;
        var width = frameWidth + (pad * 2);
        var height = frameHeight + (pad * 2);
        var scale = settings.Scale > 0 ? settings.Scale : throw new ArgumentException("缩放倍数要大于零。", nameof(options));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight))
            ?? throw new InvalidOperationException("开不出一块离屏画布。");

        var canvas = surface.Canvas;

        CanvasPainter.Background(canvas, list.Background, settings.Transparent);

        // 先缩放再平移：一个点先加留白、再整体乘倍数。反过来排的话留白也会被缩放，
        // 于是"留白十像素"在二倍图上变成二十像素。
        canvas.Scale((float)scale);
        canvas.Translate((float)pad, (float)pad);

        CanvasPainter.Commands(canvas, list.Commands);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG 编码没给出结果。");

        return new BitmapExport(data.ToArray(), pixelWidth, pixelHeight, Losses(list, settings, width, height));
    }

    #region 范围

    /// <summary>这一档导出要画多大，单位是文档单位。</summary>
    private static (double Width, double Height) Frame(DrawList list, BitmapOptions settings) =>
        settings.Crop switch
        {
            BitmapCrop.Page => Page(settings),
            _ => (list.Width, list.Height),
        };

    private static (double Width, double Height) Page(BitmapOptions settings) =>
        settings.PageSize.Width > 0 && settings.PageSize.Height > 0
            ? (settings.PageSize.Width, settings.PageSize.Height)
            : throw new ArgumentException("按页面裁要给出页面尺寸。", nameof(settings));

    /// <summary>
    /// 这份导出丢了什么。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 第一条是位图这种格式本身的代价：所有东西都变成像素，文字搜不到、图不能再编辑。
    /// 要可编辑的矢量就用 SVG 那一档。
    /// </para>
    /// <para>
    /// 第二条只在按页面裁、而且内容比页面大时出现。**这一条必须如实报出来**：
    /// 静默裁掉一块的话，用户拿到一张缺了角而"看起来正常"的图，
    /// 而那种缺失在缩略图上根本看不出来。
    /// </para>
    /// </remarks>
    private static DroppedFeature[] Losses(DrawList list, BitmapOptions settings, double width, double height)
    {
        var losses = new List<DroppedFeature>
        {
            new(
                "可编辑性",
                [],
                "位图里所有东西都是像素：文字搜不到、形状不能再改。要能接着编辑就用 SVG 那一档。"),
        };

        if (settings.Crop == BitmapCrop.Page && (list.Width > width || list.Height > height))
        {
            losses.Add(new(
                "超出页面的内容",
                [],
                $"按页面尺寸裁掉了超出纸张的部分：内容实际 {Numbers.Format(list.Width)}×{Numbers.Format(list.Height)}，"
                + $"页面 {Numbers.Format(width)}×{Numbers.Format(height)}。要完整内容就按内容外接框那一档导。"));
        }

        return [.. losses];
    }

    #endregion
}
