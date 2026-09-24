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
    public static string? NoSelection(MenuContext context, string what) =>
        context.HasSelection ? null : $"先选中{what}";

    /// <summary>既要能写、又要有选中，两样都挡住。</summary>
    public static string? WriteToSelection(MenuContext context, string what) =>
        ReadOnly(context) ?? NoSelection(context, what);

    /// <summary>既要能写、又要选中至少两个元素。对齐与同层都至少两个成员。</summary>
    public static string? WriteToGroup(MenuContext context, string what) =>
        ReadOnly(context) ?? (context.Selected.Count < 2 ? $"先选中两个以上{what}" : null);
}

/// <summary>文件那一档。这一轮有"再开一个窗口"、"存盘"与"存为模板"三件。</summary>
internal static class FileEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "file.new-window",
            MenuGroups.File,
            "新建窗口",
            "Ctrl+N",
            MenuSurface.Menu,
            _ => null,
            context => context.Window.OpenAnotherWindow()));

        registry.Add(new MenuEntry(
            "file.save",
            MenuGroups.File,
            "保存",
            "Ctrl+S",
            MenuSurface.Menu,
            context => context.IsReadOnly
                ? ErrorPresenterTable.For(ErrorCodes.DocumentReadOnly).Message
                : context.Window.DocumentPath is null ? "这份文档没有文件，存不了" : null,
            context => context.Window.Save()));

        // 「存为模板」与「保存」都写文件，但写的是两样东西：一个是这份文档，一个是可复用的一段。
        // 名字由会话那一层取（文档标识），界面上没有问名字的地方——多一个输入框就多一处
        // 要处理"用户没填"的地方，而这一轮要验的是模板进得去、出得来。
        registry.Add(new MenuEntry(
            "file.save-template",
            MenuGroups.File,
            "存为模板",
            null,
            MenuSurface.Menu,
            context => MenuRefusals.WriteToSelection(context, "要存成模板的元素"),
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
            "撤销",
            "Ctrl+Z",
            MenuSurface.Both,
            context => context.CanUndo ? null : "没有可撤销的操作",
            context => context.Session.Undo()));

        registry.Add(new MenuEntry(
            "edit.redo",
            MenuGroups.Edit,
            "重做",
            "Ctrl+Y",
            MenuSurface.Both,
            context => context.CanRedo ? null : "没有可重做的操作",
            context => context.Session.Redo()));

        registry.Add(new MenuEntry(
            "edit.delete",
            MenuGroups.Edit,
            "删除",
            "Delete",
            MenuSurface.Both | MenuSurface.Context,
            context => MenuRefusals.WriteToSelection(context, "要删的元素"),
            context => context.Session.DeleteSelection()));

        registry.Add(new MenuEntry(
            "edit.select-all",
            MenuGroups.Edit,
            "全选",
            "Ctrl+A",
            MenuSurface.Both | MenuSurface.Context,
            context => context.Session.AllNodeIds.Count == 0 ? "文档里还没有元素" : null,
            context => context.Session.SetSelection(context.Session.AllNodeIds)));

        registry.Add(new MenuEntry(
            "edit.select-none",
            MenuGroups.Edit,
            "清空选择",
            null,
            MenuSurface.Both | MenuSurface.Context,
            context => context.HasSelection ? null : "现在没有选中东西",
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
            "同层",
            null,
            MenuSurface.Both,
            context => MenuRefusals.WriteToGroup(context, "节点"),
            context => context.Session.AddConstraint(LayoutConstraintSpec.SameRank(context.Selected))));

        registry.Add(new MenuEntry(
            "align.align",
            MenuGroups.Align,
            "对齐",
            null,
            MenuSurface.Both,
            context => MenuRefusals.WriteToGroup(context, "节点"),
            context => context.Session.AddConstraint(LayoutConstraintSpec.Align(context.Selected))));
    }
}

/// <summary>布局那一档：主方向、间距与一次显式重排。</summary>
internal static class LayoutEntries
{
    public static void Register(MenuRegistry registry)
    {
        AddDirection(registry, "lr", "从左到右", Direction.LR);
        AddDirection(registry, "tb", "从上到下", Direction.TB);
        AddDirection(registry, "rl", "从右到左", Direction.RL);
        AddDirection(registry, "bt", "从下到上", Direction.BT);

        registry.Add(new MenuEntry(
            "layout.spacing-tight",
            MenuGroups.Layout,
            "收紧间距",
            null,
            MenuSurface.Both,
            MenuRefusals.ReadOnly,
            context => context.Session.NudgeSpacing(0.8)));

        registry.Add(new MenuEntry(
            "layout.spacing-loose",
            MenuGroups.Layout,
            "放宽间距",
            null,
            MenuSurface.Both,
            MenuRefusals.ReadOnly,
            context => context.Session.NudgeSpacing(1.25)));

        registry.Add(new MenuEntry(
            "layout.relayout",
            MenuGroups.Layout,
            "重排",
            "F5",
            MenuSurface.Both,
            _ => null,
            context => context.Session.RetryLayout()));
    }

    private static void AddDirection(MenuRegistry registry, string id, string label, Direction direction) =>
        registry.Add(new MenuEntry(
            $"layout.direction-{id}",
            MenuGroups.Layout,
            label,
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
            "性能诊断面板",
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
/// 这一轮只有一条占位条目，它永远给一句"还没接上"：能用的导出只有工具那条通路，
/// 而界面上的导出要一个选文件、选格式、选范围的地方，那是后面几条的事。
/// 留一条一直拒绝的条目而不是留一个空档，是因为空档看起来像"这一轮没做"，
/// 而拒绝能说清是"还没做"还是"做不了"。
/// </remarks>
internal static class ExportEntries
{
    private const string Reason = "界面上的导出还没接上：要选文件、选格式、选范围，那三样在后面的任务里";

    public static void Register(MenuRegistry registry) =>
        registry.Add(new MenuEntry(
            "export.dialog",
            MenuGroups.Export,
            "导出…",
            null,
            MenuSurface.Both,
            _ => Reason,
            _ => { }));
}
