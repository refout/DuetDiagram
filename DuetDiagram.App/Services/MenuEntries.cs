using DuetDiagram.App.Resources;
using DuetDiagram.App.ViewModels;
using DuetDiagram.Core.Commands;
using DuetDiagram.Core.Model;

namespace DuetDiagram.App.Services;

/// <summary>
/// 几条反复用到的拒绝理由。
/// </summary>
/// <remarks>
/// 合成一处是因为它们必须一致：只读这一条在四个档里都会出现，
/// 各写一遍的话，四个入口对同一件事会给出四句不同的话，而用户以为遇到的是四个问题。
/// 只读那一句取自错误码呈现表，所以它与命令被拒时说的那句是同一句。
/// </remarks>
internal static class MenuRefusals
{
    /// <summary>只读时不许改。理由与命令层拒绝写入时给的那一句相同。</summary>
    public static string? ReadOnly(MenuContext context) =>
        context.IsReadOnly ? ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message : null;

    /// <summary>没选中东西时，这条做不了。</summary>
    /// <remarks>
    /// 理由整句由调用方给，不在这里拼"先选中"加一个宾语：拼出来的话，
    /// 换一种语序（宾语在前）就拼不成句子了，而语序正是翻译要改的东西。
    /// </remarks>
    public static string? NoSelection(MenuContext context, string reason) =>
        context.HasSelection ? null : reason;

    /// <summary>既要能写、又要有选中，两样都挡住。</summary>
    public static string? WriteToSelection(MenuContext context, string reason) =>
        ReadOnly(context) ?? NoSelection(context, reason);

    /// <summary>既要能写、又要选中至少两个元素。对齐与同层都至少两个成员。</summary>
    public static string? WriteToGroup(MenuContext context, string reason) =>
        ReadOnly(context) ?? (context.Selected.Count < 2 ? reason : null);
}

/// <summary>文件那一档。这一轮有"再开一个窗口"、"存盘"、"存为模板"与"导入"四件。</summary>
internal static class FileEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "file.new-window",
            MenuGroups.File,
            "menu.file.new-window",
            "Ctrl+N",
            MenuSurface.Menu,
            _ => null,
            context => context.Window.OpenAnotherWindow()));

        // 「导入」与「保存」都在文件那一档，但一个把外面的东西拿进来、一个把这份写出去。
        // 它排在保存前面：这一档的第一件事是"从哪儿来"。
        registry.Add(new MenuEntry(
            "file.import",
            MenuGroups.File,
            "menu.file.import",
            null,
            MenuSurface.Menu,
            MenuRefusals.ReadOnly,
            context => context.Window.BeginImport()));

        registry.Add(new MenuEntry(
            "file.save",
            MenuGroups.File,
            "menu.file.save",
            "Ctrl+S",
            MenuSurface.Menu,
            context => context.IsReadOnly
                ? ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message
                : context.Window.DocumentPath is null ? Strings.RefusalSaveNoFile : null,
            context => context.Window.Save()));

        // 「存为模板」与「保存」都写文件，但写的是两样东西：一个是这份文档，一个是可复用的一段。
        // 名字由会话那一层取（文档标识），界面上没有问名字的地方——多一个输入框就多一处
        // 要处理"用户没填"的地方，而这一轮要验的是模板进得去、出得来。
        registry.Add(new MenuEntry(
            "file.save-template",
            MenuGroups.File,
            "menu.file.save-template",
            null,
            MenuSurface.Menu,
            context => MenuRefusals.WriteToSelection(context, Strings.RefusalSaveTemplate),
            context => context.Window.Templates.SaveSelection()));
    }
}

/// <summary>编辑那一档：撤销、重做、删除与两个选择动作。</summary>
internal static class EditEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "edit.undo",
            MenuGroups.Edit,
            "menu.edit.undo",
            "Ctrl+Z",
            MenuSurface.Both,
            context => context.CanUndo ? null : Strings.RefusalUndo,
            context => context.Session.Undo()));

        registry.Add(new MenuEntry(
            "edit.redo",
            MenuGroups.Edit,
            "menu.edit.redo",
            "Ctrl+Y",
            MenuSurface.Both,
            context => context.CanRedo ? null : Strings.RefusalRedo,
            context => context.Session.Redo()));

        registry.Add(new MenuEntry(
            "edit.delete",
            MenuGroups.Edit,
            "menu.edit.delete",
            "Delete",
            MenuSurface.Both | MenuSurface.Context,
            context => MenuRefusals.WriteToSelection(context, Strings.RefusalDelete),
            context => context.Session.DeleteSelection()));

        registry.Add(new MenuEntry(
            "edit.select-all",
            MenuGroups.Edit,
            "menu.edit.select-all",
            "Ctrl+A",
            MenuSurface.Both | MenuSurface.Context,
            context => context.Session.AllNodeIds.Count == 0 ? Strings.RefusalSelectAll : null,
            context => context.Session.SetSelection(context.Session.AllNodeIds)));

        registry.Add(new MenuEntry(
            "edit.select-none",
            MenuGroups.Edit,
            "menu.edit.select-none",
            null,
            MenuSurface.Both | MenuSurface.Context,
            context => context.HasSelection ? null : Strings.RefusalSelectNone,
            context => context.Session.SetSelection([])));
    }
}

/// <summary>对齐那一档。两种约束的参数形状相同，都是"对选中的这几个"，所以只有两条。</summary>
internal static class AlignEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "align.same-rank",
            MenuGroups.Align,
            "menu.align.same-rank",
            null,
            MenuSurface.Both,
            context => MenuRefusals.WriteToGroup(context, Strings.RefusalAlign),
            context => context.Session.AddConstraint(LayoutConstraintSpec.SameRank(context.Selected))));

        registry.Add(new MenuEntry(
            "align.align",
            MenuGroups.Align,
            "menu.align.align",
            null,
            MenuSurface.Both,
            context => MenuRefusals.WriteToGroup(context, Strings.RefusalAlign),
            context => context.Session.AddConstraint(LayoutConstraintSpec.Align(context.Selected))));
    }
}

/// <summary>布局那一档：主方向、间距与一次显式重排。</summary>
internal static class LayoutEntries
{
    public static void Register(MenuRegistry registry)
    {
        AddDirection(registry, "lr", "menu.layout.direction-lr", Direction.LR);
        AddDirection(registry, "tb", "menu.layout.direction-tb", Direction.TB);
        AddDirection(registry, "rl", "menu.layout.direction-rl", Direction.RL);
        AddDirection(registry, "bt", "menu.layout.direction-bt", Direction.BT);

        registry.Add(new MenuEntry(
            "layout.spacing-tight",
            MenuGroups.Layout,
            "menu.layout.spacing-tight",
            null,
            MenuSurface.Both,
            MenuRefusals.ReadOnly,
            context => context.Session.NudgeSpacing(0.8)));

        registry.Add(new MenuEntry(
            "layout.spacing-loose",
            MenuGroups.Layout,
            "menu.layout.spacing-loose",
            null,
            MenuSurface.Both,
            MenuRefusals.ReadOnly,
            context => context.Session.NudgeSpacing(1.25)));

        registry.Add(new MenuEntry(
            "layout.relayout",
            MenuGroups.Layout,
            "menu.layout.relayout",
            "F5",
            MenuSurface.Both,
            _ => null,
            context => context.Session.RetryLayout()));
    }

    private static void AddDirection(MenuRegistry registry, string id, string labelKey, Direction direction) =>
        registry.Add(new MenuEntry(
            $"layout.direction-{id}",
            MenuGroups.Layout,
            labelKey,
            null,
            MenuSurface.Both,
            MenuRefusals.ReadOnly,
            context => context.Session.SetDirection(direction)));
}

/// <summary>视图那一档。这一轮只有诊断面板的开关。</summary>
internal static class ViewEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "view.diagnostics",
            MenuGroups.View,
            "menu.view.diagnostics",
            "Ctrl+Shift+P",
            MenuSurface.Menu,
            _ => null,
            context => context.Model.Diagnostics.Toggle()));
    }
}

/// <summary>
/// 导出那一档。
/// </summary>
/// <remarks>
/// <para>
/// 这一轮只有一条占位条目，它永远给一句"还没接上"。能导出的是工具那条通路
/// （<c>diagram_export</c>）与脱屏自检，两者都直接消费绘制列表；
/// 界面上的导出还差三样：选文件、选格式、选范围——那三样在后面的任务里，
/// 而格式那一样要等到四种格式都有了才值得做（Mermaid、SVG、PNG 与 PDF 现在都有了）。
/// </para>
/// <para>
/// 留一条一直拒绝的条目而不是留一个空档，是因为空档看起来像"这一轮没做"，
/// 而拒绝能说清是"还没做"还是"做不了"。理由里不再提"导出还没实现"——
/// 导出本身已经能用了，缺的只是界面这一层。
/// </para>
/// </remarks>
internal static class ExportEntries
{
    public static void Register(MenuRegistry registry) =>
        registry.Add(new MenuEntry(
            "export.dialog",
            MenuGroups.Export,
            "menu.export.dialog",
            null,
            MenuSurface.Both,
            _ => Strings.RefusalExport,
            _ => { }));
}
