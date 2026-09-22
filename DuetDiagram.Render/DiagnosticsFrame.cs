namespace DuetDiagram.Render;

/// <summary>
/// 一帧的测量结果。
/// </summary>
/// <remarks>
/// <para>
/// 分成三段而不是只给一个总数：出问题时第一件要判断的是**该改哪儿**，
/// 而"这一帧花了 20 毫秒"回答不了它。剔除贵说明索引或视口算得不对，
/// 光栅化贵说明该少画东西，两者处置完全不同。
/// </para>
/// <para>
/// 三段之和略小于总数，差额是进函数、压栈与两次取时钟本身。差得明显多时
/// 说明有活儿落在三段之外——那时该做的是补一段，而不是把这个差额调大。
/// </para>
/// <para>
/// 值类型。每帧都要造一个，做成类的话每帧一次分配，而垃圾回收会出现在
/// 面板显示的数字里——量的是自己。
/// </para>
/// </remarks>
/// <param name="Total">这一帧从开始到画完的毫秒数。</param>
/// <param name="Cull">定下这一帧画哪几条所用的毫秒数，含换档判定与索引查询。</param>
/// <param name="Raster">执行绘制指令所用的毫秒数，含铺底与裁剪。</param>
/// <param name="Drawn">这一帧执行了多少条指令。</param>
/// <param name="Culled">这一帧剔掉了多少条指令。</param>
/// <param name="Mode">这一帧实际走的档位。</param>
public readonly record struct DiagnosticsFrame(
    double Total,
    double Cull,
    double Raster,
    int Drawn,
    int Culled,
    RenderMode Mode);

/// <summary>
/// 不按帧计的那几段重活。
/// </summary>
/// <remarks>
/// 布局、绘制列表构建与哈希都是一次性的：换一份文档才算一次，之后每帧都在复用结果。
/// 把它们塞进按帧的窗口里毫无意义——那会让"这一帧的布局花了多久"这种问题
/// 看起来有个答案，而答案其实是几十帧之前的。
/// </remarks>
public enum DiagnosticsStage
{
    /// <summary>求解布局。</summary>
    Layout,

    /// <summary>把文档与布局结果翻译成绘制列表。</summary>
    DrawList,

    /// <summary>算结构哈希或视觉哈希。</summary>
    Hash,
}
