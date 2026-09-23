namespace DuetDiagram.App.Services;

/// <summary>
/// 只在右键菜单里出现的那一档：四类组合，以及解散。
/// </summary>
/// <remarks>
/// <para>
/// **四类各有各的条目，不合成一个「新建组合」。** 分组、泳道、子流程、组合框的参数与语义
/// 都不同（泳道的成员先后是条带顺序、子流程可以折叠、组合框不参与布局），
/// 合成一条之后那些参数就没地方给——现在没有，将来也不会有。
/// </para>
/// <para>
/// **这是「组合」在界面上唯一的入口。** 命令层早就齐了（建、移入、解散三条），
/// 而在此之前只有模型能建分组，人不能。
/// </para>
/// <para>
/// **空成员的泳道是正当的。** 在空白处右键建一条泳道、再把节点拖进去，
/// 是先划地方后放东西的常见走法；分组、子流程、组合框没有成员就什么也框不住，
/// 所以那三条要求先选中东西。
/// </para>
/// </remarks>
internal static class ContextEntries
{
    public static void Register(MenuRegistry registry)
    {
        registry.Add(new MenuEntry(
            "group.create-group",
            MenuGroups.Group,
            "建分组",
            null,
            MenuSurface.Context,
            context => MenuRefusals.WriteToSelection(context, "要包进分组的元素"),
            context => context.Session.CreateGroup(context.Selected)));

        registry.Add(new MenuEntry(
            "group.create-lane",
            MenuGroups.Group,
            "建泳道",
            null,
            MenuSurface.Context,

            // 泳道可以在空白处建：一条空泳道先划出来，再把节点拖进去是常见走法。
            // 而"选中了几个"这件事决定它建成空的还是带上那几个成员，见执行体。
            MenuRefusals.ReadOnly,
            context => context.Session.CreateLane(context.Selected)));

        registry.Add(new MenuEntry(
            "group.create-subflow",
            MenuGroups.Group,
            "建子流程",
            null,
            MenuSurface.Context,
            context => MenuRefusals.WriteToSelection(context, "要包进子流程的元素"),
            context => context.Session.CreateSubflow(context.Selected)));

        registry.Add(new MenuEntry(
            "group.create-combo",
            MenuGroups.Group,
            "建组合框",
            null,
            MenuSurface.Context,
            context => MenuRefusals.WriteToSelection(context, "要圈进框里的元素"),
            context => context.Session.CreateCombo(context.Selected)));

        registry.Add(new MenuEntry(
            "group.dissolve",
            MenuGroups.Group,
            "解散这一组",
            null,
            MenuSurface.Context,
            context => MenuRefusals.ReadOnly(context) ?? Dissolvable(context),
            context => context.Session.DissolveComposite(context.Target!)));
    }

    /// <summary>
    /// 右键落在一个组合上。
    /// </summary>
    /// <remarks>
    /// 解散只对组合有意义，而判据看的是**右键落在谁身上**、不是"选中了谁"：
    /// 组合现在选不中（选中集合里只有节点），按选中判的话这一条永远点不动。
    /// </remarks>
    private static string? Dissolvable(MenuContext context) =>
        context.Target is { } target
        && context.Session.Document.Composites.Any(
            composite => string.Equals(composite.Id, target, StringComparison.Ordinal))
            ? null
            : "这一条要对准一个组合右键";
}
