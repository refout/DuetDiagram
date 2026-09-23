using System.ComponentModel;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 图层面板。
/// </summary>
/// <remarks>
/// <para>
/// **这是「图层」这件事唯一看得见的地方。** 属性面板上一次只看得到一个元素的归属，
/// 而图层的意义在于「一层里有谁」——所以列表、开关、次序与归属入口都在这里。
/// </para>
/// <para>
/// **一项都不直接写文档。** 开关、新建、改名、挪次序、移入，各发一条命令：
/// 撤销、版本日志、广播、审计日志这几条因此自动成立。自己写的话每一条都要单独补一遍，
/// 而漏掉的那一条不会有任何提示。
/// </para>
/// <para>
/// **列表顺序取文档里的次序字段。** 面板自己维护一份顺序的话，
/// 撤销之后两边会对不上，而画面上看起来只是"顺序没变回来"。
/// 次序相同时用标识断并列，与渲染层出笔的口径一致——
/// 两处断法不同的话，面板上排在上面的那一层未必画在上面。
/// </para>
/// <para>
/// **当前图层不是文档里的东西。** 它是"接下来往哪儿放"这个意图，只活在面板里，
/// 所以不进 IR、不进哈希、撤销也不会把它带回上一轮的样子。
/// </para>
/// </remarks>
public sealed class LayerPanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly List<LayerRowViewModel> _rows = [];

    private string _currentLayerId = string.Empty;
    private int _selectionCount;
    private string? _emptyHint;

    public LayerPanelViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;

        // 文档变了、选中变了都重读一遍。由宿主每次手动调一次的话迟早会漏掉一条路径，
        // 而漏掉的表现是"文档变了但面板还显示旧值"，看不出是哪一次没接上。
        //
        // **不接 DocumentChanged。** 那条通知在后台投递线程上到达（别的窗口改了同一份文档），
        // 而这里一刷新就要把新值推进控件，控件只能在界面线程上碰。
        // 宿主收到它之后会排一次队去重算，重算完发 SceneChanged——那一条是在界面线程上的。
        _session.SceneChanged += Refresh;
        _session.SelectionChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>列表里的行，按文档里的次序。</summary>
    public IReadOnlyList<LayerRowViewModel> Rows => _rows;

    /// <summary>当前图层的标识。一个图层都没有时为空。</summary>
    public string CurrentLayerId => _currentLayerId;

    /// <summary>当前图层的名字。没有当前图层时为空。</summary>
    public string? CurrentLayerName =>
        _rows.FirstOrDefault(row => string.Equals(row.Id, _currentLayerId, StringComparison.Ordinal))?.Name;

    /// <summary>有没有当前图层。</summary>
    public bool HasCurrentLayer => _currentLayerId.Length > 0;

    /// <summary>当前图层那一句说明。</summary>
    public string CurrentLayerNote =>
        CurrentLayerName is { } name ? $"当前图层：{name}" : "还没有当前图层";

    /// <summary>这份文档改不动时面板顶上那一句。可写时为空。</summary>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>面板上要不要显示那一句。</summary>
    public bool HasReadOnlyNote => _session.IsReadOnly;

    /// <summary>一个图层都没有时的一句说明。</summary>
    public string? EmptyHint
    {
        get => _emptyHint;
        private set
        {
            if (string.Equals(_emptyHint, value, StringComparison.Ordinal))
            {
                return;
            }

            _emptyHint = value;
            Raise(nameof(EmptyHint));
            Raise(nameof(HasEmptyHint));
        }
    }

    /// <summary>要不要显示那句说明。</summary>
    public bool HasEmptyHint => !string.IsNullOrEmpty(_emptyHint);

    /// <summary>当前选中了几个元素。</summary>
    public int SelectionCount
    {
        get => _selectionCount;
        private set
        {
            if (_selectionCount == value)
            {
                return;
            }

            _selectionCount = value;
            Raise(nameof(SelectionCount));
            Raise(nameof(HasSelection));
            Raise(nameof(AssignLabel));
            Raise(nameof(CanAssign));
        }
    }

    /// <summary>有没有选中元素。</summary>
    public bool HasSelection => _selectionCount > 0;

    /// <summary>「移入」那个按钮上写的话。</summary>
    public string AssignLabel => _selectionCount > 0
        ? $"把选中的 {_selectionCount} 个元素移入"
        : "把选中的元素移入";

    /// <summary>能不能移入。没选中元素或还没有当前图层时不能。</summary>
    public bool CanAssign => _selectionCount > 0 && HasCurrentLayer && !_session.IsReadOnly;

    /// <summary>
    /// 把某一层定为当前图层。
    /// </summary>
    /// <remarks>
    /// 只改面板自己的状态：它不发命令、不进文档，所以撤销回不到"上一次点的那一层"。
    /// 那是对的——撤销该撤的是对文档做的事，而点一下某一层什么都没做。
    /// </remarks>
    public void Select(string layerId)
    {
        ArgumentNullException.ThrowIfNull(layerId);

        if (_rows.Any(row => string.Equals(row.Id, layerId, StringComparison.Ordinal)))
        {
            SetCurrent(layerId);
        }
    }

    /// <summary>
    /// 新建一层。
    /// </summary>
    /// <remarks>
    /// 新建之后立刻把它定为当前图层：用户刚建一层，接下来多半要往里放东西，
    /// 而当前图层还停在别处的话，那一次「移入」会落到另一层上。
    /// </remarks>
    public CommandResult Create(string? name)
    {
        var result = _session.CreateLayer(name);

        if (result.IsEffectiveSuccess && result.AffectedIds.Length > 0)
        {
            Refresh();
            SetCurrent(result.AffectedIds[0]);
        }

        return result;
    }

    /// <summary>给当前图层改名。</summary>
    public CommandResult Rename(string? name)
    {
        if (!HasCurrentLayer)
        {
            return ReportNoCurrentLayer();
        }

        return _session.RenameLayer(_currentLayerId, name ?? string.Empty);
    }

    /// <summary>把当前图层往上挪一位。</summary>
    public CommandResult MoveUp() => Move(-1);

    /// <summary>把当前图层往下挪一位。</summary>
    public CommandResult MoveDown() => Move(+1);

    /// <summary>藏起来或者放出来。</summary>
    public CommandResult ToggleVisible(string layerId)
    {
        ArgumentNullException.ThrowIfNull(layerId);

        return _session.SetLayerVisible(layerId, !Visible(layerId));
    }

    /// <summary>锁上或者解锁。</summary>
    public CommandResult ToggleLocked(string layerId)
    {
        ArgumentNullException.ThrowIfNull(layerId);

        return _session.SetLayerLocked(layerId, !Locked(layerId));
    }

    /// <summary>
    /// 把选中的元素移到当前图层上。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **一条命令，因此撤销一次就全回去。** 逐个发字段写入也能改，
    /// 但撤销要按很多次，而用户做的是同一次操作。
    /// </para>
    /// <para>
    /// 选中的元素里有锁着的、或者当前图层锁着，两种都由会话挡下来并给出结构化错误。
    /// </para>
    /// </remarks>
    public CommandResult AssignSelection()
    {
        if (!HasCurrentLayer)
        {
            return ReportNoCurrentLayer();
        }

        return _session.AssignLayer(_session.SelectedIds, _currentLayerId);
    }

    /// <summary>按文档里的图层重读一遍。</summary>
    public void Refresh()
    {
        var layers = Ordered();

        // 行的集合只在图层增删时变。换选中、挪次序都不该重建行——
        // 重建会让用户正在输入的那个框失焦，而挪次序时正需要它保持焦点。
        if (!SameIds(layers))
        {
            _rows.Clear();

            foreach (var layer in layers)
            {
                _rows.Add(new LayerRowViewModel(layer.Id));
            }

            Raise(nameof(Rows));
        }

        // 当前图层没了（被撤销掉了）就挑一个顶上：没有当前图层的话「移入」点不动，
        // 而用户看不出为什么。
        if (_rows.Count > 0 && !_rows.Any(row => row.Id == _currentLayerId))
        {
            SetCurrent(_rows[0].Id);
        }
        else if (_rows.Count == 0 && _currentLayerId.Length > 0)
        {
            SetCurrent(string.Empty);
        }

        var counts = Counts();

        for (var index = 0; index < layers.Count && index < _rows.Count; index++)
        {
            var layer = layers[index];

            _rows[index].Set(
                layer,
                current: layer.Id == _currentLayerId,
                elements: counts.GetValueOrDefault(layer.Id),
                canMoveUp: index > 0,
                canMoveDown: index < layers.Count - 1);
        }

        EmptyHint = _rows.Count == 0
            ? "还没有图层。不在任何图层上的元素归一个隐含的缺省层，它画在最底下。"
            : null;

        SelectionCount = _session.SelectedIds.Count;

        Raise(nameof(CurrentLayerId));
        Raise(nameof(CurrentLayerName));
        Raise(nameof(HasCurrentLayer));
        Raise(nameof(CurrentLayerNote));
        Raise(nameof(CanAssign));
    }

    #region 内部

    /// <summary>
    /// 文档里的图层，按次序排好。
    /// </summary>
    /// <remarks>
    /// 次序相同时用标识断并列。与渲染层出笔的口径一致——
    /// 两处断法不同的话，面板上排在上面的那一层未必画在上面。
    /// </remarks>
    private List<LayerDef> Ordered() =>
        [.. _session.Document.Layers
            .OrderBy(layer => layer.Order)
            .ThenBy(layer => layer.Id, StringComparer.Ordinal)];

    private bool SameIds(IReadOnlyList<LayerDef> layers)
    {
        if (layers.Count != _rows.Count)
        {
            return false;
        }

        for (var index = 0; index < layers.Count; index++)
        {
            if (!string.Equals(layers[index].Id, _rows[index].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>每一层上有几个元素。</summary>
    /// <remarks>
    /// 从文档算，不另存一份计数：另存的话，撤销、导入、模型那边的改动都要记得同步它，
    /// 而漏掉任何一条路的表现都是"计数是错的"，看起来像数据坏了。
    /// </remarks>
    private Dictionary<string, int> Counts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in _session.Document.Nodes)
        {
            if (node.Layer is { } layerId)
            {
                counts[layerId] = counts.GetValueOrDefault(layerId) + 1;
            }
        }

        return counts;
    }

    private CommandResult Move(int delta)
    {
        if (!HasCurrentLayer)
        {
            return ReportNoCurrentLayer();
        }

        var index = _rows.FindIndex(row => row.Id == _currentLayerId);

        if (index < 0)
        {
            return ReportNoCurrentLayer();
        }

        // 夹紧到一个仍然合理的位置，而不是把整条拒掉：索引可能来自一个在请求发出之后
        // 就已经过期的界面状态（列表刚被撤销改过）。
        var target = Math.Clamp(index + delta, 0, _rows.Count - 1);

        // **已经在端点也照样交给会话。** 在这里直接回一句"已经在最上面"的话，
        // 只读文档上这一下会报成功，而它一个字都没写进去——面板上每一个写入口
        // 都该在只读时给出拒绝，哪怕那一下本来什么都不会发生。
        return _session.ReorderLayer(_currentLayerId, target);
    }

    private bool Visible(string layerId) =>
        _rows.FirstOrDefault(row => row.Id == layerId)?.Visible ?? true;

    private bool Locked(string layerId) =>
        _rows.FirstOrDefault(row => row.Id == layerId)?.Locked ?? false;

    private void SetCurrent(string layerId)
    {
        if (string.Equals(_currentLayerId, layerId, StringComparison.Ordinal))
        {
            return;
        }

        _currentLayerId = layerId;

        foreach (var row in _rows)
        {
            row.SetCurrent(row.Id == _currentLayerId);
        }

        Raise(nameof(CurrentLayerId));
        Raise(nameof(CurrentLayerName));
        Raise(nameof(HasCurrentLayer));
        Raise(nameof(CurrentLayerNote));
        Raise(nameof(CanAssign));
    }

    /// <summary>没有当前图层时的统一答复。</summary>
    /// <remarks>
    /// 成一个方法而不是各写一句：它出现的两个地方都是"用户按了一个按钮，
    /// 而按钮该不该可点由 <see cref="CanAssign"/> 决定"，说法一致才不会让人以为
    /// 遇到的是两个问题。
    /// </remarks>
    private static CommandResult ReportNoCurrentLayer() =>
        CommandResult.Fail(CommandError.Of(ErrorCodes.LayerMissing, "先在图层面板里选一层"));

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
