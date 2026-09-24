using DuetDiagram.Core.Model;

namespace DuetDiagram.Render;

/// <summary>
/// 位图导出按哪一档定范围。
/// </summary>
/// <remarks>
/// 两种都要有：整份内容的外接框适合"这张图本身"，页面尺寸适合"插进一份按纸张排的文档"。
/// 只给一种的话，另一种场景里用户得自己拿别的工具去裁。
/// </remarks>
public enum BitmapCrop
{
    /// <summary>整份内容的外接框，也就是绘制列表自己的宽高。</summary>
    Content,

    /// <summary>页面尺寸。由调用方从文档的画布设置里读出来传进来。</summary>
    Page,
}

/// <summary>
/// 位图导出的选择。
/// </summary>
/// <remarks>
/// <para>
/// **缩放按倍数给，不给目标像素宽度。** 给宽度的话，同一份文档导两次（一次 1000 像素、
/// 一次 2000 像素）各自算出一个缩放系数，字体的渲染结果会有一点差别，
/// 而用户以为是自己哪一步写错了。给倍数时"我要两倍图"是一个确定的值，
/// 两次导出之间的关系也一眼看得出。
/// </para>
/// <para>
/// **背景与透明度必须显式给。** 缺省透明的话，导出的 PNG 贴进白底文档里会变成黑底——
/// 这是"导出看起来坏了"里最常见的一种。所以缺省是**不透明**，底色取绘制列表自己的背景色；
/// 真要透明时才显式打开。
/// </para>
/// <para>
/// **页面尺寸由调用方传，不由导出器去读文档。** 导出器只认绘制列表，重新遍历文档的话，
/// 导出与画布就成了两条绘制路径，而两条迟早对不上。页面尺寸不是绘制列表的一部分
/// （它不影响任何一条指令），所以它只能从外面进来。
/// </para>
/// </remarks>
public sealed record BitmapOptions
{
    /// <summary>缩放倍数。一倍表示一个文档单位对应一个像素。</summary>
    public double Scale { get; init; } = 1;

    /// <summary>留白，单位是文档单位。只在按内容外接框裁时算数。</summary>
    public double Padding { get; init; }

    /// <summary>底是不是透明的。缺省不透明，底色取绘制列表的背景色。</summary>
    public bool Transparent { get; init; }

    /// <summary>按哪一档定范围。</summary>
    public BitmapCrop Crop { get; init; } = BitmapCrop.Content;

    /// <summary>页面尺寸。<see cref="Crop"/> 为 <see cref="BitmapCrop.Page"/> 时用它。</summary>
    public Size PageSize { get; init; } = new(0, 0);
}
