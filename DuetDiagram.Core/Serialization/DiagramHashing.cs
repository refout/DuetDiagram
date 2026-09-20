using System.Security.Cryptography;
using System.Text;
using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Serialization;

/// <summary>
/// 结构哈希与视觉哈希。
/// </summary>
/// <remarks>
/// <para>
/// AGENTS.md 约定 6：Phase 1 一律全量计算；只有 Phase 5 实测不达标时才改为增量（Merkle 树）。
/// 全量 1000 节点目标 ≤ 5ms（方案 §10）。
/// </para>
/// <para>
/// 结构哈希只覆盖「谁和谁相连」——它决定是否需要重布局。
/// 视觉哈希覆盖标签、形状、样式令牌——它决定是否需要重绘。
/// 两者都不覆盖 Version 与自身，避免自引用。
/// </para>
/// </remarks>
public static class DiagramHashing
{
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

    public static string ComputeVisualHash(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

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
