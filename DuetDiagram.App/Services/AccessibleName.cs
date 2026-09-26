using Avalonia;
using Avalonia.Automation;
using DuetDiagram.App.Resources;

namespace DuetDiagram.App.Services;

/// <summary>
/// 给控件挂可访问名称与说明。
/// </summary>
/// <remarks>
/// <para>
/// 屏幕阅读器读的是这个名字，不是控件在屏幕上的位置。一个没有名字的控件在阅读器里
/// 等于不存在——用户能 Tab 到它，却听不到它是干什么的，只能靠猜。
/// </para>
/// <para>
/// **名字为空直接抛。** 让它悄悄过去的话，"每个控件都有名字"这件事会在某个
/// 分支上悄悄不成立，而界面上看不出任何异常；抛出来则当场暴露，改的人是知道原因才改的。
/// </para>
/// <para>
/// 名字要说清"这是哪一个、做什么"，而不是"按钮"两个字：一屏上有二十颗按钮，
/// 读到二十遍"按钮"与什么都没读到是一回事。
/// </para>
/// </remarks>
internal static class AccessibleName
{
    /// <summary>
    /// 挂上名字与说明。
    /// </summary>
    /// <param name="control">要挂的控件。</param>
    /// <param name="name">名字。说出这个控件是干什么的，不能为空。</param>
    /// <param name="help">
    /// 补充说明，通常是提示条上那句话（此刻为什么点不动、有没有快捷键）。
    /// 传空表示这条控件没有额外要说的，此时清掉上一次留下的说明——
    /// 留着的话，按钮已经能点了，阅读器还在念上一轮那句"现在不能点"。
    /// </param>
    /// <remarks>
    /// 名字与说明一起设：启用状态一变，说明就要跟着变，两者分开设的话
    /// 迟早会出现"名字是新的、说明是旧的"这种半截状态。
    /// </remarks>
    public static T Set<T>(T control, string name, string? help = null)
        where T : StyledElement
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("可访问名称不能为空：没有名字的控件在屏幕阅读器里等于不存在", nameof(name));
        }

        AutomationProperties.SetName(control, name);
        AutomationProperties.SetHelpText(control, string.IsNullOrWhiteSpace(help) ? null : help);

        return control;
    }

    /// <summary>
    /// 注册表条目在"档名看不见的那两处界面"上的名字。
    /// </summary>
    /// <remarks>
    /// 工具栏与右键菜单不显示档名，只有菜单栏的顶级菜单是档名本身。不带上档名的话，
    /// 「同层」这两个字说明不了它是"让两个节点排在同一层"，读起来像一个不知道作用于什么的名词。
    /// 档名已经出现在标签里的那几条（例如「导出」档里的「导出…」）会略显重复，
    /// 但统一比"看着重复就去掉"更好：名字的构造方式必须是可预期的，
    /// 否则读屏用户听到的名字会随标签措辞而变。
    /// </remarks>
    public static string ForEntry(MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return Strings.EntryName(MenuGroups.Display(entry.Group), entry.Label);
    }
}
