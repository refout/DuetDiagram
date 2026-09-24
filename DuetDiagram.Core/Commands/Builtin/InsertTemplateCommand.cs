using DuetDiagram.Core.Model;
using DuetDiagram.Core.Templates;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一份模板拼进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// **一条命令，不是一串命令。** 拼进去十个节点如果发十条命令，撤销要按十次，
/// 而用户在界面上做的是同一次"放了一个模板"。一条命令还让"失败整体回滚"变成免费的：
/// 校验不通过就一条元素都没落进去，不存在"落了半个模板"这种中间状态。
/// </para>
/// <para>
/// **装配计划算三遍，但读的是同一份实现。** 校验、算逆变更、真正落地各调一次
/// <see cref="TemplateInstantiator.TryPlan"/>——三次都在文档被改动之前，
/// 输入相同，所以结果相同。与新增节点那条命令把索引计算收在一处是同一个理由：
/// 三处各写一段的话，改了一处忘了另一处，撤销就会把元素还到错的地方。
/// </para>
/// <para>
/// **不查目标文档里已有的内容。** 目标文档自己有没有悬空引用是整体校验器的事，
/// 这条命令只保证"拼进去的这部分自洽"。拼完之后的文档仍然应当整体校验一遍，
/// 见 <c>DiagramValidator</c>。
/// </para>
/// </remarks>
public sealed class InsertTemplateCommand : DiagramCommandBase
{
    public const string Id = "insert-template";

    private readonly TemplateDocument _template;

    public InsertTemplateCommand(TemplateDocument template)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(template);
        _template = template;
    }

    /// <summary>这条命令要拼的那份模板。</summary>
    public TemplateDocument Template => _template;

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return TemplateInstantiator.TryPlan(document, _template, out _, out var errors)
            ? ValidationResult.Valid
            : ValidationResult.Invalid([.. errors]);
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!TemplateInstantiator.TryPlan(document, _template, out var plan, out var errors))
        {
            return CommandResult.Fail([.. errors]);
        }

        // 顺序与计划里的一致：节点、边、组合。三处都追加到末尾，
        // 于是撤销时按标识删掉就够了，不必记索引——这三批元素全都是这次新加的，
        // 不存在"它们中间还夹着别的元素"这种情况。
        document.MutableNodes.AddRange(plan!.Nodes);
        document.MutableEdges.AddRange(plan.Edges);
        document.MutableComposites.AddRange(plan.Composites);

        return CommandResult.Ok(
            affected: plan.Ids,
            changes: [.. Added(plan)],
            structural: true,
            visual: true,
            message: Describe(plan));
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new InsertTemplateCommand(_template);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        // 校验已经放行过一次，这里不会再失败；真失败了也不该抛——
        // 命令层的失败一律走返回值。给一份空快照，撤销时什么也不做。
        if (!TemplateInstantiator.TryPlan(document, _template, out var plan, out _))
        {
            return new InsertTemplateMemento();
        }

        return new InsertTemplateMemento
        {
            Nodes = [.. plan!.Nodes],
            Edges = [.. plan.Edges],
            Composites = [.. plan.Composites],
            AffectedIds = plan.Ids,
            InverseChanges = [.. Removed(plan)],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        var typed = (InsertTemplateMemento)memento;

        // 按标识找、找到才删：重做会复用这条路径，而重做前那些元素可能已经不在了。
        foreach (var node in typed.Nodes)
        {
            var index = document.MutableNodes.FindIndex(item => string.Equals(item.Id, node.Id, StringComparison.Ordinal));

            if (index >= 0)
            {
                document.MutableNodes.RemoveAt(index);
            }
        }

        foreach (var edge in typed.Edges)
        {
            var index = document.MutableEdges.FindIndex(item => string.Equals(item.Id, edge.Id, StringComparison.Ordinal));

            if (index >= 0)
            {
                document.MutableEdges.RemoveAt(index);
            }
        }

        foreach (var composite in typed.Composites)
        {
            var index = document.MutableComposites.FindIndex(
                item => string.Equals(item.Id, composite.Id, StringComparison.Ordinal));

            if (index >= 0)
            {
                document.MutableComposites.RemoveAt(index);
            }
        }
    }

    /// <summary>每条元素一条"新增"明细。一次插入要在变更明细里逐条看得见。</summary>
    private static IEnumerable<FieldChange> Added(TemplatePlan plan)
    {
        foreach (var node in plan.Nodes)
        {
            yield return Change(node.Id, FieldNames.NodeElement, node.Label, null, ChangeKind.Added);
        }

        foreach (var edge in plan.Edges)
        {
            yield return Change(edge.Id, FieldNames.EdgeElement, edge.Label, null, ChangeKind.Added);
        }

        foreach (var composite in plan.Composites)
        {
            yield return Change(composite.Id, FieldNames.CompositeElement, composite.Label, null, ChangeKind.Added);
        }
    }

    /// <summary>逆变更与命令本身方向相反：插入的逆操作是移除。</summary>
    private static IEnumerable<FieldChange> Removed(TemplatePlan plan)
    {
        foreach (var node in plan.Nodes)
        {
            yield return Change(node.Id, FieldNames.NodeElement, null, node.Label, ChangeKind.Removed);
        }

        foreach (var edge in plan.Edges)
        {
            yield return Change(edge.Id, FieldNames.EdgeElement, null, edge.Label, ChangeKind.Removed);
        }

        foreach (var composite in plan.Composites)
        {
            yield return Change(composite.Id, FieldNames.CompositeElement, null, composite.Label, ChangeKind.Removed);
        }
    }

    private static FieldChange Change(string id, string field, string? oldValue, string? newValue, ChangeKind kind) =>
        new()
        {
            ElementId = id,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            Kind = kind,
        };

    /// <summary>一句话说明这次放了什么。改过标识时要提一句——那是用户没做过的改动。</summary>
    private static string Describe(TemplatePlan plan)
    {
        var renamed = plan.Renames.Count(pair => !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal));

        return renamed == 0
            ? $"放入模板 {plan.ElementCount} 条元素"
            : $"放入模板 {plan.ElementCount} 条元素，其中 {renamed} 个标识与文档里已有的重名，已自动改名";
    }
}
