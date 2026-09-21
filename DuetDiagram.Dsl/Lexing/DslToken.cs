namespace DuetDiagram.Dsl.Lexing;

/// <summary>记号种类。</summary>
public enum DslTokenKind
{
    /// <summary>输入结束。</summary>
    End,

    /// <summary>
    /// 换行。
    /// </summary>
    /// <remarks>
    /// 与 Mermaid 侧同一条理由：DSL 是行式语法，每行一个声明。
    /// 把换行当空白吞掉的话，语句边界只能靠猜，而一行里塞两句与一句写错
    /// 这两种情况将无法区分。
    /// </remarks>
    NewLine,

    /// <summary>标识、关键字或数字。三者不做区分，由语法层解释。</summary>
    Word,

    /// <summary>
    /// 引号包裹的文本。显示文本与 desc 用。
    /// </summary>
    /// <remarks>
    /// 这是 DSL 与 Mermaid 最重要的差别之一：DSL 把标识与显示文本**显式分开**，
    /// 所以显示文本一律带引号，不必像 Mermaid 那样靠定界符猜哪里是文本。
    /// </remarks>
    QuotedText,

    /// <summary>连线。<c>-&gt;</c> 有箭头，<c>--</c> 无箭头。</summary>
    Arrow,

    /// <summary>冒号。边标识、order 的目标、端口分隔用。</summary>
    Colon,

    /// <summary>逗号。并列列表用。</summary>
    Comma,

    /// <summary>等号。属性赋值用。</summary>
    Equals,

    /// <summary>点号。端口引用 <c>节点.端口</c> 用。</summary>
    Dot,

    /// <summary>注释，<c>#</c> 到行尾。</summary>
    Comment,

    /// <summary>
    /// 不认识的字符。
    /// </summary>
    /// <remarks>
    /// 产生记号而不是抛异常，与 Mermaid 侧同一口径。两份解析器对坏输入的处理
    /// 必须一致，否则对比测试量到的是口径差异而不是格式差异。
    /// </remarks>
    Unknown,
}

/// <summary>
/// 一个记号。
/// </summary>
/// <param name="Kind">种类。</param>
/// <param name="Text">原文。引号文本是**已解转义**的内容，不含外层引号。</param>
/// <param name="Line">行号，从 1 开始。</param>
/// <param name="Column">列号，从 1 开始。</param>
/// <param name="HasArrow">连线是否带箭头。仅连线记号有意义。</param>
public sealed record DslToken(
    DslTokenKind Kind,
    string Text,
    int Line,
    int Column,
    bool HasArrow = false)
{
    /// <summary>是不是带箭头的连线。</summary>
    public bool IsArrow => Kind == DslTokenKind.Arrow && HasArrow;

    /// <summary>是不是不带箭头的连线。</summary>
    public bool IsOpenLine => Kind == DslTokenKind.Arrow && !HasArrow;

    public override string ToString() => $"{Kind}('{Text}') @{Line}:{Column}";
}
