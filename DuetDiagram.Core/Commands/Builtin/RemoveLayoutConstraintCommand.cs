using DuetDiagram.Core.Model;

namespace DuetDiagram.Core.Commands.Builtin;

/// <summary>
/// 删一条布局约束。
/// </summary>
/// <remarks>
/// <para>
/// 按"是不是这一条"来找，不按序号。序号会随别的增删而变，而界面手上那份列表可能已经过期——
/// 按序号删的话，删掉的会是另一个位置上的一条，而用户看到的是"我删了这条，消失的是另一条"。
/// </para>
/// <para>
/// **要删的那条不存在时报错，不报无操作。** 删一条本来就不在的约束，想要的终态确实已经达到，
/// 但那通常意味着调用方手上的列表过期了；报成功会让它继续拿一份错的列表往下走。
/// </para>
/// </remarks>
public sealed class RemoveLayoutConstraintCommand : DiagramCommandBase
{
    public const string Id = "remove-layout-constraint";

    private readonly LayoutConstraintSpec _spec;
    private readonly ConstraintOwner _owner;

    public RemoveLayoutConstraintCommand(LayoutConstraintSpec spec, ConstraintOwner owner)
        : base(Id)
    {
        ArgumentNullException.ThrowIfNull(spec);

        _spec = spec;
        _owner = owner;
    }

    public override ValidationResult Validate(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Exists(document.Layout)
            ? ValidationResult.Valid
            : ValidationResult.Invalid(
                CommandError.Of(ErrorCodes.LayoutConstraintMissing, Describe()));
    }

    public override CommandResult Apply(DiagramDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Exists(document.Layout))
        {
            return CommandResult.Fail(
                CommandError.Of(ErrorCodes.LayoutConstraintMissing, Describe()));
        }

        document.Layout = Without(document.Layout);

        return CommandResult.Ok(
            affected: [.. _spec.Members],
            changes:
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
            structural: true,
            visual: true);
    }

    protected override DiagramCommandBase CloneWith(ChangeContext context) =>
        new RemoveLayoutConstraintCommand(_spec, _owner);

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
                    OldValue = null,
                    NewValue = string.Join("、", _spec.Members),
                    Kind = ChangeKind.Added,
                },
            ],
        };
    }

    protected override void RestoreCore(DiagramDocument document, CommandMemento memento)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Layout = ((LayoutConstraintMemento)memento).Previous;
    }

    private bool Exists(LayoutHints layout) => _spec.Kind switch
    {
        LayoutConstraintKind.SameRank => layout.SameRank.Any(c => c.Owner == _owner && _spec.Matches(c)),
        LayoutConstraintKind.Align => layout.Align.Any(c => c.Owner == _owner && _spec.Matches(c)),
        _ => layout.Order.Any(c => c.Owner == _owner && _spec.Matches(c)),
    };

    private LayoutHints Without(LayoutHints layout) => _spec.Kind switch
    {
        LayoutConstraintKind.SameRank => layout with
        {
            SameRank = [.. layout.SameRank.Where(c => c.Owner != _owner || !_spec.Matches(c))],
        },

        LayoutConstraintKind.Align => layout with
        {
            Align = [.. layout.Align.Where(c => c.Owner != _owner || !_spec.Matches(c))],
        },

        _ => layout with
        {
            Order = [.. layout.Order.Where(c => c.Owner != _owner || !_spec.Matches(c))],
        },
    };

    /// <summary>给用户看的一句话，说清要删的是哪一条。</summary>
    private string Describe()
    {
        var subject = _spec.Subject is null ? string.Empty : $"{_spec.Subject} 上的";
        var kind = _spec.Kind switch
        {
            LayoutConstraintKind.SameRank => "同层",
            LayoutConstraintKind.Order => "层内次序",
            _ => "对齐",
        };

        return $"{subject}{kind}约束（{string.Join("、", _spec.Members)}）";
    }
}
