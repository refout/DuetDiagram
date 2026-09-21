namespace DuetDiagram.Mermaid.Lexing;

/// <summary>记号种类。</summary>
public enum MermaidTokenKind
{
    /// <summary>输入结束。</summary>
    End,

    /// <summary>
    /// 换行。
    /// </summary>
    /// <remarks>
    /// Mermaid 的语句以行为单位，因此换行是有意义的记号而不是空白。
    /// 把它当空白吞掉的话，语句边界就只能靠猜，而 `a --> b c --> d` 这类
    /// 合法的紧凑写法与错误写法将无法区分。
    /// </remarks>
    NewLine,

    /// <summary>标识、关键字或裸文本。</summary>
    Word,

    /// <summary>
    /// 定界符之内的标签文本。
    /// </summary>
    /// <remarks>
    /// 标签里可以出现几乎任何字符：<c>&lt;br/&gt;</c>、问号、逗号、十六进制颜色里的井号。
    /// 把它按标识去切分的话，那些字符全会变成"不认识的东西"，
    /// 而真实输出里七成以上的文件都含这类字符。
    /// </remarks>
    Text,

    /// <summary>引号包裹的文本。可能含空格与标点。</summary>
    QuotedText,

    /// <summary>连线。</summary>
    Arrow,

    /// <summary>形状的左半部分，例如 <c>[</c>、<c>([</c>、<c>[{</c>。</summary>
    ShapeOpen,

    /// <summary>形状的右半部分。</summary>
    ShapeClose,

    /// <summary>管道符。边标签的定界符。</summary>
    Pipe,

    /// <summary>冒号。类名简写与字段分隔用。</summary>
    Colon,

    /// <summary>与号。同一语句里并列多个节点用。</summary>
    Ampersand,

    /// <summary>分号。可以当语句分隔符用。</summary>
    Semicolon,

    /// <summary>注释，<c>%%</c> 到行尾。</summary>
    Comment,

    /// <summary>不认识的字符。</summary>
    /// <remarks>
    /// 产生记号而不是抛异常。导入时要能在有坏字符的输入上继续走完，
    /// 把错误就地抛出去会让它连"哪里坏了"都报不出来。
    /// </remarks>
    Unknown,
}

/// <summary>连线种类。</summary>
public enum MermaidArrowKind
{
    /// <summary>实线箭头 <c>--&gt;</c> 或 <c>-&gt;</c>。</summary>
    Arrow,

    /// <summary>实线无箭头 <c>---</c>。</summary>
    Open,

    /// <summary>虚线箭头 <c>-.-></c>。</summary>
    Dotted,

    /// <summary>虚线无箭头 <c>-.-</c>。</summary>
    DottedOpen,

    /// <summary>粗箭头 <c>==&gt;</c>。</summary>
    Thick,

    /// <summary>粗线无箭头 <c>===</c>。</summary>
    ThickOpen,
}

/// <summary>
/// 一个记号。
/// </summary>
/// <param name="Kind">种类。</param>
/// <param name="Text">原文。</param>
/// <param name="Line">行号，从 1 开始。</param>
/// <param name="Column">列号，从 1 开始。</param>
/// <param name="Arrow">连线的种类。仅连线记号有意义。</param>
/// <remarks>
/// 带行列信息是为了让错误能指到具体位置。只报"解析失败"的实现，
/// 在被解析对象是模型输出的时候几乎没法排查——那些文本动辄几十行。
/// </remarks>
public sealed record MermaidToken(
    MermaidTokenKind Kind,
    string Text,
    int Line,
    int Column,
    MermaidArrowKind? Arrow = null)
{
    /// <summary>是不是形状的开符号。</summary>
    public bool IsShapeOpen => Kind == MermaidTokenKind.ShapeOpen;

    /// <summary>是不是形状的闭符号。</summary>
    public bool IsShapeClose => Kind == MermaidTokenKind.ShapeClose;

    public override string ToString() => $"{Kind}('{Text}') @{Line}:{Column}";
}
