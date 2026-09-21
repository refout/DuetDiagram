namespace DuetDiagram.Dsl.Lexing;

/// <summary>
/// 从一段文本里取出 DSL 源码。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>MermaidSource.Extract</c> 是同一步骤、同一套算法：词法分析处理"一段 DSL 源码"，
/// 而真实来源往往还包着一层——模型回答里的代码围栏、文件里的注释头。
/// </para>
/// <para>
/// **剥围栏是必需的，不是可选的宽容。** 冻结语料的 c-dsl 组五十份回答里有十五份带围栏
/// （30%），另外三十五份没带——同一批提示词，同一批要求。不剥的话，
/// 那十五份会在第一个记号处就解析失败，而失败原因与被测的东西毫无关系。
/// 围栏标签还各不相同（<c>dsl</c>、<c>flow</c>、<c>text</c>、空），所以只能整行跳过，
/// 不能按标签匹配。
/// </para>
/// <para>
/// 两份实现是刻意重复的，不是漏了抽象：Mermaid 与 DSL 互不依赖，而 Core 的职责是
/// IR 与命令总线，把 Markdown 围栏的知识塞进去不属于它的章程。
/// 这段逻辑只有二十行且已经稳定，重复的代价小于新引入一条工程间依赖。
/// </para>
/// </remarks>
public static class DslSource
{
    private const string Fence = "```";

    /// <summary>
    /// 从可能带包装的文本里取出 DSL 源码。
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

        // 围栏行后面可以跟语言标记（dsl、flow 等），跳过整行。
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
