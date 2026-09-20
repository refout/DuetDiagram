namespace DuetDiagram.Core.Model;

/// <summary>一条校验问题。</summary>
public sealed record ValidationIssue(string Code, string Message, string? RelatedId = null);

/// <summary>
/// 文档一致性校验。
/// </summary>
/// <remarks>
/// <para>
/// 命令层在写入前已经把大部分非法状态挡住了。这里做的是另一件事：
/// **整体校验**，用于加载外部文件、接收远端同步结果、以及测试断言。
/// 这些入口不经过命令层，只靠命令层的防线是不够的。
/// </para>
/// <para>
/// 校验只报告不修改。发现问题时由调用方决定是拒绝加载、丢弃问题部分，还是照常打开并提示——
/// 这三种处置在不同场景下都合理，校验器不该替调用方做这个决定。
/// </para>
/// </remarks>
public static class DiagramValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var issues = new List<ValidationIssue>();

        CheckIdUniqueness(document, issues);
        CheckEdgeEndpoints(document, issues);
        CheckCompositeMembership(document, issues);
        CheckReferences(document, issues);

        return issues;
    }

    /// <summary>九个集合共用一个命名空间，标识重复会让引用产生歧义。</summary>
    private static void CheckIdUniqueness(DiagramDocument document, List<ValidationIssue> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        void Check(string kind, IEnumerable<IDefinition> definitions)
        {
            foreach (var definition in definitions)
            {
                if (seen.TryGetValue(definition.Id, out var previous))
                {
                    issues.Add(new ValidationIssue(
                        "ID_DUPLICATE",
                        $"标识 {definition.Id} 同时出现在 {previous} 与 {kind} 中。九个集合共用一个命名空间。",
                        definition.Id));
                }
                else
                {
                    seen[definition.Id] = kind;
                }
            }
        }

        Check("页面", document.Pages);
        Check("图层", document.Layers);
        Check("节点", document.Nodes);
        Check("边", document.Edges);
        Check("组合", document.Composites);
        Check("标签", document.Tags);
        Check("动作", document.Actions);
        Check("字体", document.Fonts);
        Check("文本预设", document.TextPresets);
    }

    private static void CheckEdgeEndpoints(DiagramDocument document, List<ValidationIssue> issues)
    {
        foreach (var edge in document.Edges)
        {
            if (document.FindNode(edge.From) is null)
            {
                issues.Add(new ValidationIssue(
                    "EDGE_FROM_MISSING",
                    $"边 {edge.Id} 的起点 {edge.From} 不存在。",
                    edge.Id));
            }

            if (document.FindNode(edge.To) is null)
            {
                issues.Add(new ValidationIssue(
                    "EDGE_TO_MISSING",
                    $"边 {edge.Id} 的终点 {edge.To} 不存在。",
                    edge.Id));
            }

            CheckPort(document, issues, edge.Id, edge.From, edge.FromPort, "起点");
            CheckPort(document, issues, edge.Id, edge.To, edge.ToPort, "终点");
        }
    }

    private static void CheckPort(
        DiagramDocument document,
        List<ValidationIssue> issues,
        string edgeId,
        string nodeId,
        string? portName,
        string end)
    {
        if (portName is null)
        {
            return;
        }

        var node = document.FindNode(nodeId);

        // 节点本身不存在的情况上面已经报过，这里不重复报，避免一处错因引出两条问题。
        if (node is null)
        {
            return;
        }

        if (node.FindPort(portName) is null)
        {
            issues.Add(new ValidationIssue(
                "EDGE_PORT_MISSING",
                $"边 {edgeId} 的{end}指定了端口 {portName}，但节点 {nodeId} 上没有这个端口。",
                edgeId));
        }
    }

    /// <summary>
    /// 组合成员与节点父级必须一致。
    /// </summary>
    /// <remarks>
    /// 这两处表达的是同一件事。方案对两者都有要求，但两份数据天然可能不一致，
    /// 约定以组合的成员列表为准、节点的父级是冗余索引。不一致时必须报出来——
    /// 放任不管的话，布局按其中一处算、渲染按另一处画，症状会表现为"节点画在了错误的框里"。
    /// </remarks>
    private static void CheckCompositeMembership(DiagramDocument document, List<ValidationIssue> issues)
    {
        var composites = document.Composites.ToDictionary(c => c.Id, StringComparer.Ordinal);

        foreach (var composite in document.Composites)
        {
            if (composite.Parent is not null && !composites.ContainsKey(composite.Parent))
            {
                issues.Add(new ValidationIssue(
                    "COMPOSITE_PARENT_MISSING",
                    $"组合 {composite.Id} 的外层 {composite.Parent} 不存在。",
                    composite.Id));
            }

            foreach (var memberId in composite.Members)
            {
                var node = document.FindNode(memberId);

                if (node is not null)
                {
                    if (!string.Equals(node.Parent, composite.Id, StringComparison.Ordinal))
                    {
                        issues.Add(new ValidationIssue(
                            "MEMBERSHIP_MISMATCH",
                            $"节点 {memberId} 是组合 {composite.Id} 的成员，但它的父级写的是 {node.Parent ?? "空"}。"
                            + "约定以成员列表为准。",
                            memberId));
                    }

                    continue;
                }

                if (composites.TryGetValue(memberId, out var nested))
                {
                    if (!string.Equals(nested.Parent, composite.Id, StringComparison.Ordinal))
                    {
                        issues.Add(new ValidationIssue(
                            "MEMBERSHIP_MISMATCH",
                            $"组合 {memberId} 是组合 {composite.Id} 的成员，但它的外层写的是 {nested.Parent ?? "空"}。",
                            memberId));
                    }

                    continue;
                }

                issues.Add(new ValidationIssue(
                    "MEMBER_MISSING",
                    $"组合 {composite.Id} 的成员 {memberId} 既不是节点也不是组合。",
                    composite.Id));
            }
        }

        // 反向检查：节点的父级指向了组合，但它不在那个组合的成员列表里。
        foreach (var node in document.Nodes)
        {
            if (node.Parent is null)
            {
                continue;
            }

            if (!composites.TryGetValue(node.Parent, out var parent))
            {
                issues.Add(new ValidationIssue(
                    "NODE_PARENT_MISSING",
                    $"节点 {node.Id} 的父级 {node.Parent} 不是已定义的组合。",
                    node.Id));

                continue;
            }

            if (!parent.Members.Contains(node.Id, StringComparer.Ordinal))
            {
                issues.Add(new ValidationIssue(
                    "MEMBERSHIP_MISMATCH",
                    $"节点 {node.Id} 的父级是组合 {node.Parent}，但该组合的成员列表里没有它。",
                    node.Id));
            }
        }
    }

    /// <summary>标签成员与动作目标必须指向存在的定义。</summary>
    private static void CheckReferences(DiagramDocument document, List<ValidationIssue> issues)
    {
        foreach (var tag in document.Tags)
        {
            foreach (var member in tag.Members.Where(m => !document.IsIdTaken(m, except: null)))
            {
                issues.Add(new ValidationIssue(
                    "TAG_MEMBER_MISSING",
                    $"标签 {tag.Id} 的成员 {member} 不存在。",
                    tag.Id));
            }
        }

        foreach (var action in document.Actions)
        {
            if (action.Target is not null && !document.IsIdTaken(action.Target, except: null))
            {
                issues.Add(new ValidationIssue(
                    "ACTION_TARGET_MISSING",
                    $"动作 {action.Id} 的目标 {action.Target} 不存在。",
                    action.Id));
            }
        }
    }
}
