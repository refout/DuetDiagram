using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// PDF 导出按哪一档定范围。
/// </summary>
/// <remarks>
/// 两种都要有：整份内容的外接框适合"这张图本身"，页面尺寸适合"插进一份按纸张排的文档"。
/// 与位图那一档同一组取值，理由也同一份。
/// </remarks>
public enum PdfCrop
{
    /// <summary>整份内容的外接框。多页时取各页里最大的那个，好让每页一样大。</summary>
    Content,

    /// <summary>页面尺寸。由调用方从文档的画布设置里读出来传进来。</summary>
    Page,
}

/// <summary>
/// PDF 导出的选择。
/// </summary>
/// <remarks>
/// <para>
/// **没有缩放倍数这一项，因为 PDF 里的单位是物理长度。** 点就是点，一寸是七十二点；
/// 而文档单位是"96 像素每英寸的那一像素"，两者的换算系数由单位的定义定死，
/// 不是一个可以随便选的旋钮。缺省页面尺寸 1123×794 折过去正好是 A4 横放，
/// 这不是巧合——它本来就是按 A4 定的。
/// </para>
/// <para>
/// **页面尺寸由调用方传，不由导出器去读文档。** 导出器只认绘制列表，重新遍历文档的话，
/// 导出与画布就成了两条绘制路径，而两条迟早对不上。页面尺寸不是绘制列表的一部分
/// （它不影响任何一条指令），所以它只能从外面进来。
/// </para>
/// </remarks>
public sealed record PdfOptions
{
    /// <summary>按哪一档定范围。</summary>
    public PdfCrop Crop { get; init; } = PdfCrop.Content;

    /// <summary>页面尺寸，单位是文档单位。<see cref="Crop"/> 为 <see cref="PdfCrop.Page"/> 时用它。</summary>
    public Size PageSize { get; init; } = new(0, 0);
}
