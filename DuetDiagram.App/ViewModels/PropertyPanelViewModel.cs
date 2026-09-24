using System.ComponentModel;
using System.Globalization;
using System.Text;
using DuetDiagram.App.Services;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.ViewModels;

/// <summary>
/// 选中元素的属性面板。
/// </summary>
/// <remarks>
/// <para>
/// **七个分节，字段来自字段表。** 界面描述表只补显示名、归到哪一节、用哪种控件；
/// 字段名一律取自字段表，两边对不上时在启动阶段就抛异常，而不是等用户点到那个控件。
/// </para>
/// <para>
/// **切换选中不重建树。** 分节与字段在构造时建好一次，换选中只是把新值读进来。
/// 每次换选中重建一遍的话，连续点选时面板会明显卡顿——而"连续点选"正是
/// 用户在找元素时的常态。
/// </para>
/// <para>
/// **改文档只走命令层。** 面板不直接写 IR，也不持有文档的可变视图。
/// 走命令层之后，撤销、版本日志、广播、审计日志这几条自动都成立；
/// 自己写的话每一条都要单独补一遍，而漏掉的那一条不会有任何提示。
/// </para>
/// <para>
/// **多选只显示节点共有的字段。** 今天选中集合里只会有节点（收选中的那一处就把
/// 别的种类挡掉了），所以"共有的字段"就是节点字段表的全部。等边与组合也能被选中时，
/// 那个挡的地方是唯一要改的入口，这里的读值逻辑不用动——它对每个字段逐元素比一遍，
/// 本来就不假设各元素的字段表相同。
/// </para>
/// <para>
/// 图层、端口、动作与链接这三节只读。图层归属要给一个可选取值的下拉，
/// 而那张取值表是编译期写死的、图层标识是运行期才有的，所以改归属的入口在图层面板上，
/// 这里只把当前归属显示出来（<see cref="DescribeLayer"/>）；端口要等连线交互里的端口编辑
/// （不然改了端口看不到效果），动作要等动作编辑界面。先把它们做成可改的话，
/// 会得到一个能点但没效果的控件。
/// </para>
/// </remarks>
public sealed class PropertyPanelViewModel : INotifyPropertyChanged
{
    private readonly DiagramSession _session;
    private readonly Dictionary<string, PropertyFieldViewModel> _byField = new(StringComparer.Ordinal);

    private string _title = string.Empty;
    private string? _selectionNote;
    private bool _hasSelection;

    public PropertyPanelViewModel(DiagramSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Sections = Build();
        Constraints = new ConstraintEditorViewModel(session);

        // 换选中、命令执行成功、撤销与重做都会走到这两个事件上，面板据此重读一遍。
        // 由宿主每次手动调一次刷新的话，迟早会漏掉一条路径——而漏掉的表现是
        // "文档变了但面板还显示旧值"，看不出是哪一次没接上。
        _session.SelectionChanged += Refresh;
        _session.SceneChanged += Refresh;

        Refresh();
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>六个分节，按界面上的顺序。</summary>
    public IReadOnlyList<PropertySectionViewModel> Sections { get; }

    /// <summary>布局约束那一节的编辑器。它管的是整份文档的约束，不是某一个元素身上的。</summary>
    public ConstraintEditorViewModel Constraints { get; }

    /// <summary>面板标题：选中的是谁，或者一句"没有选中"。</summary>
    public string Title
    {
        get => _title;
        private set
        {
            if (string.Equals(_title, value, StringComparison.Ordinal))
            {
                return;
            }

            _title = value;
            Raise(nameof(Title));
        }
    }

    /// <summary>
    /// 多选时的一句说明。单选与没选中时为空。
    /// </summary>
    /// <remarks>
    /// 多选下改一个字段等于给每个选中元素赋同一个值，这件事必须写在界面上。
    /// 不写的话，用户以为改的只是"当前那个"，而实际上整批都被改了——
    /// 等他发现时已经找不到原来的值是什么了。
    /// </remarks>
    public string? SelectionNote
    {
        get => _selectionNote;
        private set
        {
            if (string.Equals(_selectionNote, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectionNote = value;
            Raise(nameof(SelectionNote));
            Raise(nameof(HasSelectionNote));
        }
    }

    /// <summary>有没有那一段多选说明。</summary>
    public bool HasSelectionNote => !string.IsNullOrEmpty(_selectionNote);

    /// <summary>
    /// 这份文档改不动时面板顶上那一句。可写时为空。
    /// </summary>
    /// <remarks>
    /// 与状态栏上那一句是同一件事的两个位置。字段全灰着而没有任何说明的话，
    /// 用户会以为面板坏了，而不是想到"另一个进程正开着这份文档"。
    /// </remarks>
    public string? ReadOnlyNote => _session.ReadOnlyReason;

    /// <summary>面板上要不要显示那一句。</summary>
    public bool HasReadOnlyNote => _session.IsReadOnly;

    /// <summary>当前有没有选中东西。没有时面板上不该出现任何可改的控件。</summary>
    public bool HasSelection
    {
        get => _hasSelection;
        private set
        {
            if (_hasSelection == value)
            {
                return;
            }

            _hasSelection = value;
            Raise(nameof(HasSelection));
        }
    }

    /// <summary>
    /// 按当前选中的元素重读一遍面板上的值。
    /// </summary>
    /// <remarks>
    /// 换选中、命令执行成功、撤销与重做之后都会走到这里。它只读不写：
    /// 面板显示的值永远来自文档，而不是用户上一次敲进去的东西——
    /// 用户敲进去的值可能被命令拒掉，那时界面上的旧值就是假的。
    /// </remarks>
    public void Refresh()
    {
        var nodes = _session.SelectedNodes;
        var composites = _session.SelectedComposites;

        HasSelection = nodes.Count > 0;
        Title = (nodes.Count, composites.Count) switch
        {
            // 组合没有字段表（它没有要编辑的属性），字段一节照旧收起来；
            // 但标题要照实说——"没有选中任何元素"是在撒谎，明明点中的是组合。
            (0, 1) => $"组合 {composites[0].Id}",
            (0, _) => $"选中 {composites.Count} 个组合",
            (1, 0) => $"节点 {nodes[0].Id}",
            (_, 0) => $"选中 {nodes.Count} 个元素",
            (_, _) => $"选中 {nodes.Count + composites.Count} 个元素",
        };

        SelectionNote = nodes.Count > 1
            ? $"这 {nodes.Count} 个元素：改一个字段等于给它们都赋同一个值，值不一致的字段显示为「多个值」。"
            : null;

        foreach (var field in _byField.Values)
        {
            var (value, mixed) = Common(nodes, field.Field);

            field.Set(value, mixed);
        }

        SetReadOnlyText(nodes);

        // 样式令牌的下拉选项跟着调色板走：面板上建的、删的条目，这里就是可选的名字。
        // 文档变了要重推一遍——命令可能建了新条目，也可能删掉了正在用的那个。
        if (_byField.TryGetValue(FieldNames.StyleToken, out var tokenField))
        {
            tokenField.SetChoices(
            [
                .. _session.Document.Palette.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal),
            ]);
        }

        // 约束编辑器读的是整份文档，与选中无关；但"能不能加"由选中决定，
        // 所以换选中也要跟着重读一遍按钮的可用状态。
        Constraints.Refresh();
    }

    /// <summary>
    /// 选中的这些元素在某个字段上的共同值。
    /// </summary>
    /// <remarks>
    /// 逐元素比一遍，不假设各元素的字段表相同——今天选中集合里只有节点，
    /// 但这条逻辑本身不该依赖这一点。比不出来时返回"多个值"而不是取其中一个，
    /// 理由见 <see cref="PropertyFieldViewModel.IsMixed"/>。
    /// </remarks>
    private static (string? Value, bool Mixed) Common(IReadOnlyList<NodeDef> nodes, string field)
    {
        if (nodes.Count == 0)
        {
            return (null, false);
        }

        var first = NodeFieldValue.Read(nodes[0], field);

        for (var index = 1; index < nodes.Count; index++)
        {
            if (!string.Equals(NodeFieldValue.Read(nodes[index], field), first, StringComparison.Ordinal))
            {
                return (null, true);
            }
        }

        return (first, false);
    }

    private void SetReadOnlyText(IReadOnlyList<NodeDef> nodes)
    {
        var document = _session.Document;
        var single = nodes.Count == 1 ? nodes[0] : null;

        Section("图层").SetText(single is null ? Placeholder(nodes.Count) : DescribeLayer(document, single));
        Section("端口").SetText(single is null ? Placeholder(nodes.Count) : DescribePorts(single));
        Section("动作与链接").SetText(single is null ? Placeholder(nodes.Count) : DescribeActions(document, single.Id));
    }

    /// <summary>
    /// 一个元素归在哪一层。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **只显示，不给控件。** 改归属的入口在图层面板上：那里才看得到全部图层，
    /// 也多选之后一次移入。在这里放一个可选取值的下拉是做不到的——
    /// 那段取值表是编译期写死的，而图层标识是运行期才有的。
    /// </para>
    /// <para>
    /// 归属指向一个不存在的图层时说清楚"按缺省层画"，而不是把那个标识原样摆出来：
    /// 后者看起来像一个正常的归属，而画面上它在最底下。
    /// </para>
    /// </remarks>
    private static string DescribeLayer(DiagramDocument document, NodeDef node)
    {
        const string Where = "改归属在图层面板上，那里也多选之后一次移入。";

        if (node.Layer is not { } layerId)
        {
            return $"缺省层（不在任何图层上，画在最底下）。{Where}";
        }

        var layer = document.Layers.FirstOrDefault(item => string.Equals(item.Id, layerId, StringComparison.Ordinal));

        if (layer is null)
        {
            return $"指向一个不存在的图层 {layerId}，按缺省层画。{Where}";
        }

        var name = string.IsNullOrWhiteSpace(layer.Name) ? layer.Id : layer.Name;

        return layer.Locked
            ? $"{name}（锁着：这一层上的东西点不中、改不了）。{Where}"
            : $"{name}。{Where}";
    }

    private static string? Placeholder(int count) =>
        count == 0 ? null : "选中多个元素，这几节只看单个元素的归属。";

    private PropertySectionViewModel Section(string title) =>
        Sections.First(s => string.Equals(s.Title, title, StringComparison.Ordinal));

    /// <summary>
    /// 建六个分节。只建一次。
    /// </summary>
    /// <remarks>
    /// 分节的顺序与标题取自描述表，不是在这里另写一遍——
    /// 另写一遍的话，描述表里改了一节的归属，界面上那一节就空了，
    /// 而空的那一节看起来只是"这一轮没做"。
    /// </remarks>
    private List<PropertySectionViewModel> Build()
    {
        var sections = new List<PropertySectionViewModel>(PropertyFieldCatalog.Sections.Count);

        foreach (var title in PropertyFieldCatalog.Sections)
        {
            var specs = PropertyFieldCatalog.All
                .Where(s => string.Equals(s.Section, title, StringComparison.Ordinal))
                .ToList();

            var fields = new List<PropertyFieldViewModel>(specs.Count);

            foreach (var spec in specs)
            {
                // 只读在构造时定下来：它由"另一个进程有没有拿着这份文档"决定，
                // 而那个答案在会话建好之后不会再变。字段跟着会话走，
                // 免得面板自己再判断一遍，两处判断迟早会分叉。
                var field = new PropertyFieldViewModel(spec) { IsReadOnly = _session.IsReadOnly };

                field.Committed += OnCommitted;
                _byField[spec.Field] = field;
                fields.Add(field);
            }

            sections.Add(new PropertySectionViewModel(title, fields));
        }

        return sections;
    }

    private void OnCommitted(PropertyFieldViewModel field, string? value)
    {
        var result = _session.Apply(field.Field, value);

        if (result.IsEffectiveSuccess)
        {
            // 写进去之后不在这里读回来：命令成功会触发一次整体刷新，
            // 那次刷新读的才是文档里的真值（命令可能把值规整过，例如把空串读成"没有值"）。
            return;
        }

        // 界面上的值回到文档里的那份。不回的话，用户看到的是一段没写进去的内容，
        // 而它会一直留在那儿，直到下次换选中——那时它才"自己变回去"，看起来像丢了数据。
        Refresh();

        // 错误挂在刷新之后：刷新会把每个字段的值重读一遍，读的同时也清掉了上一次的错误。
        // 先挂错误再刷新的话，挂上去的那条立刻就被刷没了。
        field.Show(Describe(result));
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

    private static string DescribePorts(NodeDef node)
    {
        if (node.Ports.Count == 0)
        {
            return "（没有自定义端口，由布局引擎按形状自动均分）";
        }

        var text = new StringBuilder();

        foreach (var port in node.Ports)
        {
            text.Append(port.Name)
                .Append(" · ").Append(Side(port.Side))
                .Append(" · 偏移 ").Append(port.Offset.ToString("0.##", CultureInfo.InvariantCulture));

            if (port.IsCustom)
            {
                text.Append(" · 自定义");
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string DescribeActions(DiagramDocument document, string nodeId)
    {
        var actions = document.Actions
            .Where(a => string.Equals(a.Target, nodeId, StringComparison.Ordinal))
            .ToList();

        if (actions.Count == 0)
        {
            return "（没有动作）";
        }

        var text = new StringBuilder();

        foreach (var action in actions)
        {
            text.Append(action.Event).Append(" → ").Append(action.Kind).Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string Side(PortSide side) => side switch
    {
        PortSide.Left => "左侧",
        PortSide.Right => "右侧",
        PortSide.Top => "上侧",
        PortSide.Bottom => "下侧",
        _ => side.ToString(),
    };

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
