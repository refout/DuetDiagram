using DuetDiagram.Core.Model;
using DuetDiagram.Core.Templates;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 把一份从别的格式导入出来的片段拼进当前文档。
/// </summary>
/// <remarks>
/// <para>
/// **一条命令，不是一串命令。** 导入一份五百个节点的图如果按元素发命令，
/// 撤销要按五百次，而用户在界面上做的是同一次「导入了这个文件」。
/// 一条命令还让「失败整体回滚」变成免费的：解析出来的一半内容不会留在文档里，
/// 不存在「落了半个」这种中间状态。
/// </para>
/// <para>
/// **标识冲突消解与放入模板读的是同一份实现。** 两边各写一套改名规则的话，
/// 同一份内容从模板那条路进来与从导入那条路进来会得到两套名字，
/// 而其中一套迟早会漏掉某个引用字段——漏掉的表现是一份指向空处的文档，
/// 校验器报出来的位置离肇事的那一步很远。
/// </para>
/// <para>
/// **它不碰文档级设置。** 片段里的方向、图类型、画布设置都没有落点：
/// 导进来的是「片段」，不是整份文档。元素一律落在缺省页上，位置交给布局——
/// 自己算位置的话，导入进来的东西会与布局结果打架。
/// </para>
/// <para>
/// **它不认识 Mermaid。** 收的是一份已经映射好的片段，
/// 解析与映射留在格式那一层：那两件事每种格式各有一套，
/// 而"拼进文档"这件事只有一套。
/// </para>
/// </remarks>
public sealed class ImportFragmentCommand : DiagramCommandBase
{
    public const string Id = "import-fragment";

    private readonly TemplateDocument _fragment;

    /// <param name="fragment">导进来的片段。名字取来源文件名，用于说明这次导的是什么。</param>
    public ImportFragmentCommand(TemplateDocument fragment)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        _fragment = fragment;
    }

    /// <summary>这条命令要拼的那份片段。</summary>
    public TemplateDocument Fragment => _fragment;

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 空片段单独报一个码：放进模板那条路的码说的是"这份模板里没有可放的东西"，
        // 而用户手上是一份文件、从没碰过模板，那句话读起来对不上他刚做的事。
        if (_fragment.ElementCount == 0)
        {
            return ValidationResult.Invalid(CommandError.Of(ErrorCodes.ImportEmpty, _fragment.Name));
        }

        return TemplateInstantiator.TryPlan(document, _fragment, out _, out var errors)
            ? ValidationResult.Valid
            : ValidationResult.Invalid([.. errors]);
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_fragment.ElementCount == 0)
        {
            return CommandResult.Fail([CommandError.Of(ErrorCodes.ImportEmpty, _fragment.Name)]);
        }

        if (!TemplateInstantiator.TryPlan(document, _fragment, out var plan, out var errors))
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

    protected override DiagramCommandBase CloneWith(ChangeContext context) => new ImportFragmentCommand(_fragment);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        // 校验已经放行过一次，这里不会再失败；真失败了也不该抛——
        // 命令层的失败一律走返回值。给一份空快照，撤销时什么也不做。
        if (!TemplateInstantiator.TryPlan(document, _fragment, out var plan, out _))
        {
            return new ImportFragmentMemento();
        }

        return new ImportFragmentMemento
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
        var typed = (ImportFragmentMemento)memento;

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

    /// <summary>每条元素一条"新增"明细。一次导入要在变更明细里逐条看得见。</summary>
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

    /// <summary>逆变更与命令本身方向相反：导进来的逆操作是移除。</summary>
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

    /// <summary>
    /// 一句话说明这次导进了什么。
    /// </summary>
    /// <remarks>
    /// 改过标识时要提一句——那是用户没做过的改动，而它决定了图上那些元素叫什么。
    /// 来源文件名也带上：模板目录里能查到是哪一份模板，导入的那份文件在界面上关掉提示之后就查不到了。
    /// </remarks>
    private string Describe(TemplatePlan plan)
    {
        var renamed = plan.Renames.Count(pair => !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal));
        var source = string.IsNullOrWhiteSpace(_fragment.Name) ? "外部内容" : _fragment.Name;

        return renamed == 0
            ? $"导入 {source}：{plan.ElementCount} 条元素"
            : $"导入 {source}：{plan.ElementCount} 条元素，其中 {renamed} 个标识与文档里已有的重名，已自动改名";
    }
}
