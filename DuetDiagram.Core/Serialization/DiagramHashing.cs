using System.Security.Cryptography;
using System.Text;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// 结构哈希与视觉哈希。
/// </summary>
/// <remarks>
/// <para>
/// 两个哈希解决的是同一个问题的两半："这次变更之后要做什么"。
/// 结构哈希只覆盖连接关系与父子归属，它变了就必须重新求解布局，因为坐标可能全部失效；
/// 视觉哈希覆盖标签、形状、样式，它变了只需要重绘，现有坐标仍然可用。
/// 把两者分开的直接收益是改个颜色不会引发一次全图重排。
/// </para>
/// <para>
/// 两个哈希都刻意**不覆盖**版本号和哈希自身。覆盖版本号会让哈希随着无关的版本推进而变化；
/// 覆盖自身则是循环定义。
/// </para>
/// <para>
/// 计算方式是按标识排序后拼成规范化文本再做摘要。排序是关键：
/// 集合的插入顺序可能因为撤销重做而变化，不排序就会算出不同的哈希，
/// 而对端会因此误判"结构变了"并白白重取一次全量。
/// </para>
/// <para>
/// 目前每次都是全量计算。这是因为增量维护（把每个子树做成哈希树、只重算受影响的分支）
/// 需要额外的索引结构，在实测证明全量算不过来之前不值得引入那部分复杂度。
/// </para>
/// </remarks>
public static class DiagramHashing
{
    /// <summary>
    /// 结构哈希：只回答"连接的形状变了吗"。
    /// 节点部分取标识与父级，边部分取标识、两端以及端口。
    /// 标签、形状、颜色都不参与——它们变了不影响布局。
    /// </summary>
    public static string ComputeStructuralHash(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        foreach (var node in document.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            builder.Append("n|").Append(node.Id).Append('|').Append(node.Parent ?? string.Empty).Append('\n');
        }

        foreach (var edge in document.Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            builder.Append("e|")
                .Append(edge.Id).Append('|')
                .Append(edge.From).Append('|')
                .Append(edge.To).Append('|')
                .Append(edge.FromPort ?? string.Empty).Append('|')
                .Append(edge.ToPort ?? string.Empty).Append('\n');
        }

        return Sha256Hex(builder.ToString());
    }

    /// <summary>
    /// 视觉哈希：回答"画出来会不一样吗"。
    /// 在主方向、节点外观、边外观上做全覆盖，因此结构哈希的每一次变化也必然让视觉哈希变化，
    /// 反过来则不成立——只改颜色时结构哈希不变而视觉哈希变。
    /// </summary>
    public static string ComputeVisualHash(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        // 主方向会整体改变布局结果，属于视觉层面最先要覆盖的输入。
        builder.Append("k|").Append(document.Kind).Append('\n');
        builder.Append("d|").Append(document.Direction).Append('\n');

        foreach (var node in document.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            builder.Append("n|")
                .Append(node.Id).Append('|')
                .Append(node.Label).Append('|')
                .Append(node.Shape).Append('|')
                .Append(node.Layer ?? string.Empty).Append('|')
                .Append(node.StyleToken ?? string.Empty).Append('|')
                .Append(node.Desc ?? string.Empty).Append('\n');
        }

        foreach (var edge in document.Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            builder.Append("e|")
                .Append(edge.Id).Append('|')
                .Append(edge.From).Append('|')
                .Append(edge.To).Append('|')
                .Append(edge.Label).Append('|')
                .Append(edge.Line).Append('|')
                .Append(edge.Arrow).Append('|')
                .Append(edge.StyleToken ?? string.Empty).Append('\n');
        }

        return Sha256Hex(builder.ToString());
    }

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
