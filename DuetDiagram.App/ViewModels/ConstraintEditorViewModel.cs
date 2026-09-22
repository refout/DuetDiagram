using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 属性面板里布局约束那一节的状态：当前有哪些约束、能加哪几种、能不能删。
/// </summary>
/// <remarks>
/// <para>
/// **界面加的是人工约束。** 自动约束由引擎推导，模型约束由工具写入，三者在降级时
/// 被丢弃的次序不同：解不出来时先丢模型提的，人工定的留到最后。界面上不区分这三者的话，
/// 用户会以为自己加的约束在降级时被保住了，而实际丢掉的可能是他加的那一条。
/// 所以每一行都要把归属写出来，而且只有人工的那些能删。
/// </para>
/// <para>
/// **能加什么由当前选中决定。** 同层与对齐要两个及以上节点，层内次序要一个主语
/// 加至少两个目标、且主语到每个目标都有一条边。凑不出来的那几种按钮就是灰的——
/// 让用户点下去再报错的话，他会以为是自己点错了。
/// </para>
/// <para>
/// **它不直接写文档。** 加与删都构造命令交给会话。面板自己写 IR 的话，
/// 撤销、版本日志、广播、审计这几条要各自再补一遍，而漏掉的那条不会有任何提示。
/// </para>
/// </remarks>
public sealed class ConstraintEditorViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;

    private IReadOnlyList<ConstraintRowViewModel> _rows = [];
    private string? _note;

    public ConstraintEditorViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前文档里的全部约束，人工的在最前面。</summary>
    public IReadOnlyList<ConstraintRowViewModel> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            Raise(nameof(Rows));
            Raise(nameof(HasRows));
        }
    }

    /// <summary>有没有约束。</summary>
    public bool HasRows => _rows.Count > 0;

    /// <summary>没有约束时那一句说明。</summary>
    /// <remarks>
    /// 只读时换一句。留着"选中两个节点可以加"的话，用户会照着去选，
    /// 然后发现按钮始终是灰的——那句话在这种情形下是错的。
    /// </remarks>
    public string EmptyNote => CanEdit
        ? "（还没有约束。选中两个及以上节点可以加同层或对齐。）"
        : "（还没有约束。这份文档改不动，加不了约束。）";

    /// <summary>最近一次加删的结果，一句话。没有时为负。</summary>
    public string? Note
    {
        get => _note;
        private set
        {
            _note = value;
            Raise(nameof(Note));
            Raise(nameof(HasNote));
        }
    }

    /// <summary>有没有那句话。</summary>
    public bool HasNote => !string.IsNullOrEmpty(_note);

    /// <summary>
    /// 这一份能不能改约束。
    /// </summary>
    /// <remarks>
    /// 只读时为假，三个加按钮与每行的删除都跟着灰掉。删除按钮不灰的话，
    /// 用户点下去会收到一句"这份文档是只读的"——那本来在按钮上就该看出来。
    /// </remarks>
    public bool CanEdit => !_session.IsReadOnly;

    /// <summary>选中两个及以上节点才能加同层约束。</summary>
    public bool CanAddSameRank => CanEdit && _session.SelectedIds.Count >= 2;

    /// <summary>选中两个及以上节点才能加对齐约束。</summary>
    public bool CanAddAlign => CanEdit && _session.SelectedIds.Count >= 2;

    /// <summary>
    /// 层内次序要"一个主语 + 至少两个目标"，而且主语到每个目标都要有一条边。
    /// </summary>
    /// <remarks>
    /// 次序在文档里存的是出边标识。选中的目标里有一个连不上，这一条次序就描述不出来，
    /// 所以按钮直接灰掉，而不是让用户点下去再收到一句"找不到边"。
    /// </remarks>
    public bool CanAddOrder => CanEdit && OrderPlan() is not null;

    /// <summary>按当前选中重读一遍。</summary>
    public void Refresh()
    {
        // 人工的排在最前面：能删的那几条是用户接下来要动的，而自动与模型推导出来的
        // 通常有几十条，把它们排前面的话用户要翻很久才找得到自己加的那一条。
        Rows = [.. Read().OrderBy(row => row.IsRemovable ? 0 : 1)];

        Raise(nameof(CanAddSameRank));
        Raise(nameof(CanAddAlign));
        Raise(nameof(CanAddOrder));
    }

    /// <summary>加一条同层约束。</summary>
    public CommandResult AddSameRank() =>
        Add(LayoutConstraintSpec.SameRank([.. _session.SelectedIds]), "已加同层约束");

    /// <summary>加一条对齐约束。</summary>
    public CommandResult AddAlign() =>
        Add(LayoutConstraintSpec.Align([.. _session.SelectedIds]), "已加对齐约束");

    /// <summary>加一条层内次序约束。选中次序就是先后次序。</summary>
    public CommandResult AddOrder() =>
        OrderPlan() is { } spec
            ? Add(spec, "已加层内次序约束")
            : Fail("顺序约束要选一个主语加至少两个目标，且主语到每个目标都有边");

    /// <summary>删掉一条人工约束。</summary>
    public CommandResult Remove(ConstraintRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.IsRemovable)
        {
            // 自动与模型约束由各自的来源负责。界面能删它们的话，模型刚提的约束
            // 会被人顺手删掉，而模型那边不知道自己提的东西没了。
            return Fail("只有人工加的约束能从界面上删");
        }

        var result = _session.RemoveConstraint(row.Spec!, row.Owner);

        Note = result.IsEffectiveSuccess ? "已删约束" : Describe(result);
        Refresh();

        return result;
    }

    /// <summary>把当前文档里的约束读成一行行。</summary>
    private IEnumerable<ConstraintRowViewModel> Read()
    {
        var layout = _session.Document.Layout;

        foreach (var constraint in layout.SameRank)
        {
            yield return Row(LayoutConstraintSpec.SameRank(constraint.Value.Nodes), "同层", constraint.Owner);
        }

        foreach (var constraint in layout.Order)
        {
            yield return Row(
                LayoutConstraintSpec.Order(constraint.Value.NodeId, constraint.Value.Order),
                $"层内次序（{constraint.Value.NodeId}）",
                constraint.Owner);
        }

        foreach (var constraint in layout.Align)
        {
            yield return Row(LayoutConstraintSpec.Align(constraint.Value.Nodes), "对齐", constraint.Owner);
        }

        foreach (var constraint in layout.Place)
        {
            // 相对位置属后续阶段，界面上还不给入口。读出来是为了让用户看到
            // "这条约束确实在"，而不是以为它没写进去——但删不了，所以规格为空。
            yield return new ConstraintRowViewModel(
                null,
                $"相对位置（{constraint.Value.NodeId} {Relation(constraint.Value.Relation)} {constraint.Value.RelativeTo}）",
                constraint.Owner);
        }
    }

    private static ConstraintRowViewModel Row(LayoutConstraintSpec spec, string kind, ConstraintOwner owner) =>
        new(spec, $"{kind} · {string.Join("、", spec.Members)}", owner);

    private static string Relation(PlaceRelation relation) => relation switch
    {
        PlaceRelation.RightOf => "在右侧",
        PlaceRelation.LeftOf => "在左侧",
        PlaceRelation.Above => "在上方",
        PlaceRelation.Below => "在下方",
        _ => relation.ToString(),
    };

    /// <summary>当前选中能拼出哪一条次序约束。拼不出来时为空。</summary>
    private LayoutConstraintSpec? OrderPlan()
    {
        var ids = _session.SelectedIds;

        // 主语 + 至少两个目标。少一个目标的话，那条次序只有一条边，
        // 而"一条边谁先谁后"没有内容。
        if (ids.Count < 3)
        {
            return null;
        }

        var subject = ids[0];
        var edges = new List<string>(ids.Count - 1);

        foreach (var target in ids.Skip(1))
        {
            var edge = _session.Document.Edges
                .FirstOrDefault(e => string.Equals(e.From, subject, StringComparison.Ordinal)
                    && string.Equals(e.To, target, StringComparison.Ordinal));

            if (edge is null)
            {
                return null;
            }

            edges.Add(edge.Id);
        }

        return LayoutConstraintSpec.Order(subject, edges);
    }

    private CommandResult Add(LayoutConstraintSpec spec, string done)
    {
        var result = _session.AddConstraint(spec);

        Note = result.IsEffectiveSuccess ? done : Describe(result);
        Refresh();

        return result;
    }

    private CommandResult Fail(string message)
    {
        Note = message;

        return CommandResult.Fail(CommandError.Of(ErrorCodes.LayoutConstraintInvalid, message));
    }

    private static string Describe(CommandResult result)
    {
        if (result.IsNoOp)
        {
            return "这条约束已经有了";
        }

        return result.Errors.Length == 0
            ? result.Message ?? "没有写进去"
            : result.Errors[0].Payload ?? result.Errors[0].Code;
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>面板上的一行约束。</summary>
/// <param name="Spec">
/// 这一行对应哪一条约束，删的时候按它找。只读的那些行（例如相对位置）为空——
/// 它们没有对应的规格，也就无从删起。
/// </param>
/// <param name="Title">给人看的一句话：哪一类、成员是谁。</param>
/// <param name="Owner">谁加的：人工、模型还是引擎。</param>
public sealed record ConstraintRowViewModel(LayoutConstraintSpec? Spec, string Title, ConstraintOwner Owner)
{
    /// <summary>
    /// 能不能从界面上删。
    /// </summary>
    /// <remarks>
    /// 只有人工加的能删。自动约束由引擎每一轮重新推导，删掉下一次求解又会回来；
    /// 模型约束的归属方是工具，界面替它删掉之后它并不知道自己提的东西没了。
    /// </remarks>
    public bool IsRemovable => Spec is not null && Owner == ConstraintOwner.Human;

    /// <summary>归属的中文名。</summary>
    public string OwnerText => Owner switch
    {
        ConstraintOwner.Human => "人工",
        ConstraintOwner.Llm => "模型",
        _ => "自动",
    };
}
