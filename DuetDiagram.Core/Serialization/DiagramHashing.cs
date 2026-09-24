using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// 结构哈希与视觉哈希。
/// </summary>
/// <remarks>
/// <para>
/// **两个哈希回答的是两个问题，而不是"结构变了吗"和"外观变了吗"。**
/// </para>
/// <list type="bullet">
/// <item>结构哈希：<b>现有坐标还有效吗？</b>它变了就必须重新求解布局。</item>
/// <item>视觉哈希：<b>画出来会不一样吗？</b>它变了只需要重绘。</item>
/// </list>
/// <para>
/// 这个措辞不是文字游戏。按"结构变了吗"来理解会把三类东西漏在外面：
/// 布局提示改了间距、字体换了宽度、端口挪了位置——它们都不改变图形拓扑，
/// 却都会让已算出的坐标失效。按"要不要重排"来理解，这三类自然就归位了。
/// </para>
/// <para>
/// **包含关系是构造出来的，不是碰巧成立的。** 视觉哈希先算结构部分的内容再追加外观部分，
/// 因此结构哈希的每一次变化必然让视觉哈希变化。反过来不成立——只改颜色时结构不变而视觉变。
/// 这条性质必须靠实现来保证：早先的实现里视觉哈希漏掉了节点的父级，
/// 于是"结构变了视觉却没变"，而那种不一致没有任何地方会报错。
/// </para>
/// <para>
/// 两个哈希都刻意**不覆盖**版本号、时间戳与哈希自身。
/// 覆盖版本号会让哈希随无关的版本推进而变化；覆盖自身是循环定义；
/// 覆盖约束的创建时间则会让"重复添加同一条约束"被判成内容变化。
/// </para>
/// <para>
/// 计算方式是按标识排序后拼成规范化文本再做摘要。排序是关键：
/// 集合的插入顺序可能因为撤销重做而变化，不排序就会算出不同的哈希，
/// 而对端会因此误判"结构变了"并白白重取一次全量。
/// </para>
/// <para>
/// 目前每次都是全量计算。增量维护（把每个子树做成哈希树、只重算受影响的分支）
/// 需要额外的索引结构，在实测证明全量算不过来之前不值得引入那部分复杂度。
/// </para>
/// </remarks>
public static class DiagramHashing
{
    /// <summary>
    /// 结构哈希：回答"要不要重新求解布局"。
    /// </summary>
    public static string ComputeStructuralHash(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Sha256Hex(StructuralContent(document));
    }

    /// <summary>
    /// 视觉哈希：回答"画出来会不一样吗"。
    /// </summary>
    /// <remarks>
    /// 结构部分的内容原样并入，因此结构哈希的每一次变化都必然带来视觉哈希的变化。
    /// </remarks>
    public static string ComputeVisualHash(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        builder.Append(StructuralContent(document));
        AppendVisualContent(document, builder);

        return Sha256Hex(builder.ToString());
    }

    /// <summary>
    /// 决定布局的那部分内容。
    /// </summary>
    /// <remarks>
    /// 判据只有一个：这个字段变了，已经算出来的坐标还准不准。
    /// 端口、布局提示、字体都在这里——它们不改变图形形状，但都会让坐标失效。
    /// </remarks>
    private static string StructuralContent(DiagramDocument document)
    {
        var builder = new StringBuilder();

        // 图类型与主方向都改变分层的语义与推进方向，是最先要让坐标失效的输入。
        builder.Append("k|").Append(document.Kind).Append('\n');
        builder.Append("d|").Append(document.Direction).Append('\n');

        foreach (var node in document.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            builder.Append("n|")
                .Append(node.Id).Append('|')
                .Append(node.Parent ?? string.Empty).Append('|')
                .Append(node.Page ?? string.Empty).Append('|')
                .Append(Ports(node)).Append('\n');
        }

        foreach (var edge in document.Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            builder.Append("e|")
                .Append(edge.Id).Append('|')
                .Append(edge.From).Append('|')
                .Append(edge.To).Append('|')
                .Append(edge.FromPort ?? string.Empty).Append('|')
                .Append(edge.ToPort ?? string.Empty).Append('|')
                .Append(edge.Page ?? string.Empty).Append('\n');
        }

        foreach (var composite in document.Composites.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            builder.Append("c|")
                .Append(composite.Id).Append('|')
                .Append(composite.Parent ?? string.Empty).Append('|')
                .Append(composite.Direction?.ToString() ?? string.Empty).Append('|')
                .Append(composite.Collapsed).Append('|')
                .Append(string.Join(',', composite.Members)).Append('\n');
        }

        // 字体改变标签的实际宽度，进而改变节点尺寸与布局结果。
        foreach (var font in document.Fonts.OrderBy(f => f.Id, StringComparer.Ordinal))
        {
            builder.Append("f|")
                .Append(font.Id).Append('|')
                .Append(font.Name).Append('|')
                .Append(font.Path ?? string.Empty).Append('|')
                .Append(font.IsMono).Append('\n');
        }

        builder.Append("ls|")
            .Append(document.Layout.NodeSpacing).Append('|')
            .Append(document.Layout.LayerSpacing).Append('\n');

        AppendConstraints(document.Layout, builder);

        return builder.ToString();
    }

    /// <summary>
    /// 追加纯外观内容。
    /// </summary>
    /// <remarks>
    /// 这里只放"改了它像素会变、但坐标不用重算"的东西。
    /// 判断一个字段该不该出现在这里，看它是否已经出现在结构部分——
    /// 重复出现不会错，但漏掉会让视觉哈希失去它该有的灵敏度。
    /// <para>
    /// **自定义形状的路径在这里，不在结构部分。** 换轮廓不改节点坐标：
    /// 节点的尺寸由标签量出来，端口位置由所在边与偏移算出来，两者都不看形状。
    /// 所以换形状只需要重绘——把它算进结构哈希会让每次换形状都白白重排一次。
    /// </para>
    /// </remarks>
    private static void AppendVisualContent(DiagramDocument document, StringBuilder builder)
    {
        foreach (var node in document.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            builder.Append("nl|")
                .Append(node.Id).Append('|')
                .Append(node.Label).Append('|')
                .Append(node.Shape).Append('|')
                .Append(node.ShapePath ?? string.Empty).Append('|')
                .Append(node.Layer ?? string.Empty).Append('|')
                .Append(node.StyleToken ?? string.Empty).Append('|')
                .Append(node.Desc ?? string.Empty).Append('|')
                .Append(node.RichText).Append('|')
                .Append(node.MathMode).Append('|')
                .Append(Canonical(node.Style, DiagramJsonContext.Default.NodeStyle)).Append('|')
                .Append(Canonical(node.Text, DiagramJsonContext.Default.TextStyle)).Append('\n');
        }

        foreach (var edge in document.Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            builder.Append("el|")
                .Append(edge.Id).Append('|')
                .Append(edge.Label).Append('|')
                .Append(Canonical(edge.Style, DiagramJsonContext.Default.EdgeStyle)).Append('\n');
        }

        foreach (var composite in document.Composites.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            builder.Append("cl|")
                .Append(composite.Id).Append('|')
                .Append(composite.Label).Append('|')
                .Append(Canonical(composite.Style, DiagramJsonContext.Default.NodeStyle)).Append('|')
                .Append(Canonical(composite.LocalLayout, DiagramJsonContext.Default.LayoutHints)).Append('\n');
        }

        foreach (var tag in document.Tags.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            builder.Append("t|")
                .Append(tag.Id).Append('|')
                .Append(tag.Label).Append('|')
                .Append(tag.Color ?? string.Empty).Append('|')
                .Append(string.Join(',', tag.Members)).Append('\n');
        }

        foreach (var preset in document.TextPresets.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            builder.Append("tp|")
                .Append(preset.Id).Append('|')
                .Append(preset.Name).Append('|')
                .Append(Canonical(preset.Style, DiagramJsonContext.Default.TextStyle)).Append('\n');
        }

        foreach (var layer in document.Layers.OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            builder.Append("ly|")
                .Append(layer.Id).Append('|')
                .Append(layer.Name).Append('|')
                .Append(layer.Visible).Append('|')
                .Append(layer.Locked).Append('|')
                .Append(layer.Order).Append('\n');
        }

        foreach (var page in document.Pages.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            builder.Append("pg|")
                .Append(page.Id).Append('|')
                .Append(page.Name).Append('|')
                .Append(page.Order).Append('\n');
        }

        foreach (var entry in document.Palette.Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            builder.Append("pe|")
                .Append(entry.Key).Append('|')
                .Append(Canonical(entry.Value, DiagramJsonContext.Default.PaletteEntry)).Append('\n');
        }

        var canvas = document.Canvas;

        builder.Append("cv|")
            .Append(canvas.Grid).Append('|')
            .Append(canvas.GridSize).Append('|')
            .Append(canvas.PageSize.Width).Append('x').Append(canvas.PageSize.Height).Append('|')
            .Append(canvas.Orientation).Append('|')
            .Append(canvas.Background ?? string.Empty).Append('|')
            .Append(canvas.Infinite).Append('\n');
    }

    /// <summary>
    /// 端口串。空集合与"只有一个默认端口"必须算出不同的结果，
    /// 因此这里显式写出数量，不能依赖空串与单元素串的差别。
    /// </summary>
    private static string Ports(NodeDef node) =>
        node.Ports.Count == 0
            ? "-"
            : string.Join(',', node.Ports.Select(p => $"{p.Name}@{p.Side}:{p.Offset}:{p.IsCustom}"));

    /// <summary>
    /// 追加四类约束。
    /// </summary>
    /// <remarks>
    /// <see cref="Constraint{T}.CreatedAt"/> **不参与**：两条内容相同的约束，
    /// 创建时间不同不该让对方认为"布局输入变了"。把它算进去，
    /// 重复添加一条一模一样的约束就会触发一次全图重排。
    /// </remarks>
    private static void AppendConstraints(LayoutHints layout, StringBuilder builder)
    {
        foreach (var c in layout.SameRank)
        {
            builder.Append("sr|").Append(c.Owner).Append('|').Append(string.Join(',', c.Value.Nodes)).Append('\n');
        }

        foreach (var c in layout.Order)
        {
            builder.Append("or|").Append(c.Owner).Append('|').Append(c.Value.NodeId).Append('|')
                .Append(string.Join(',', c.Value.Order)).Append('\n');
        }

        foreach (var c in layout.Align)
        {
            builder.Append("al|").Append(c.Owner).Append('|').Append(string.Join(',', c.Value.Nodes)).Append('\n');
        }

        foreach (var c in layout.Place)
        {
            builder.Append("pl|").Append(c.Owner).Append('|').Append(c.Value.NodeId).Append('|')
                .Append(c.Value.RelativeTo).Append('|').Append(c.Value.Relation).Append('\n');
        }
    }

    /// <summary>
    /// 把记录写成规范化文本。
    /// </summary>
    /// <remarks>
    /// 走序列化而不是手写字段拼接，是为了让**新增字段自动进入哈希**。
    /// 手写拼接的隐患是：给样式记录加一个字段，却忘了同步改哈希函数，
    /// 于是那个字段怎么改都不会被判定为"画出来不一样"，界面不重绘而用户看到旧画面。
    /// 这类问题的表现是"改了不生效"，排查起来非常费劲。
    /// </remarks>
    /// <param name="value">要规范化的记录。</param>
    /// <param name="typeInfo">
    /// 源生成的类型信息。必须显式传入而不是靠泛型推断——
    /// 泛型重载在裁剪与原生编译下会被判定为需要运行时生成代码，直接报错。
    /// </param>
    private static string Canonical<T>(T? value, JsonTypeInfo<T> typeInfo) where T : class =>
        value is null ? "-" : JsonSerializer.Serialize(value, typeInfo).Replace('\n', ' ');

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
