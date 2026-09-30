using System.Globalization;
using System.Resources;

namespace DuetDiagram.App.Resources;

/// <summary>
/// 界面文字的取用入口。一处文字一个属性，属性的值来自资源文件。
/// </summary>
/// <remarks>
/// <para>
/// **语言是显式指定的，不走运行期文化。** 这一份程序集在不变量全球化下运行，
/// 具名文化建不出来，卫星程序集也就永远选不出来；语言只能由调用方点名。
/// </para>
/// <para>
/// **缺的键回落中性资源。** 第二语言不必一次补齐——没翻到的那几条会显示中性资源里的那句，
/// 而不是变成空白或键名。中性资源里也没有的键才算错，直接抛。
/// </para>
/// <para>
/// **属性是编译期接口，资源文件是运行期接口。** 调用方写属性名，写错编译不过；
/// 键写在资源文件里，翻译者按键取那一句。两边由一条用例逐键对照。
/// </para>
/// </remarks>
public static class Strings
{
    /// <summary>中性资源那一档。它装的是中文，也是回落的那一份。</summary>
    public const string Chinese = "zh-CN";

    /// <summary>第二语言。它的键可以缺，缺的回落中性资源。</summary>
    public const string English = "en";

    private const string NeutralBase = "DuetDiagram.App.Resources.Strings";
    private const string EnglishBase = "DuetDiagram.App.Resources.Strings.en";

    private static readonly ResourceManager NeutralResources = new(NeutralBase, typeof(Strings).Assembly);
    private static readonly ResourceManager EnglishResources = new(EnglishBase, typeof(Strings).Assembly);

    private static string _language = Chinese;

    /// <summary>当前这一份文字用的是哪种语言。</summary>
    /// <remarks>
    /// 界面文字在窗口搭起来的时候取一次，所以换语言要在开窗口之前设。
    /// 已经开着的窗口不会自己重取——换语言是"下一次打开是什么样"，不是"当场改写"。
    /// </remarks>
    public static string Language
    {
        get => _language;
        set
        {
            if (Languages.Contains(value, StringComparer.Ordinal) is false)
            {
                throw new ArgumentException($"不认识的语言代码：{value}", nameof(value));
            }

            _language = value;
        }
    }

    /// <summary>这一版认得的语言代码。</summary>
    public static IReadOnlyList<string> Languages { get; } = [Chinese, English];

    /// <summary>
    /// 条目后面跟着的那个快捷键，例如"撤销（Ctrl+Z）"。
    /// </summary>
    /// <remarks>
    /// 括号与前后次序都是语言的一部分：换一种语言可能写成"Ctrl+Z Undo"，
    /// 也可能把括号换成别的。所以整句是一个格式串，不在调用处拼。
    /// </remarks>
    public static string MenuShortcut(string label, string shortcut) =>
        Format("menu.shortcut", label, shortcut);

    /// <summary>
    /// 档名看不见的那两处界面上，条目该被念成什么。
    /// </summary>
    /// <remarks>
    /// 工具栏与右键菜单不显示档名，只有菜单栏的顶级菜单是档名本身。不带上档名的话，
    /// 「同层」这两个字说明不了它是"让两个节点排在同一层"。分隔符也是语言的一部分：
    /// 换一种语言可能用冒号加空格，也可能换一个写法。
    /// </remarks>
    public static string EntryName(string group, string label) =>
        Format("menu.entry-name", group, label);

    /// <summary>
    /// 按键取一句。当前语言里没有就回落中性资源；两边都没有直接抛。
    /// </summary>
    /// <remarks>
    /// 两边都没有时不返回键名：键名会原样显示在界面上，而那种"界面里出现一串英文键"
    /// 看起来像界面坏了，实际是漏了一条资源——抛出来才能当场发现。
    /// </remarks>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var manager = string.Equals(_language, English, StringComparison.Ordinal)
            ? EnglishResources
            : NeutralResources;

        return manager.GetString(key, CultureInfo.InvariantCulture)
            ?? NeutralResources.GetString(key, CultureInfo.InvariantCulture)
            ?? throw new MissingManifestResourceException($"资源里没有这个键：{key}");
    }

    /// <summary>
    /// 带占位符的那几句。拼句子用格式串，不把前后两截各自拼出来。
    /// </summary>
    /// <remarks>
    /// 各拼一截的话，换一种语序就拼不成句子了——而语序正是翻译要改的东西。
    /// 格式按不变量文化算：参数都是标识、数字与已经取好的文字，不需要按语言变格式。
    /// </remarks>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arguments);

    #region 菜单与工具栏的档名

    public static string MenuGroupFile => Get("menu.groupname.file");

    public static string MenuGroupEdit => Get("menu.groupname.edit");

    public static string MenuGroupAlign => Get("menu.groupname.align");

    public static string MenuGroupLayout => Get("menu.groupname.layout");

    public static string MenuGroupView => Get("menu.groupname.view");

    public static string MenuGroupExport => Get("menu.groupname.export");

    public static string MenuGroupGroup => Get("menu.groupname.group");

    #endregion

    #region 菜单与工具栏的条目

    public static string MenuFileNewWindow => Get("menu.file.new-window");

    public static string MenuFileOpen => Get("menu.file.open");

    public static string MenuFileImport => Get("menu.file.import");

    public static string MenuFileSave => Get("menu.file.save");

    public static string MenuFileSaveTemplate => Get("menu.file.save-template");

    public static string MenuEditUndo => Get("menu.edit.undo");

    public static string MenuEditRedo => Get("menu.edit.redo");

    public static string MenuEditDelete => Get("menu.edit.delete");

    public static string MenuEditSelectAll => Get("menu.edit.select-all");

    public static string MenuEditSelectNone => Get("menu.edit.select-none");

    public static string MenuAlignSameRank => Get("menu.align.same-rank");

    public static string MenuAlignAlign => Get("menu.align.align");

    public static string MenuLayoutDirectionLr => Get("menu.layout.direction-lr");

    public static string MenuLayoutDirectionTb => Get("menu.layout.direction-tb");

    public static string MenuLayoutDirectionRl => Get("menu.layout.direction-rl");

    public static string MenuLayoutDirectionBt => Get("menu.layout.direction-bt");

    public static string MenuLayoutSpacingTight => Get("menu.layout.spacing-tight");

    public static string MenuLayoutSpacingLoose => Get("menu.layout.spacing-loose");

    public static string MenuLayoutRelayout => Get("menu.layout.relayout");

    public static string MenuViewDiagnostics => Get("menu.view.diagnostics");

    public static string MenuExportDialog => Get("menu.export.dialog");

    public static string MenuGroupCreateGroup => Get("menu.group.create-group");

    public static string MenuGroupCreateLane => Get("menu.group.create-lane");

    public static string MenuGroupCreateSubflow => Get("menu.group.create-subflow");

    public static string MenuGroupCreateCombo => Get("menu.group.create-combo");

    public static string MenuGroupDissolve => Get("menu.group.dissolve");

    #endregion

    #region 条目不能点时的理由

    public static string RefusalUndo => Get("menu.refusal.undo");

    public static string RefusalRedo => Get("menu.refusal.redo");

    public static string RefusalDelete => Get("menu.refusal.delete");

    public static string RefusalSelectAll => Get("menu.refusal.select-all");

    public static string RefusalSelectNone => Get("menu.refusal.select-none");

    public static string RefusalAlign => Get("menu.refusal.align");

    public static string RefusalSaveNoFile => Get("menu.refusal.save-no-file");

    public static string RefusalSaveTemplate => Get("menu.refusal.save-template");

    public static string RefusalCreateGroup => Get("menu.refusal.create-group");

    public static string RefusalCreateSubflow => Get("menu.refusal.create-subflow");

    public static string RefusalCreateCombo => Get("menu.refusal.create-combo");

    public static string RefusalDissolve => Get("menu.refusal.dissolve");

    #endregion

    #region 面板与画布

    public static string PanelLayersTitle => Get("panel.layers.title");

    public static string PanelShapesTitle => Get("panel.shapes.title");

    public static string PanelTemplatesTitle => Get("panel.templates.title");

    public static string PanelPaletteTitle => Get("panel.palette.title");

    public static string PanelPresetsTitle => Get("panel.presets.title");

    public static string PanelPropertiesEmpty => Get("panel.properties.empty");

    public static string PanelTemplatesEmpty => Get("panel.templates.empty");

    public static string PanelTemplatesFailures => Get("panel.templates.failures");

    public static string CanvasEmpty => Get("canvas.empty");

    public static string DiffTitle => Get("diff.title");

    public static string DiffEmpty => Get("diff.empty");

    public static string DiffExpand => Get("diff.expand");

    public static string DiffCollapse => Get("diff.collapse");

    #endregion

    #region 就地编辑器

    public static string RichTextSize => Get("richtext.size");

    public static string RichTextColor => Get("richtext.color");

    public static string RichTextCancel => Get("richtext.cancel");

    public static string RichTextCommit => Get("richtext.commit");

    #endregion

    #region 对话框

    public static string ImportTitle => Get("import.title");

    public static string ImportClose => Get("import.close");

    public static string OpenTitle => Get("open.title");

    public static string ExportTitle => Get("export.title");

    public static string ExportDialogTitle => Get("export.dialog.title");

    public static string ExportDialogFormat => Get("export.dialog.format");

    public static string ExportDialogRange => Get("export.dialog.range");

    public static string ExportDialogRangeContent => Get("export.dialog.range.content");

    public static string ExportDialogRangePage => Get("export.dialog.range.page");

    public static string ExportDialogScale => Get("export.dialog.scale");

    public static string ExportDialogScaleInvalid => Get("export.dialog.scale-invalid");

    public static string ExportDialogConfirm => Get("export.dialog.confirm");

    public static string ExportDialogCancel => Get("export.dialog.cancel");

    public static string LayoutFailureTitle => Get("layout.failure.title");

    public static string LayoutFailureRetry => Get("layout.failure.retry");

    public static string LayoutFailureManual => Get("layout.failure.manual");

    public static string LayoutFailureSimplify => Get("layout.failure.simplify");

    public static string SidecarTitle => Get("sidecar.title");

    public static string SidecarBody => Get("sidecar.body");

    public static string SidecarRestore => Get("sidecar.restore");

    public static string SidecarDiscard => Get("sidecar.discard");

    public static string DiagnosticsClose => Get("diagnostics.close");

    #endregion
}
