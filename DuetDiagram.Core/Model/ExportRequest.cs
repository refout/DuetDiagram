namespace DuetDiagram.Core.Model;

/// <summary>
/// 导出格式的记号，以及"哪个格式认哪个旋钮"这张表。
/// </summary>
/// <remarks>
/// <para>
/// **记号与适用性放在一起，是因为它们必须一致。** 界面与工具层都会拿这几个字符串做分支：
/// 各写一份的话，某一天两边对"svg 认不认范围"给出不同答案，而症状是同一份文档
/// 从界面导与从工具导出来不一样。表只有这一份，两处都读它。
/// </para>
/// <para>
/// **适用性不许默默忽略。** 给一个格式它办不到的旋钮时，正确的处置是拒绝并说清
/// 哪个格式办得到，而不是收下参数、按缺省值出图——后者会让调用方以为自己的选择生效了。
/// </para>
/// </remarks>
public static class ExportFormats
{
    /// <summary>语义文本。</summary>
    public const string Dsl = "dsl";

    /// <summary>矢量图。</summary>
    public const string Svg = "svg";

    /// <summary>位图。</summary>
    public const string Png = "png";

    /// <summary>可打印文档。</summary>
    public const string Pdf = "pdf";

    /// <summary>认得的全部格式，按这个次序摆出来。</summary>
    public static IReadOnlyList<string> All { get; } = [Dsl, Svg, Png, Pdf];

    /// <summary>写进错误与说明里的那一串。</summary>
    public static string Listed { get; } = string.Join('、', All);

    /// <summary>
    /// 这个格式认不认"按内容还是按页面"。
    /// </summary>
    /// <remarks>
    /// 文本与矢量图都不认：文本没有纸张这个概念，而矢量图的画布就是内容的外接框，
    /// 两种范围对它是同一件事。
    /// </remarks>
    public static bool HonorsRange(string format) => format is Png or Pdf;

    /// <summary>
    /// 这个格式认不认缩放倍数。
    /// </summary>
    /// <remarks>
    /// 只有位图认。PDF 里的单位是物理长度（点），换算系数由单位的定义定死，
    /// 不是一个可以随便选的旋钮；放大它的办法是换纸张尺寸，不是乘一个倍数。
    /// </remarks>
    public static bool HonorsScale(string format) => format is Png;
}

/// <summary>
/// 导出范围的记号。
/// </summary>
/// <remarks>
/// 两种都要有：整份内容的外接框适合"这张图本身"，页面尺寸适合"插进一份按纸张排的文档"。
/// </remarks>
public static class ExportRanges
{
    /// <summary>整份内容的外接框。</summary>
    public const string Content = "content";

    /// <summary>页面尺寸。</summary>
    public const string Page = "page";

    /// <summary>认得的全部范围。</summary>
    public static IReadOnlyList<string> All { get; } = [Content, Page];

    /// <summary>写进错误与说明里的那一串。</summary>
    public static string Listed { get; } = string.Join('、', All);
}

/// <summary>
/// 一次导出要什么。
/// </summary>
/// <remarks>
/// <para>
/// **它是界面与工具层共用的那一份请求。** 界面把用户选的几样装进来，
/// 工具层把模型给的参数装进来，两条路交出去的是同一种东西——
/// 于是"界面能设而工具不能设"这种偏差在结构上就不可能发生。
/// </para>
/// <para>
/// **它不装产物，只装选择。** 产物（文本、字节、丢失清单）各有各的类型，
/// 而它们与"要什么"的生命周期不同：请求在导出之前就存在，产物在之后才有。
/// </para>
/// </remarks>
/// <param name="Format">导出格式，取 <see cref="ExportFormats"/> 里的记号。</param>
/// <param name="PageId">要导出的页面。空表示当前页或全部页，由导出那一条路自己定。</param>
/// <param name="Range">裁剪范围，取 <see cref="ExportRanges"/> 里的记号。</param>
/// <param name="Scale">缩放倍数，大于零。只有认它的格式会用到。</param>
public sealed record ExportRequest(
    string Format,
    string? PageId = null,
    string Range = ExportRanges.Content,
    double Scale = 1)
{
    /// <summary>按缺省的一组选择导某个格式。</summary>
    public static ExportRequest Of(string format, string? pageId = null) => new(format, pageId);
}
