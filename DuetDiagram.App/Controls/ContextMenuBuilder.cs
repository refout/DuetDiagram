using DuetDiagram.App.ViewModels;

namespace DuetDiagram.App.Services;

/// <summary>
/// 右键菜单上摆哪几条。
/// </summary>
/// <remarks>
/// <para>
/// **条目仍然来自注册表。** 这一层只决定"这一下该摆哪几条、按什么顺序"，
/// 不写任何行为——点下去做什么、什么时候不能点，都在条目自己身上。
/// 在这里再写一遍的话，同一件事会有两种启用判据，而用户看到的是
/// "菜单里能点、快捷键按了没反应"。
/// </para>
/// <para>
/// **空白处与元素上是两套条目。** 空白处是"接下来要画什么"（新建泳道）与
/// "换一批选中"（全选、清空）；元素上是"对这几个东西做什么"（组合、删除）。
/// 合成一套的话，右键一个节点会看到"全选"，而右键空白会看到"删除"。
/// </para>
/// <para>
/// **共用的那三条按标识取。** 全选、清空选择、删除本来就注册在「编辑」档上
/// （菜单栏与工具栏上也有它们），这里按标识把它们取过来，
/// 而不是另注册一份——同一件事有两个条目的话，两处的说法迟早会不一样。
/// </para>
/// </remarks>
public static class ContextMenuBuilder
{
    /// <summary>右键落在空白处时的条目，按摆出来的顺序。</summary>
    /// <remarks>
    /// 泳道可以在空白处建：先划一条空泳道、再把节点拖进去是常见走法。
    /// 另外三条组合都要有成员才有意义，所以只在元素上出现。
    /// </remarks>
    private static readonly string[] OnBlank =
    [
        "edit.select-all",
        "edit.select-none",
        "group.create-lane",
    ];

    /// <summary>右键落在元素上时的条目，按摆出来的顺序。</summary>
    private static readonly string[] OnElement =
    [
        "edit.delete",
        "group.create-group",
        "group.create-lane",
        "group.create-subflow",
        "group.create-combo",
        "group.dissolve",
    ];

    /// <summary>
    /// 这一下该摆哪几条。
    /// </summary>
    /// <param name="context">条目判启用与执行时要看的东西。</param>
    /// <param name="onElement">右键落在元素上还是空白处。</param>
    /// <remarks>
    /// 返回的条目可以被 <see cref="MenuEntry.IsEnabled"/> 判过再显示：
    /// 不能点的那几条也摆出来，并把理由写在上面——藏起来的话，用户会以为
    /// "这里没有这个功能"，而实际上只是此刻缺一个前提。
    /// </remarks>
    public static IReadOnlyList<MenuEntry> Build(MenuContext context, bool onElement)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registry = MenuRegistry.Default;
        var ids = onElement ? OnElement : OnBlank;
        var entries = new List<MenuEntry>(ids.Length);

        foreach (var id in ids)
        {
            // 取不到就直接抛：那说明标识写错了，而写错的表现是"菜单上少了一条"，
            // 看起来像"这一轮没做"。
            entries.Add(registry.Find(id)
                ?? throw new InvalidOperationException($"右键菜单引了一条不存在的条目：{id}"));
        }

        return entries;
    }
}
