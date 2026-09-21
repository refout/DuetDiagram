using DuetDiagram.Core.Commands;

namespace DuetDiagram.Core.Model;

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
/// <para>
/// **每条问题都必须给出修复建议。** 只说哪里错、不说怎么改，等于把问题原样丢回给用户。
/// 这一点对模型同样重要：一个能听懂"该动哪里"的模型可以自己修好，
/// 而只收到"标识重复"的模型只能猜。
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
        CheckCompositeCycles(document, issues);
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
                    issues.Add(new ValidationIssue
                    {
                        Code = ErrorCodes.DuplicateId,
                        Message = $"标识 {definition.Id} 同时出现在{previous}与{kind}中。九个集合共用一个命名空间。",
                        RelatedId = definition.Id,
                        Suggestion = "给其中一个改名，或删除不再需要的那个定义。",
                    });
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

    /// <summary>
    /// 边的两端必须指向存在的定义。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **端点可以是节点，也可以是组合。** 分层架构图里 <c>ODS --&gt; DWD</c> 拿分组当端点，
    /// 说的是"这一层流向那一层"——这是外部格式里很常见、也很自然的写法。
    /// 解析顺序是先节点后组合，与 <see cref="CheckCompositeMembership"/> 同一口径。
    /// </para>
    /// <para>
    /// 提示语里把两种可能都写出来：只说"创建节点"的话，用户面对一个本来就想连到分组上的边
    /// 会以为是标识写错了，而实际上他要的可能是先建那个分组。
    /// </para>
    /// </remarks>
    private static void CheckEdgeEndpoints(DiagramDocument document, List<ValidationIssue> issues)
    {
        foreach (var edge in document.Edges)
        {
            if (!document.HasEndpoint(edge.From))
            {
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.EdgeSourceMissing,
                    Message = $"边 {edge.Id} 的起点 {edge.From} 不存在。",
                    RelatedId = edge.Id,
                    Suggestion = $"创建节点或组合 {edge.From}，或把边 {edge.Id} 的起点改成已有的节点或组合。",
                });
            }

            if (!document.HasEndpoint(edge.To))
            {
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.EdgeTargetMissing,
                    Message = $"边 {edge.Id} 的终点 {edge.To} 不存在。",
                    RelatedId = edge.Id,
                    Suggestion = $"创建节点或组合 {edge.To}，或把边 {edge.Id} 的终点改成已有的节点或组合。",
                });
            }

            CheckPort(document, issues, edge.Id, edge.From, edge.FromPort, "起点");
            CheckPort(document, issues, edge.Id, edge.To, edge.ToPort, "终点");
        }
    }

    /// <summary>
    /// 端点上指定的端口必须存在。
    /// </summary>
    /// <remarks>
    /// **端口只属于节点。** 组合没有端口，所以端点落在组合上时指定端口是写错了，
    /// 而不是"端口名对不上"——这两种说法给用户的下一步动作完全不同：
    /// 前者要把端口去掉或把端点改成节点，后者要去补端口。
    /// </remarks>
    private static void CheckPort(
        DiagramDocument document,
        List<ValidationIssue> issues,
        string edgeId,
        string endpointId,
        string? portName,
        string end)
    {
        if (portName is null)
        {
            return;
        }

        // 端点本身不存在的情况上面已经报过，这里不重复报，避免一处错因引出两条问题。
        if (!document.HasEndpoint(endpointId))
        {
            return;
        }

        var node = document.FindNode(endpointId);

        if (node is null)
        {
            issues.Add(new ValidationIssue
            {
                Code = ErrorCodes.EdgePortOnComposite,
                Message = $"边 {edgeId} 的{end}指向组合 {endpointId}，却指定了端口 {portName}。组合没有端口。",
                RelatedId = edgeId,
                Suggestion = $"去掉边 {edgeId} 的{end}端口，或把{end}改成节点 {endpointId} 之外的某个节点。",
            });

            return;
        }

        if (node.FindPort(portName) is null)
        {
            issues.Add(new ValidationIssue
            {
                Code = ErrorCodes.EdgePortMissing,
                Message = $"边 {edgeId} 的{end}指定了端口 {portName}，但节点 {endpointId} 上没有这个端口。",
                RelatedId = edgeId,
                Suggestion = $"在节点 {endpointId} 上补上端口 {portName}，或把边改为不指定端口由引擎自动选边。",
            });
        }
    }

    /// <summary>
    /// 组合成员与节点父级必须一致。
    /// </summary>
    /// <remarks>
    /// 这两处表达的是同一件事。两者都要满足，但两份数据天然可能不一致，
    /// 约定以组合的成员列表为准、成员的父级是冗余索引。不一致时必须报出来——
    /// 放任不管的话，布局按其中一处算、渲染按另一处画，症状会表现为"节点画在了错误的框里"。
    /// </remarks>
    private static void CheckCompositeMembership(DiagramDocument document, List<ValidationIssue> issues)
    {
        var composites = document.Composites.ToDictionary(c => c.Id, StringComparer.Ordinal);

        foreach (var composite in document.Composites)
        {
            if (composite.Parent is not null && !composites.ContainsKey(composite.Parent))
            {
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.ParentMissing,
                    Message = $"组合 {composite.Id} 的外层 {composite.Parent} 不存在。",
                    RelatedId = composite.Id,
                    Suggestion = $"创建组合 {composite.Parent}，或清空 {composite.Id} 的外层字段。",
                });
            }

            foreach (var memberId in composite.Members)
            {
                var node = document.FindNode(memberId);

                if (node is not null)
                {
                    if (!string.Equals(node.Parent, composite.Id, StringComparison.Ordinal))
                    {
                        issues.Add(Mismatch(
                            $"{memberId}（节点）",
                            composite.Id,
                            node.Parent));
                    }

                    continue;
                }

                if (composites.TryGetValue(memberId, out var nested))
                {
                    if (!string.Equals(nested.Parent, composite.Id, StringComparison.Ordinal))
                    {
                        issues.Add(Mismatch(
                            $"{memberId}（组合）",
                            composite.Id,
                            nested.Parent));
                    }

                    continue;
                }

                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.GroupMemberMissing,
                    Message = $"组合 {composite.Id} 的成员 {memberId} 既不是节点也不是组合。",
                    RelatedId = composite.Id,
                    Suggestion = $"创建 {memberId}，或把它从组合 {composite.Id} 的成员列表里移除。",
                });
            }
        }

        // 反向检查：成员的父级指向了组合，但它不在那个组合的成员列表里。
        foreach (var node in document.Nodes)
        {
            if (node.Parent is null)
            {
                continue;
            }

            if (!composites.TryGetValue(node.Parent, out var parent))
            {
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.ParentMissing,
                    Message = $"节点 {node.Id} 的父级 {node.Parent} 不是已定义的组合。",
                    RelatedId = node.Id,
                    Suggestion = $"创建组合 {node.Parent}，或清空节点 {node.Id} 的父级字段。",
                });

                continue;
            }

            if (!parent.Members.Contains(node.Id, StringComparer.Ordinal))
            {
                issues.Add(Mismatch($"{node.Id}（节点）", node.Parent, node.Parent));
            }
        }
    }

    private static ValidationIssue Mismatch(string member, string compositeId, string? declaredParent) => new()
    {
        Code = ErrorCodes.MembershipMismatch,

        // 两处不一致时把双方都写出来，看的人才知道该信哪个、该改哪个。
        Message = $"{member} 是组合 {compositeId} 的成员，但它记录的父级是 {declaredParent ?? "空"}。"
                  + "约定以组合的成员列表为准。",
        RelatedId = member,
        Suggestion = $"把父级字段改成 {compositeId}，或把该标识从组合 {compositeId} 的成员列表里移除。",
    };

    /// <summary>
    /// 组合的归属关系不能成环。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 成环会让"向上找容器"这类遍历永远走不到头。这类问题在写入时不容易发现——
    /// 单独看每一步都是合法的父子关系，只有连起来才成环。
    /// </para>
    /// <para>
    /// 环上的**每一个**组合都会报一条，而不是只报一条。界面按标识高亮时，
    /// 这样能把整个环都标出来；只报一个的话用户还得自己顺着找。
    /// </para>
    /// </remarks>
    private static void CheckCompositeCycles(DiagramDocument document, List<ValidationIssue> issues)
    {
        var composites = document.Composites.ToDictionary(c => c.Id, StringComparer.Ordinal);

        foreach (var start in document.Composites)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
            var current = start.Parent;

            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    issues.Add(new ValidationIssue
                    {
                        Code = ErrorCodes.GroupCycle,
                        Message = $"组合 {start.Id} 的归属关系成环，经由 {current} 回到了自己。",
                        RelatedId = start.Id,
                        Suggestion = $"断开 {start.Id} 与 {current} 之间的归属关系，或把其中一个改为顶层组合。",
                    });

                    break;
                }

                current = composites.TryGetValue(current, out var composite) ? composite.Parent : null;
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
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.TagMemberMissing,
                    Message = $"标签 {tag.Id} 的成员 {member} 不存在。",
                    RelatedId = tag.Id,
                    Suggestion = $"创建 {member}，或把它从标签 {tag.Id} 的成员列表里移除。",
                });
            }
        }

        foreach (var action in document.Actions)
        {
            if (action.Target is not null && !document.IsIdTaken(action.Target, except: null))
            {
                issues.Add(new ValidationIssue
                {
                    Code = ErrorCodes.ActionTargetMissing,
                    Message = $"动作 {action.Id} 的目标 {action.Target} 不存在。",
                    RelatedId = action.Id,
                    Suggestion = $"创建 {action.Target}，或清空动作 {action.Id} 的目标。",
                });
            }
        }
    }
}
