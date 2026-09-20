namespace DuetDiagram.Mermaid.Lexing;

/// <summary>
/// 从一段文本里取出 Mermaid 源码。
/// </summary>
/// <remarks>
/// <para>
/// 这一步与词法分析分开：词法分析处理"一段 Mermaid 源码"，
/// 而真实来源往往还包着一层——模型回答里的代码围栏、文件里的注释头、
/// 或者从聊天记录里贴出来的一整段。
/// </para>
/// <para>
/// **剥围栏是必需的，不是可选的宽容。** 冻结语料里一百份 Mermaid 回答有八十五份带围栏，
/// 而剩下十五份没带——同一批提示词，同一批要求。不剥的话，
/// 那八十五份会在第一个记号处就解析失败，而失败原因与被测的东西毫无关系。
/// </para>
/// </remarks>
public static class MermaidSource
{
    private const string Fence = "```";

    /// <summary>
    /// 从可能带包装的文本里取出 Mermaid 源码。
    /// </summary>
    /// <remarks>
    /// 有围栏时取第一个围栏块的内容；没有时原样返回并去掉首尾空白。
    /// 只取第一个块：多块的情况属于调用方没想清楚要解析哪一块，
    /// 与其猜，不如取第一个并让调用方自己决定要不要拆。
    /// </remarks>
    public static string Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var start = text.IndexOf(Fence, StringComparison.Ordinal);

        if (start < 0)
        {
            return text.Trim();
        }

        // 围栏行后面可以跟语言标记（mermaid、mmd 等），跳过整行。
        var contentStart = text.IndexOf('\n', start);

        if (contentStart < 0)
        {
            return string.Empty;
        }

        var end = text.IndexOf(Fence, contentStart, StringComparison.Ordinal);

        return end < 0
            ? text[(contentStart + 1)..].Trim()
            : text[(contentStart + 1)..end].Trim();
    }
}
