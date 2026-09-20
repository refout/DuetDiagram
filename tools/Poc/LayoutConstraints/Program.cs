namespace DuetDiagram.Poc.LayoutConstraints;

/// <summary>
/// 验证布局引擎之外的约束补齐方案。
/// </summary>
/// <remarks>
/// 用可计算的不变量检验补齐结果：锚点偏差、残留重叠、同层极差、节点守恒、分阶段耗时。
/// 退出码表达整体结果，便于放进持续集成。
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("布局约束补齐方案验证");
        Console.WriteLine("覆盖：同层组收缩展开、锚点回填、重叠松弛");

        return InvariantChecks.RunAll();
    }
}
