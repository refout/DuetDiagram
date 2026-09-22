using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 加一条布局约束。
/// </summary>
/// <remarks>
/// <para>
/// **一条命令覆盖三类约束**，而不是一类一条。三类的成员形状不同（同层与对齐是节点，
/// 层内次序是出边），但增删的动作一样：找到那一类的列表，改它。一类一条命令的话，
/// 校验、原子性与撤销三套逻辑要各写三遍，而抄漏的那一遍不会报错，
/// 只会让某一类约束在某个入口下改不动。
/// </para>
/// <para>
/// **归属必须由调用方显式给出。** 它是降级矩阵的输入：布局解不出来时按归属决定先丢谁，
/// 人工定的比模型提的更值得保留。给一个默认值的话，模型那条路径会悄悄写出人工归属的约束，
/// 而降级时被当成"用户设的"保住——用户根本没设过它。
/// </para>
/// <para>
/// **层内次序是"每个主语一条"，不是"每加一次一条"。** 同一个主语上已经有次序约束时
/// 换掉它而不是再追加一条。追加的话，拖动几次就会在同一对节点上积下几十条互相矛盾的次序，
/// 而求解器按列表顺序取第一条，于是生效的是最早那一次——用户后来拖的那几下全都白做。
/// </para>
/// </remarks>
public sealed class AddLayoutConstraintCommand : DiagramCommandBase
{
    public const string Id = "add-layout-constraint";

    private readonly LayoutConstraintSpec _spec;
    private readonly ConstraintOwner _owner;

    public AddLayoutConstraintCommand(LayoutConstraintSpec spec, ConstraintOwner owner)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(spec);

        _spec = spec;
        _owner = owner;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _spec.Kind == LayoutConstraintKind.Order
            ? ValidateOrder(document)
            : ValidateGroup(document);
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var updated = WithConstraint(document.Layout, out var changed);

        if (!changed)
        {
            return CommandResult.NoOp("这条约束已经有了");
        }

        document.Layout = updated;

        return CommandResult.Ok(
            affected: [.. _spec.Members],
            changes:
            [
                new FieldChange
                {
                    ElementId = _spec.Subject ?? _spec.Members[0],
                    Field = _spec.Field,
                    OldValue = null,
                    NewValue = string.Join("、", _spec.Members),
                    Kind = ChangeKind.Added,
                },
            ],

            // 布局约束进的是结构哈希，而那个哈希要回答的正是"要不要重新求解布局"。
            // 报成纯外观的话，宿主会只重绘不重排，而画面上的坐标根本没跟着约束变。
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new AddLayoutConstraintCommand(_spec, _owner);

    protected override CommandMemento CaptureCore(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new LayoutConstraintMemento
        {
            Previous = document.Layout,
            AffectedIds = [.. _spec.Members],
            InverseChanges =
            [
                new FieldChange
                {
                    ElementId = _spec.Subject ?? _spec.Members[0],
                    Field = _spec.Field,
                    OldValue = string.Join("、", _spec.Members),
                    NewValue = null,
                    Kind = ChangeKind.Removed,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);

        // 整份换回去而不是"把新加的那条摘掉"：约束列表可能被别的命令动过，
        // 按条摘的话摘到的未必是这一条加进去的那一份，而那种错只会体现在哈希上。
        document.Layout = ((LayoutConstraintMemento)memento).Previous;
    }

    /// <summary>同层与对齐：一组平级的节点。</summary>
    private ValidationResult ValidateGroup(DiagramDocument document)
    {
        if (_spec.Subject is not null)
        {
            return Invalid($"这一类约束没有主语，实际给了 {_spec.Subject}");
        }

        if (_spec.Members.Count < 2)
        {
            return Invalid($"至少两个成员，实际 {_spec.Members.Count} 个");
        }

        if (Duplicate(_spec.Members) is { } duplicate)
        {
            return Invalid($"成员重复：{duplicate}");
        }

        var missing = _spec.Members.Where(id => document.FindNode(id) is null).ToList();

        return missing.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(
                CommandError.Of(ErrorCodes.LayoutNodeMissing, string.Join("、", missing)));
    }

    /// <summary>层内次序：一个主语加它的若干条出边。</summary>
    private ValidationResult ValidateOrder(DiagramDocument document)
    {
        if (string.IsNullOrWhiteSpace(_spec.Subject))
        {
            return Invalid("层内次序要有主语节点");
        }

        if (_spec.Members.Count < 2)
        {
            return Invalid($"至少两条出边，实际 {_spec.Members.Count} 条");
        }

        if (Duplicate(_spec.Members) is { } duplicate)
        {
            return Invalid($"次序里重复出现：{duplicate}");
        }

        if (document.FindNode(_spec.Subject) is null)
        {
            return ValidationResult.Invalid(
                CommandError.Of(ErrorCodes.LayoutNodeMissing, _spec.Subject));
        }

        var wrong = _spec.Members
            .Where(id => document.FindEdge(id) is not { } edge
                || !string.Equals(edge.From, _spec.Subject, StringComparison.Ordinal))
            .ToList();

        return wrong.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(
                CommandError.Of(ErrorCodes.LayoutOrderEdgeMissing, string.Join("、", wrong)));
    }

    /// <summary>按规格算出新的布局提示，并说明有没有真的改动。</summary>
    private LayoutHints WithConstraint(LayoutHints layout, out bool changed)
    {
        var created = CreatedAt();

        switch (_spec.Kind)
        {
            case LayoutConstraintKind.SameRank:
                if (layout.SameRank.Any(c => c.Owner == _owner && _spec.Matches(c)))
                {
                    changed = false;
                    return layout;
                }

                changed = true;

                return layout with
                {
                    SameRank =
                    [
                        .. layout.SameRank,
                        new Constraint<SameRankConstraint>(new SameRankConstraint(_spec.Members), _owner, created),
                    ],
                };

            case LayoutConstraintKind.Align:
                if (layout.Align.Any(c => c.Owner == _owner && _spec.Matches(c)))
                {
                    changed = false;
                    return layout;
                }

                changed = true;

                return layout with
                {
                    Align =
                    [
                        .. layout.Align,
                        new Constraint<AlignConstraint>(new AlignConstraint(_spec.Members), _owner, created),
                    ],
                };

            default:
                return WithOrder(layout, created, out changed);
        }
    }

    private LayoutHints WithOrder(LayoutHints layout, DateTimeOffset created, out bool changed)
    {
        var value = new OrderConstraint(_spec.Subject!, _spec.Members);
        var entry = new Constraint<OrderConstraint>(value, _owner, created);

        var existing = layout.Order.FirstOrDefault(c =>
            c.Owner == _owner && string.Equals(c.Value.NodeId, _spec.Subject, StringComparison.Ordinal));

        if (existing is null)
        {
            changed = true;
            return layout with { Order = [.. layout.Order, entry] };
        }

        if (existing.Value == value)
        {
            changed = false;
            return layout;
        }

        changed = true;

        return layout with
        {
            Order =
            [
                .. layout.Order.Select(c =>
                    c.Owner == _owner && string.Equals(c.Value.NodeId, _spec.Subject, StringComparison.Ordinal)
                        ? entry
                        : c),
            ],
        };
    }

    /// <summary>
    /// 这条约束的创建时刻。
    /// </summary>
    /// <remarks>
    /// 时刻由命令总线在执行那一刻回填。直接调 <see cref="Apply"/> 的路径没有执行时刻，
    /// 这时写一个确定的纪元值而不是 0001 年——那个日期在文件里看起来像坏数据。
    /// 它不参与任何哈希，所以取什么值都不影响"同一份输入是不是同一份"。
    /// </remarks>
    private DateTimeOffset CreatedAt() =>
        Context.Timestamp == default ? DateTimeOffset.UnixEpoch : Context.Timestamp;

    private static ValidationResult Invalid(string detail) =>
        ValidationResult.Invalid(CommandError.Of(ErrorCodes.LayoutConstraintInvalid, detail));

    private static string? Duplicate(IReadOnlyList<string> members)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        return members.FirstOrDefault(member => !seen.Add(member));
    }
}
