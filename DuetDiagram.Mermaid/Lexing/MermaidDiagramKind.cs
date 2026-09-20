namespace DuetDiagram.Mermaid.Lexing;

/// <summary>
/// Mermaid 的图类型。
/// </summary>
/// <remarks>
/// Mermaid 是一个家族而不是一种语言：流程图、状态图、时序图、类图各有一套完全不同的语法。
/// 把它们放在一个枚举里，是为了让"这份输入是什么"这个问题在解析之前就有答案。
/// </remarks>
public enum MermaidDiagramKind
{
    /// <summary>流程图。<c>flowchart</c> 或 <c>graph</c>。</summary>
    Flowchart,

    /// <summary>状态图。<c>stateDiagram</c> 或 <c>stateDiagram-v2</c>。</summary>
    State,

    /// <summary>时序图。<c>sequenceDiagram</c>。</summary>
    Sequence,

    /// <summary>类图。<c>classDiagram</c>。</summary>
    Class,

    /// <summary>实体关系图。<c>erDiagram</c>。</summary>
    EntityRelationship,

    /// <summary>其它已知但不受支持的图类型（甘特图、饼图、思维导图等）。</summary>
    Other,

    /// <summary>认不出来。</summary>
    Unknown,
}

/// <summary>
/// 识别图类型。
/// </summary>
/// <remarks>
/// <para>
/// **识别的用处是能明确拒绝。** 冻结语料里一百份响应有九十七份是流程图，
/// 两份是状态图，一份是时序图——最后那份是模型面对"下单全链路"选了时序图，
/// 那是合理的选择，只是我们的 IR 里没有对应的类型。
/// </para>
/// <para>
/// 没有这一步的话，时序图会被当成流程图去解析，然后在某个位置失败并报一个
/// 与真正原因毫无关系的错误——而真正的原因是"这份东西我们根本不支持"。
/// 报错要报在能让人立刻明白的地方。
/// </para>
/// </remarks>
public static class MermaidDiagramKindDetector
{
    /// <summary>按首行判断图类型。</summary>
    public static MermaidDiagramKind Detect(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = MermaidLexer.Tokenize(source);

        foreach (var token in tokens)
        {
            if (token.Kind is MermaidTokenKind.Comment or MermaidTokenKind.NewLine)
            {
                continue;
            }

            if (token.Kind != MermaidTokenKind.Word)
            {
                return MermaidDiagramKind.Unknown;
            }

            return Classify(token.Text);
        }

        return MermaidDiagramKind.Unknown;
    }

    private static MermaidDiagramKind Classify(string keyword) => keyword switch
    {
        "flowchart" or "graph" => MermaidDiagramKind.Flowchart,
        "stateDiagram" or "stateDiagram-v2" => MermaidDiagramKind.State,
        "sequenceDiagram" => MermaidDiagramKind.Sequence,
        "classDiagram" or "classDiagram-v2" => MermaidDiagramKind.Class,
        "erDiagram" => MermaidDiagramKind.EntityRelationship,
        "gantt" or "pie" or "mindmap" or "timeline" or "journey" or "gitGraph" or "quadrantChart"
            or "xychart-beta" or "block-beta" or "architecture-beta" => MermaidDiagramKind.Other,
        _ => MermaidDiagramKind.Unknown,
    };

    /// <summary>
    /// 这个图类型能不能被当前的 IR 表达。
    /// </summary>
    /// <remarks>
    /// 状态图在 IR 里有对应类型（<c>DiagramKind.State</c>），只是语法解析还没做；
    /// 时序图则连类型都没有——那属于"表达不了"而不是"还没做"，
    /// 两者对调用方的含义不同：前者等一等就好，后者得换个工具。
    /// </remarks>
    public static bool IsRepresentable(MermaidDiagramKind kind) =>
        kind is MermaidDiagramKind.Flowchart or MermaidDiagramKind.State;
}
