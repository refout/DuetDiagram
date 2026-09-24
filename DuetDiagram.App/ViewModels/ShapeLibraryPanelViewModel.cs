using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;
using DuetDiagram.Core.Shapes;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 形状面板的状态：一份形状清单，点一个就把选中的节点换成它。
/// </summary>
/// <remarks>
/// <para>
/// **清单来自形状库，不来自枚举反射。** 面板上有什么可挑，与渲染层画得出什么、
/// 工具接受什么，读的是同一份表——三处各自列举的话，加了形状而漏改一处的表现是
/// "画布上能画、面板上没有"或者反过来，两种都要等人点到才发现。
/// </para>
/// <para>
/// **它改的是元素，与调色板、文本预设两个面板相反。** 那两处改的是文档级的清单，
/// 元素只是引用它们；形状是节点自己的字段，所以这里点一下就是一次字段编辑，
/// 与属性面板里改那个字段走同一条路（<see cref="DiagramSession.Apply"/>）。
/// </para>
/// </remarks>
public sealed class ShapeLibraryPanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;

    private string? _error;

    public ShapeLibraryPanelViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        Rows =
        [
            .. ShapeRegistry.Default.All.Select(definition =>
                new ShapeRowViewModel(definition, () => Apply(definition.Shape))),
        ];

        // 与图层面板同一套接线：不接 DocumentChanged（它在后台线程上到），
        // 宿主重算完发的 SceneChanged 才在界面线程上。选中变了也要重铺一遍——
        // "哪一个是当前形状"取决于选中了谁。
        _session.SceneChanged += Refresh;
        _session.SelectionChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>形状行，按形状库给出的顺序。</summary>
    public IReadOnlyList<ShapeRowViewModel> Rows { get; }

    /// <summary>画布上有没有选中的节点。没有的话点形状没有落点。</summary>
    public bool HasSelection => _session.SelectedNodes.Count > 0;

    /// <summary>最近一次失败的一句话。成功一次就清掉。</summary>
    public string? Error
    {
        get => _error;

        private set
        {
            if (!string.Equals(_error, value, StringComparison.Ordinal))
            {
                _error = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>有没有要显示的错误。</summary>
    public bool HasError => Error is not null;

    /// <summary>只读的原因。可写时为空。</summary>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>有没有只读提示要显示。</summary>
    public bool HasReadOnlyNote => ReadOnlyNote is not null;

    /// <summary>把选中的节点换成这个形状。</summary>
    /// <remarks>
    /// 走会话的批量字段入口：多选时每个节点各发一条命令，撤销按一次退回一个——
    /// 与属性面板里改同一个字段的表现一致，两处不该对同一件事有两种撤销手感。
    /// </remarks>
    public void Apply(NodeShape shape)
    {
        Error = null;

        if (!HasSelection)
        {
            Error = "先在画布上选中节点，再挑形状";
            return;
        }

        var result = _session.Apply(FieldNames.Shape, shape.ToString());

        if (result.IsNoOp)
        {
            Error = result.Message ?? "选中的节点已经是这个形状了";
        }
        else if (!result.IsEffectiveSuccess)
        {
            Error = Describe(result);
        }

        Refresh();
    }

    /// <summary>按当前文档与选中重读一遍「哪一个是当前形状」。</summary>
    public void Refresh()
    {
        var current = CurrentShape();

        foreach (var row in Rows)
        {
            row.SetCurrent(row.Shape == current);
        }

        // 清单本身不变（形状库是静态的），但"哪一行亮着"变了，所以还是要喊一声——
        // 界面按这一声重铺那几行。
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// 选中节点的共同形状。选中的不是一个形状、或者没有选中时为空。
    /// </summary>
    /// <remarks>
    /// 多选了几个不同形状的节点时不标任何一行：标第一个的话，
    /// 用户会以为"这就是它们现在的形状"，而实际上另外几个不是。
    /// </remarks>
    private NodeShape? CurrentShape()
    {
        var selected = _session.SelectedNodes;

        if (selected.Count == 0)
        {
            return null;
        }

        var first = selected[0].Shape;

        return selected.All(node => node.Shape == first) ? first : null;
    }

    private static string Describe(CommandResult result)
    {
        if (result.Errors.Length == 0)
        {
            return result.Message ?? "这一次改动没有写进去";
        }

        var error = result.Errors[0];

        return string.IsNullOrWhiteSpace(error.Payload) ? error.Code : error.Payload;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}

/// <summary>
/// 形状清单里的一行。
/// </summary>
/// <remarks>
/// 与调色板那几行的口径一致：行只带"画什么"与"点了做什么"，不含任何
/// "这个形状是什么意思"的判断。
/// </remarks>
public sealed class ShapeRowViewModel : INotifyPropertyChanged
{
    private bool _isCurrent;

    public ShapeRowViewModel(ShapeDefinition definition, Action apply)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(apply);

        Shape = definition.Shape;
        Name = definition.Name;
        Apply = apply;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>这个形状的枚举值。预览按它取几何。</summary>
    public NodeShape Shape { get; }

    /// <summary>形状名。与工具、属性面板用的是同一个名字。</summary>
    public string Name { get; }

    /// <summary>点这一行做什么。</summary>
    public Action Apply { get; }

    /// <summary>选中的节点是不是都已经是这个形状。是的话行上带个记号。</summary>
    public bool IsCurrent
    {
        get => _isCurrent;

        private set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    /// <summary>更新「当前形状」记号。</summary>
    public void SetCurrent(bool value) => IsCurrent = value;
}
